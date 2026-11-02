using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace StremioVR.Editor {
// Reproducible render of the actual runtime layout without connecting a room or account.
public static class MenuPreview {
 public static void Capture(){
  var root=new GameObject("Preview harness");root.SetActive(false);var app=root.AddComponent<CinemaApp>();
  var cameraObject=new GameObject("Preview camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();camera.transform.position=new Vector3(0,1.65f,0);camera.fieldOfView=60;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.015f,.02f,.03f);camera.nearClipPlane=.05f;camera.enabled=false;
  const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
  void Set(string key,object value)=>typeof(CinemaApp).GetField(key,flags).SetValue(app,value);
  Set("head",camera.transform);Set("cameraView",camera);Set("font",Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));Set("settings",new JObject());Set("room",root.AddComponent<RoomClient>());
  typeof(CinemaApp).GetMethod("BuildCanvas",flags).Invoke(app,null);
  var panel=(Transform)typeof(CinemaApp).GetField("panel",flags).GetValue(app);
  var fade=panel.GetComponentInChildren<PageFade>();typeof(PageFade).GetMethod("Awake",flags).Invoke(fade,null);
  typeof(CinemaApp).GetMethod("Home",flags).Invoke(app,null);fade.GetComponent<CanvasGroup>().alpha=1;
  Canvas.ForceUpdateCanvases();
  var target=new RenderTexture(1600,1000,24);target.Create();camera.targetTexture=target;var previous=RenderTexture.active;
  try{camera.Render();RenderTexture.active=target;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();Directory.CreateDirectory("Builds");File.WriteAllBytes("Builds/spatial-menu.png",image.EncodeToPNG());Object.DestroyImmediate(image);}
  finally{RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(target);Object.DestroyImmediate(panel.gameObject);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(root);}
 }
}
}
