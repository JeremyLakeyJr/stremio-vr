using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;
namespace StremioVR {
public sealed class WebCinema : MonoBehaviour {
 public RoomClient Room; public CinemaPlayback Playback; public WorldManager World; public VideoAmbient Ambient;
 public event Action<string> Status;
 public bool Active { get; private set; }
 byte[] pixels=new byte[1024*576*4]; Texture2D texture; AndroidJavaObject web; Renderer screen; float nextFrame; Vector2 lastPoint; bool pressed, receivedFrame;
 public void Toggle() {
  if(Active) { Close(); return; }
  if(!Room.IsHost) { Status?.Invoke("Create a room first. Only its host can operate the web screen."); return; }
#if UNITY_ANDROID && !UNITY_EDITOR
  try {
   texture=new Texture2D(1024,576,TextureFormat.RGBA32,false); texture.wrapMode=TextureWrapMode.Clamp;
   using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer")) using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity")) web=new AndroidJavaObject("com.stremiovr.CinemaWebView",activity,gameObject.name,1024,576);
   Active=true; Playback.Suspended=true; Playback.Video.Pause(); Playback.Video.renderMode=VideoRenderMode.APIOnly; Attach();
  } catch(Exception e) { Close(); Status?.Invoke("Unable to open web screen: "+e.Message); }
#else
  Status?.Invoke("The in-world web screen is available in the Android APK.");
#endif
 }
 void Attach() {
  screen=World.Screen; screen.sharedMaterial.mainTexture=texture; screen.sharedMaterial.mainTextureScale=new Vector2(1,-1); screen.sharedMaterial.mainTextureOffset=new Vector2(0,1);
  screen.transform.localScale=new Vector3(World.Data.ScreenSize.x,World.Data.ScreenSize.y,1); Ambient.Source=texture;
 }
 void Update() {
  if(!Active) return; if(!Room.IsHost) { Close();return; } if(screen!=World.Screen) Attach();
  if(Time.unscaledTime<nextFrame)return; nextFrame=Time.unscaledTime+1f/12;
  try { var bytes=web?.Call<sbyte[]>("takeFrame"); if(bytes!=null && bytes.Length==1024*576*4) {Buffer.BlockCopy(bytes,0,pixels,0,bytes.Length);texture.LoadRawTextureData(pixels);texture.Apply(false); if(!receivedFrame){receivedFrame=true;Debug.Log("[WebScreen] Hardware frame received: 1024x576 RGBA");}} } catch(Exception e) { Status?.Invoke("Web screen stopped: "+e.GetType().Name);Close(); }
 }
 public bool Point(Ray ray,bool held,float scroll,out Vector3 point) {
  point=Vector3.zero; if(!Active||!screen||!Room.IsHost)return false;
  bool hit=false;float nearest=15;Vector3 local=Vector3.one;
  foreach(var display in World.Screens) {
   var plane=new Plane(display.transform.forward,display.transform.position);
   if(Vector3.Dot(ray.direction,display.transform.forward)<=0 || !plane.Raycast(ray,out float d)||d<=0||d>=nearest)continue;
   var candidate=display.transform.InverseTransformPoint(ray.GetPoint(d)); if(Mathf.Abs(candidate.x)>0.5f||Mathf.Abs(candidate.y)>0.5f)continue;
   nearest=d;local=candidate;hit=true;
  }
  if(hit) { point=ray.GetPoint(nearest); lastPoint=new Vector2(local.x+0.5f,0.5f-local.y); if(held||pressed) web?.Call("pointer",lastPoint.x,lastPoint.y,held?(pressed?2:0):1); if(Mathf.Abs(scroll)>0.01f) web?.Call("scroll",-scroll*900*Time.unscaledDeltaTime); }
  else if(pressed) web?.Call("pointer",lastPoint.x,lastPoint.y,3);
  pressed=held&&hit; return hit;
 }
 public void Open(string url){if(!Active)Toggle();if(Active)Navigate(url);}
 public void Text(string value) { if(Active&&Room.IsHost)web?.Call("text",value); }
 public JArray Tabs { get {try{return Active?JArray.Parse(web.Call<string>("tabs")):new JArray();}catch{return new JArray();}} }
 public void Navigate(string url){if(Active&&Room.IsHost)web?.Call("navigate",url);}
 public void NewTab(string url){if(Active&&Room.IsHost)web?.Call("newTab",url);}
 public void SelectTab(int index){if(Active&&Room.IsHost)web?.Call("selectTab",index);}
 public void CloseTab(int index){if(Active&&Room.IsHost)web?.Call("closeTab",index);}
 public void Forward(){if(Active&&Room.IsHost)web?.Call("forward");}
 public void Back() { if(Active&&Room.IsHost)web?.Call("back"); }
 public void Reload() { if(Active&&Room.IsHost)web?.Call("reload"); }
 public void ForgetSession(){if(Active&&Room.IsHost)web?.Call("forgetSession");}
 public void Share() { if(Active&&Room.IsHost)web?.Call("shareVideo"); }
 public void BrowserStatus(string value) { Status?.Invoke(value); }
 public void BrowserVideo(string value) {
  if(!Active||!Room.IsHost)return;
  try {
   string decoded=JsonConvert.DeserializeObject<string>(value); if(string.IsNullOrEmpty(decoded))throw new Exception("Start a video in the browser first.");
   var data=JObject.Parse(decoded); string url=MediaLibrary.Http((string)data["url"]);
   Room.Send(new JObject { ["type"]="playback",["url"]=url,["title"]=(string)data["title"],["position"]=(double?)data["position"]??0,["paused"]=(bool?)data["paused"]??false }); Close(); Status?.Invoke(Room.IsSolo?"Playing locally on the native screen.":"Shared with room. Native synchronized playback resumed.");
  } catch { Status?.Invoke("This browser video cannot be shared directly. Choose an HTTP source in Discover; blob/DRM browser playback cannot be handed to the room."); }
 }
 public void Close() {
  Active=false; pressed=false; if(web!=null){web.Call("close");web.Dispose();web=null;}
  if(screen) {screen.sharedMaterial.mainTextureScale=Vector2.one;screen.sharedMaterial.mainTextureOffset=Vector2.zero;screen.sharedMaterial.mainTexture=Texture2D.blackTexture;}
  if(Ambient)Ambient.Source=null; if(Playback){Playback.Suspended=false;Playback.Video.renderMode=VideoRenderMode.APIOnly;if(World && World.Screen)Playback.SetScreen(World.Screen);}
  if(texture)Destroy(texture); texture=null;
 }
 void OnApplicationPause(bool paused) { if(paused && Active)Close(); }
 void OnDestroy() { Close(); }
}
}
