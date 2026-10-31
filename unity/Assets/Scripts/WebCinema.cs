using System;
using System.Security.Cryptography;
using System.Text;
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
 AndroidJavaObject rtc; Texture2D yPlane,uPlane,vPlane; RenderTexture videoTexture; Material converter; JArray remoteTabs=new JArray(); string ice="[]"; bool remoteMode,watching; float nextPointer,nextScroll,lastVolume=-1;
 public void Initialize(){Room.Message+=OnMessage;}
 string ProfileKey(){
  using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))using(var secure=new AndroidJavaClass("com.stremiovr.SecureSession")){
   string secret=secure.CallStatic<string>("loadNamed",activity,"browser-profile-master");if(string.IsNullOrEmpty(secret)){var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);secret=Convert.ToBase64String(bytes);secure.CallStatic("saveNamed",activity,"browser-profile-master",secret);}
   using(var hmac=new HMACSHA256(Convert.FromBase64String(secret)))return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(new Uri(Room.ApiEndpoint).GetLeftPart(UriPartial.Authority)))).Replace("-","").ToLowerInvariant();
  }
 }
 void Command(string action,JObject fields=null){if(Room.IsSolo||!Room.IsHost)return;var data=fields??new JObject();data["type"]="browser";data["action"]=action;Room.Send(data);}
 void OnMessage(JObject message){
  string type=(string)message["type"];if(type=="joined")ice=(message["iceServers"]??new JArray()).ToString();
  if(type=="left"||type=="disconnected"){Release();remoteTabs=new JArray();return;}
  if(type=="browser-tabs"){remoteTabs=(JArray)message["tabs"];return;}
  if(type=="browser-signal"&&rtc!=null&&message["data"]!=null)rtc.Call("signal",message["data"].ToString());
  if(type=="state"&&!Room.IsSolo){bool enabled=(bool?)message["browser"]?["ready"]==true;if(enabled&&!watching)StartReceiver();else if(!enabled&&remoteMode)Release();}
 }
 void StartReceiver(){
#if UNITY_ANDROID && !UNITY_EDITOR
  try{Release();using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))rtc=new AndroidJavaObject("com.stremiovr.RtcBrowser",activity,gameObject.name,ice);
   remoteMode=true;watching=true;Active=true;Playback.Suspended=true;Playback.Video.Pause();converter=new Material(Shader.Find("MovieNight/BrowserYuv"));Room.Send(new JObject{["type"]="browser-watch"});
  }catch(Exception e){Release();Status?.Invoke("Browser receiver failed: "+e.Message);}
#endif
 }
 public void ServerBrowserSignal(string json){if(watching&&Room.Connected)Room.Send(new JObject{["type"]="browser-signal",["data"]=JObject.Parse(json)});}
 unsafe void ReceiveFrame(){
  if(lastVolume!=Playback.Volume){lastVolume=Playback.Volume;rtc.Call("volume",lastVolume);}
  using(var buffer=rtc.Call<AndroidJavaObject>("takeFrame")){
   if(buffer==null)return;int w=rtc.Call<int>("width"),h=rtc.Call<int>("height");if(w<2||h<2||w>3840||h>2160)return;
   if(!yPlane||yPlane.width!=w||yPlane.height!=h){DestroyFrames();yPlane=new Texture2D(w,h,TextureFormat.R8,false,true);uPlane=new Texture2D((w+1)/2,(h+1)/2,TextureFormat.R8,false,true);vPlane=new Texture2D((w+1)/2,(h+1)/2,TextureFormat.R8,false,true);foreach(var t in new[]{yPlane,uPlane,vPlane}){t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;}videoTexture=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);videoTexture.Create();Attach();Debug.Log("[WebScreen] Server decoder: "+w+"x"+h);}
   IntPtr address=(IntPtr)AndroidJNI.GetDirectBufferAddress(buffer.GetRawObject());int luma=w*h,chroma=((w+1)/2)*((h+1)/2);yPlane.LoadRawTextureData(address,luma);uPlane.LoadRawTextureData(IntPtr.Add(address,luma),chroma);vPlane.LoadRawTextureData(IntPtr.Add(address,luma+chroma),chroma);yPlane.Apply(false);uPlane.Apply(false);vPlane.Apply(false);converter.SetTexture("_U",uPlane);converter.SetTexture("_V",vPlane);VideoAmbient.BlitPreservingTarget(yPlane,videoTexture,converter);
  }
 }
 void DestroyFrames(){if(yPlane)Destroy(yPlane);if(uPlane)Destroy(uPlane);if(vPlane)Destroy(vPlane);if(videoTexture){videoTexture.Release();Destroy(videoTexture);}yPlane=uPlane=vPlane=null;videoTexture=null;}
 public void Toggle() {
  if(Active) { Close(); return; }
  if(!Room.IsSolo){if(!Room.IsHost){Status?.Invoke("The host opens the shared browser.");return;}try{Command("open",new JObject{["profileKey"]=ProfileKey()});Status?.Invoke("Starting the shared server browser…");}catch(Exception e){Status?.Invoke(e.Message);}return;}
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
  screen=World.Screen; screen.sharedMaterial.mainTexture=remoteMode?(Texture)videoTexture:texture; screen.sharedMaterial.mainTextureScale=remoteMode?Vector2.one:new Vector2(1,-1); screen.sharedMaterial.mainTextureOffset=remoteMode?Vector2.zero:new Vector2(0,1);
  screen.transform.localScale=new Vector3(World.Data.ScreenSize.x,World.Data.ScreenSize.y,1); Ambient.Source=remoteMode?(Texture)videoTexture:texture;
 }
 void Update() {
  if(!Active) return; if(!remoteMode&&!Room.IsHost) { Release();return; } if(screen!=World.Screen) Attach();
  if(remoteMode){try{ReceiveFrame();}catch(Exception e){Status?.Invoke("Browser frame failed: "+e.GetType().Name);Release();}return;}
  if(Time.unscaledTime<nextFrame)return; nextFrame=Time.unscaledTime+1f/30;
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
  if(hit) { point=ray.GetPoint(nearest); lastPoint=new Vector2(local.x+0.5f,0.5f-local.y); if(held||pressed) Pointer(held?(pressed?2:0):1); if(Mathf.Abs(scroll)>0.01f) Scroll(-scroll*90); }
  else if(pressed) Pointer(3);
  pressed=held&&hit; return hit;
 }
 void Pointer(int phase){if(remoteMode){if(phase==2&&Time.unscaledTime<nextPointer)return;nextPointer=Time.unscaledTime+1f/30;Command("pointer",new JObject{["x"]=lastPoint.x,["y"]=lastPoint.y,["phase"]=phase});}else web?.Call("pointer",lastPoint.x,lastPoint.y,phase);}
 void Scroll(float value){if(Time.unscaledTime<nextScroll)return;nextScroll=Time.unscaledTime+.1f;if(remoteMode)Command("scroll",new JObject{["amount"]=value});else web?.Call("scroll",value);}
 public void Open(string url){if(!Room.IsSolo&&!Active){Command("open",new JObject{["profileKey"]=ProfileKey(),["url"]=url});return;}if(!Active)Toggle();if(Active)Navigate(url);}
 public void Text(string value){if(remoteMode)Command("text",new JObject{["value"]=value});else if(Active&&Room.IsHost)web?.Call("text",value);}
 public JArray Tabs {get{try{return remoteMode?remoteTabs:Active?JArray.Parse(web.Call<string>("tabs")):new JArray();}catch{return new JArray();}}}
 public void Navigate(string url){if(remoteMode)Command("navigate",new JObject{["url"]=url});else if(Active&&Room.IsHost)web?.Call("navigate",url);}
 public void NewTab(string url){if(remoteMode)Command("open-tab",new JObject{["url"]=url});else if(Active&&Room.IsHost)web?.Call("newTab",url);}
 public void SelectTab(int index){if(remoteMode){if(index>=0&&index<remoteTabs.Count)Command("select-tab",new JObject{["id"]=remoteTabs[index]["id"]});}else if(Active&&Room.IsHost)web?.Call("selectTab",index);}
 public void CloseTab(int index){if(remoteMode){if(index>=0&&index<remoteTabs.Count)Command("close-tab",new JObject{["id"]=remoteTabs[index]["id"]});}else if(Active&&Room.IsHost)web?.Call("closeTab",index);}
 public void Forward(){if(remoteMode)Command("forward");else if(Active&&Room.IsHost)web?.Call("forward");}
 public void Back(){if(remoteMode)Command("back");else if(Active&&Room.IsHost)web?.Call("back");}
 public void Reload(){if(remoteMode)Command("reload");else if(Active&&Room.IsHost)web?.Call("reload");}
 public void ForgetSession(){if(remoteMode)Command("forget");else if(Active&&Room.IsHost)web?.Call("forgetSession");}
 public void Share(){if(remoteMode)Status?.Invoke("Everyone in the room already sees and hears this browser.");else if(Active&&Room.IsHost)web?.Call("shareVideo");}
 public void BrowserStatus(string value) { Status?.Invoke(value); }
 public void BrowserVideo(string value) {
  if(!Active||!Room.IsHost)return;
  try {
   string decoded=JsonConvert.DeserializeObject<string>(value); if(string.IsNullOrEmpty(decoded))throw new Exception("Start a video in the browser first.");
   var data=JObject.Parse(decoded); string url=MediaLibrary.Http((string)data["url"]);
   Room.Send(new JObject { ["type"]="playback",["url"]=url,["title"]=(string)data["title"],["position"]=(double?)data["position"]??0,["paused"]=(bool?)data["paused"]??false }); Close(); Status?.Invoke(Room.IsSolo?"Playing locally on the native screen.":"Shared with room. Native synchronized playback resumed.");
  } catch { Status?.Invoke("This browser video cannot be shared directly. Choose an HTTP source in Discover; blob/DRM browser playback cannot be handed to the room."); }
 }
 public void Close(){if(remoteMode&&Room.IsHost)Command("close");Release();}
 void Release() {
  if(watching&&Room?.Connected==true&&!Room.IsSolo)Room.Send(new JObject{["type"]="browser-unwatch"});watching=false;remoteMode=false;lastVolume=-1;
  if(rtc!=null){rtc.Call("close");rtc.Dispose();rtc=null;}DestroyFrames();if(converter)Destroy(converter);converter=null;
  Active=false; pressed=false; if(web!=null){web.Call("close");web.Dispose();web=null;}
  if(screen) {screen.sharedMaterial.mainTextureScale=Vector2.one;screen.sharedMaterial.mainTextureOffset=Vector2.zero;screen.sharedMaterial.mainTexture=Texture2D.blackTexture;}
  if(Ambient)Ambient.Source=null; if(Playback){Playback.Suspended=false;Playback.Video.renderMode=VideoRenderMode.APIOnly;if(World && World.Screen)Playback.SetScreen(World.Screen);}
  if(texture)Destroy(texture); texture=null;
 }
 void OnApplicationPause(bool paused) { if(paused && Active)Release();else if(!paused&&!Room.IsSolo&&Room.State!=null)OnMessage(Room.State); }
 void OnDestroy() { if(Room)Room.Message-=OnMessage;Release(); }
}
}
