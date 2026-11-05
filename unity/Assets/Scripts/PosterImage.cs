using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace StremioVR {
public sealed class PosterImage : MonoBehaviour {
    static int active;
    static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
    static readonly LinkedList<string> CacheOrder = new LinkedList<string>();
    const int MaxCacheEntries = 64;

    Texture2D texture;
    bool isCachedInstance;

    public void Load(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return;

        if (Cache.TryGetValue(url, out var cachedTex) && cachedTex != null) {
            CacheOrder.Remove(url);
            CacheOrder.AddLast(url);
            texture = cachedTex;
            isCachedInstance = true;
            GetComponent<RawImage>().texture = texture;
            GetComponent<RawImage>().color = Color.white;
            return;
        }

        StartCoroutine(Fetch(url));
    }

    IEnumerator Fetch(string url) {
        while (active >= 4) yield return null;
        active++;
        try {
            using (var request = UnityWebRequestTexture.GetTexture(url, true)) {
                request.timeout = 15;
                var operation = request.SendWebRequest();
                while (!operation.isDone) {
                    if (request.downloadedBytes > 8 * 1024 * 1024) { request.Abort(); yield break; }
                    yield return null;
                }
                if (request.result != UnityWebRequest.Result.Success) yield break;
                texture = DownloadHandlerTexture.GetContent(request);
                if (texture != null) {
                    if (Cache.Count >= MaxCacheEntries && CacheOrder.First != null) {
                        string oldestKey = CacheOrder.First.Value;
                        CacheOrder.RemoveFirst();
                        if (Cache.TryGetValue(oldestKey, out var oldestTex)) {
                            Cache.Remove(oldestKey);
                            if (oldestTex != null) Destroy(oldestTex);
                        }
                    }
                    Cache[url] = texture;
                    CacheOrder.AddLast(url);
                    isCachedInstance = true;
                }
                GetComponent<RawImage>().texture = texture;
                GetComponent<RawImage>().color = Color.white;
            }
        } finally {
            active--;
        }
    }

    void OnDestroy() {
        if (!isCachedInstance && texture) Destroy(texture);
    }
}
}
