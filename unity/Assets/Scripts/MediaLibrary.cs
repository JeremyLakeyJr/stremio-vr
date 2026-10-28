using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR {
public sealed class MediaLibrary : MonoBehaviour {
    public event Action<string> Status;
    public string CoreEndpoint; bool coreBusy;
    public readonly List<JObject> Addons = new List<JObject>();
    public IEnumerator Request(string url, Action<JObject> success) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { Status?.Invoke("Use an HTTP(S) URL"); yield break; }
        using (var request = UnityWebRequest.Get(url)) {
            request.timeout = 20;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { Status?.Invoke("Add-on request failed: " + request.error); yield break; }
            JObject data;
            try { data = JObject.Parse(request.downloadHandler.text); } catch (Exception) { Status?.Invoke("Add-on returned invalid JSON"); yield break; }
            try { success(data); } catch (Exception error) { Status?.Invoke("Unsupported add-on response: " + error.Message); }
        }
    }
    public void Install(string url, Action changed) {
        url = url.Trim().Replace("stremio://", "https://");
        string endpoint; try { endpoint=AddonEndpoint(url); } catch (Exception error) { Status?.Invoke(error.Message); return; }
        StartCoroutine(Request(url, manifest => {
            if (manifest["id"] == null || manifest["resources"] == null) throw new Exception("Missing manifest id/resources");
            Action<JObject> accept = validated => {
                validated["endpoint"] = endpoint;
                Addons.RemoveAll(a => (string)a["endpoint"] == endpoint); Addons.Add(validated); changed();
            };
            if (string.IsNullOrEmpty(CoreEndpoint)) accept(manifest);
            else StartCoroutine(CoreRequest(new JObject { ["operation"] = "manifest", ["manifest"] = manifest }, accept));
        }));
    }
    public void ValidateInstalled() { StartCoroutine(ValidateAll()); }
    IEnumerator ValidateAll() {
        foreach (var addon in Addons.ToArray()) { var manifest=(JObject)addon.DeepClone(); manifest.Remove("endpoint"); yield return CoreRequest(new JObject { ["operation"] = "manifest", ["manifest"] = manifest }, validated => { }); }
    }
    public static JObject AccountAddon(JObject descriptor) {
        string url=Http((string)descriptor["transportUrl"]);
        string endpoint=AddonEndpoint(url);
        var manifest=descriptor["manifest"] as JObject;
        if(manifest?["id"]==null || !(manifest["resources"] is JArray)) throw new Exception("Invalid account add-on manifest");
        var result=(JObject)manifest.DeepClone(); result["endpoint"]=endpoint; return result;
    }
    public void Catalog(JObject addon, JObject catalog, Action<JArray> loaded) {
        StartCoroutine(Request(ResourceUrl((string)addon["endpoint"], "catalog/" + Escape((string)catalog["type"]) + "/" + Escape((string)catalog["id"]) + ".json"), data => loaded(data["metas"] as JArray ?? new JArray())));
    }
    public void Episodes(JObject addon, string type, string id, Action<JArray> loaded) {
        StartCoroutine(Request(ResourceUrl((string)addon["endpoint"], "meta/" + Escape(type) + "/" + Escape(id) + ".json"), data => loaded(data["meta"]?["videos"] as JArray ?? new JArray())));
    }
    public void Streams(string type, string id, Action<JArray> loaded) { StartCoroutine(AllStreams(type, id, loaded)); }
    IEnumerator AllStreams(string type, string id, Action<JArray> loaded) {
        var results = new JArray();
        foreach (var addon in Addons.ToArray()) {
            bool supported = false;
            foreach (var resource in addon["resources"] as JArray ?? new JArray()) if ((resource.Type == JTokenType.String ? (string)resource : (string)resource["name"]) == "stream") supported = true;
            if (!supported) continue;
            yield return Request(ResourceUrl((string)addon["endpoint"], "stream/" + Escape(type) + "/" + Escape(id) + ".json"), data => {
                foreach (var stream in data["streams"] as JArray ?? new JArray()) results.Add(stream.DeepClone());
            });
        }
        loaded(results);
    }
    public void ResolveStream(JObject stream, string service, Action<string> resolved) {
        StartCoroutine(CoreRequest(new JObject { ["operation"] = "resolve", ["stream"] = stream, ["streamingService"] = service }, data => resolved(Http((string)data["url"]))));
    }
    IEnumerator CoreRequest(JObject data, Action<JObject> success) {
        if (string.IsNullOrEmpty(CoreEndpoint)) { Status?.Invoke("Connect to the room server to use Stremio core"); yield break; }
        while(coreBusy) yield return null; coreBusy=true;
        try { using (var request = new UnityWebRequest(CoreEndpoint, "POST")) {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data.ToString())); request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 15;
            yield return request.SendWebRequest();
            JObject response;
            try { response = JObject.Parse(request.downloadHandler.text); }
            catch { Status?.Invoke("Stremio core unavailable: " + request.error); yield break; }
            if (request.result != UnityWebRequest.Result.Success) { Status?.Invoke((string)response["error"] ?? request.error); yield break; }
            if (Debug.isDebugBuild) Debug.Log("[StremioCore] " + (string)data["operation"] + " succeeded");
            try { success(response); } catch (Exception error) { Status?.Invoke(error.Message); }
        } } finally { coreBusy=false; }
    }
    public static string AddonEndpoint(string value) {
        var uri=new Uri(Http(value)); if(!uri.AbsolutePath.EndsWith("/manifest.json",StringComparison.OrdinalIgnoreCase)) throw new Exception("Add-on URL must point to manifest.json");
        return new UriBuilder(uri) { Path=uri.AbsolutePath.Substring(0,uri.AbsolutePath.Length-"/manifest.json".Length),Fragment="" }.Uri.AbsoluteUri;
    }
    public static string ResourceUrl(string endpoint,string path) { var uri=new Uri(Http(endpoint)); return new UriBuilder(uri) { Path=uri.AbsolutePath.TrimEnd('/')+"/"+path,Fragment="" }.Uri.AbsoluteUri; }
    public static string Http(string url) { var uri = new Uri(url); if ((uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo)) throw new Exception("Use an HTTP(S) URL without embedded credentials"); return uri.AbsoluteUri; }
    static string Escape(string value) => Uri.EscapeDataString(value ?? "");
}
}
