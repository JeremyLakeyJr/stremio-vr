using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
namespace StremioVR {
public sealed class PosterImage : MonoBehaviour {
 static int active; Texture2D texture;
 public void Load(string url) { if(Uri.TryCreate(url,UriKind.Absolute,out var uri) && (uri.Scheme=="http" || uri.Scheme=="https")) StartCoroutine(Fetch(url)); }
 IEnumerator Fetch(string url) {
  while(active>=4) yield return null; active++;
  try {
   using(var request=UnityWebRequestTexture.GetTexture(url,true)) {
    request.timeout=15; var operation=request.SendWebRequest();
    while(!operation.isDone) { if(request.downloadedBytes>8*1024*1024) { request.Abort(); yield break; } yield return null; }
    if(request.result!=UnityWebRequest.Result.Success) yield break;
    texture=DownloadHandlerTexture.GetContent(request); GetComponent<RawImage>().texture=texture; GetComponent<RawImage>().color=Color.white;
   }
  } finally { active--; }
 }
 void OnDestroy() { if(texture) Destroy(texture); }
}
}
