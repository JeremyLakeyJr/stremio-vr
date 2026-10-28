using System;
using System.Collections;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
namespace StremioVR {
public sealed class StremioAccount : MonoBehaviour {
 public event Action<string> Status;
 public event Action Changed; public event Action HistoryLoaded;
 public JArray History { get; private set; }=new JArray();
 public event Action<JArray> AddonsImported;
 public bool SignedIn => !string.IsNullOrEmpty(authKey);
 public string LinkCode { get; private set; }
 public string LinkUrl { get; private set; }
 public string DisplayName { get; private set; }
 string authKey; int generation; bool syncing;
 public void Resume() {
  try { authKey=Vault("load"); } catch { Status?.Invoke("Saved sign-in could not be read. Please sign in again."); }
  if(SignedIn) StartCoroutine(LoadAccount(generation));
 }
 public void BeginLink() { CancelLink(); StartCoroutine(Link(generation)); }
 public void CancelLink() { generation++; LinkCode=null; LinkUrl=null; Changed?.Invoke(); }
 IEnumerator Link(int run) {
  JObject created=null; yield return Request("https://link.stremio.com/api/v2/create?type=Create",null,result=>created=result as JObject);
  if(run!=generation || created==null) yield break;
  LinkCode=(string)created["code"]; LinkUrl=(string)created["link"];
  if(string.IsNullOrEmpty(LinkCode) || !OfficialLink(LinkUrl)) { Status?.Invoke("Stremio returned an invalid sign-in link"); yield break; }
  Changed?.Invoke(); float deadline=Time.realtimeSinceStartup+300;
  while(run==generation && Time.realtimeSinceStartup<deadline) {
   yield return new WaitForSecondsRealtime(5);
   if(run!=generation) yield break;
   JObject result=null;
   yield return Request("https://link.stremio.com/api/v2/read?type=Read&code="+Uri.EscapeDataString(LinkCode),null,data=>result=data as JObject,true);
   if(run!=generation) yield break;
   string key=(string)result?["authKey"]; if(string.IsNullOrEmpty(key)) continue;
   authKey=key; LinkCode=null; LinkUrl=null;
   try { Vault("save",authKey); } catch { Status?.Invoke("Signed in for this session; secure storage was unavailable."); }
   yield return LoadAccount(run); yield break;
  }
  if(run==generation) { LinkCode=null; LinkUrl=null; Status?.Invoke("Sign-in code expired. Try again."); Changed?.Invoke(); }
 }
 public static bool OfficialLink(string value) { return Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme=="https" && string.IsNullOrEmpty(uri.UserInfo) && (uri.Host=="stremio.com" || uri.Host.EndsWith(".stremio.com",StringComparison.OrdinalIgnoreCase)); }
 public void OpenLink() { if(OfficialLink(LinkUrl)) Application.OpenURL(LinkUrl); }
 IEnumerator LoadAccount(int run) {
  JObject user=null; yield return Request("https://api.strem.io/api/getUser",new JObject { ["authKey"]=authKey },value=>user=value as JObject);
  if(run!=generation) yield break;
  if(user!=null) DisplayName=(string)user["fullname"] ?? (string)user["email"] ?? "Stremio account";
  Changed?.Invoke(); yield return Import(run); yield return FetchHistory(run);
 }
 public void SyncAddons() { if(SignedIn && !syncing) StartCoroutine(Import(generation)); }
 IEnumerator Import(int run) {
  if(syncing) yield break; syncing=true; JObject collection=null;
  yield return Request("https://api.strem.io/api/addonCollectionGet",new JObject { ["authKey"]=authKey,["update"]=true },value=>collection=value as JObject);
  syncing=false; if(run!=generation || collection==null) yield break;
  var addons=collection["addons"] as JArray;
  if(addons==null) { Status?.Invoke("Stremio returned an invalid add-on collection"); yield break; }
  AddonsImported?.Invoke(addons); Changed?.Invoke();
 }
 public void RefreshHistory() { if(SignedIn) StartCoroutine(FetchHistory(generation)); }
 public static JArray WatchedItems(JArray items) {
  return new JArray(items.OfType<JObject>().Where(item=>item["state"] is JObject state && (state["lastWatched"]?.Type==JTokenType.Date || !string.IsNullOrEmpty((string)state["lastWatched"]) || (long?)state["timesWatched"]>0 || (long?)state["timeWatched"]>0 || (long?)state["timeOffset"]>0)).OrderByDescending(item=>HistoryTime(item)).Select(item=>item.DeepClone()));
 }
 static long HistoryTime(JObject item) { return DateTimeOffset.TryParse((string)item["state"]?["lastWatched"]??(string)item["_mtime"],out var time)?time.UtcDateTime.Ticks:0; }
 IEnumerator FetchHistory(int run) {
  JArray items=null; yield return Request("https://api.strem.io/api/datastoreGet",new JObject { ["authKey"]=authKey,["collection"]="libraryItem",["ids"]=new JArray(),["all"]=true },result=>items=result as JArray);
  if(run!=generation || items==null) yield break; History=WatchedItems(items); Debug.Log("[History] Imported "+History.Count+" watched entries"); HistoryLoaded?.Invoke();
 }
 public void SignOut() {
  string oldKey=authKey; CancelLink(); authKey=null; DisplayName=null; History=new JArray(); HistoryLoaded?.Invoke();
  try { Vault("clear"); } catch { Status?.Invoke("Could not clear secure session storage"); }
  AddonsImported?.Invoke(new JArray()); Changed?.Invoke(); Status?.Invoke("Signed out of Stremio");
  if(!string.IsNullOrEmpty(oldKey)) StartCoroutine(Request("https://api.strem.io/api/logout",new JObject { ["authKey"]=oldKey },_=>{},true));
 }
 IEnumerator Request(string url,JObject body,Action<JToken> success,bool quiet=false) {
  using(var request=new UnityWebRequest(url,body==null?"GET":"POST")) {
   request.downloadHandler=new DownloadHandlerBuffer(); request.timeout=20;
   if(body!=null) { request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString())); request.SetRequestHeader("Content-Type","application/json"); }
   yield return request.SendWebRequest();
   JObject response=null; try { response=JObject.Parse(request.downloadHandler.text); } catch { }
   if(request.result!=UnityWebRequest.Result.Success || response?["result"]==null) { if(!quiet) Status?.Invoke("Stremio request failed. Check your connection or sign in again."); yield break; }
   success(response["result"]);
  }
 }
 static string Vault(string operation,string value=null) {
#if UNITY_ANDROID && !UNITY_EDITOR
  using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
  using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))
  using(var vault=new AndroidJavaClass("com.stremiovr.SecureSession")) {
   if(operation=="load") return vault.CallStatic<string>(operation,activity);
   if(operation=="save") vault.CallStatic(operation,activity,value); else vault.CallStatic(operation,activity); return null;
  }
#else
  return null; // Editor sessions deliberately keep credentials in memory only.
#endif
 }
 void OnDestroy() { generation++; }
}
}
