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
 public int WebWidth { get; private set; } = 1920;
 public int WebHeight { get; private set; } = 1080;
 byte[] pixels; Texture2D texture; AndroidJavaObject web; Renderer screen; float nextFrame; Vector2 lastPoint; bool pressed, receivedFrame; int consecutiveSlowFrames;
 AndroidJavaObject rtc; Texture2D yPlane,uPlane,vPlane; RenderTexture videoTexture; Material converter; JArray remoteTabs=new JArray(); string ice="[]"; bool remoteMode,watching; float nextPointer,nextScroll,lastVolume=-1;
 DesktopBridge desktop;
 public void Initialize(){Room.Message+=OnMessage;
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
  desktop=DesktopBridge.Get();desktop.Message+=DesktopMessage;
#endif
 }
 void DesktopMessage(JObject data){switch((string)data["kind"]){case "receiver-ready":if(watching)Room.Send(new JObject{["type"]="browser-watch"});break;case "browser-signal":ServerBrowserSignal(data["data"].ToString());break;case "browser-video":BrowserVideo((string)data["value"]);break;case "status":Status?.Invoke((string)data["message"]);break;}}
 void Local(string method,params object[] args){if(desktop)desktop.Browser(method,args);else web?.Call(method,args);}
 public void SetVolume(float val) {
   if (Playback != null) Playback.SetVolume(val);
   lastVolume = Mathf.Clamp01(val);
   if (desktop) { if (remoteMode) desktop.Rtc("receiver-volume", new JObject{["value"]=lastVolume}); return; }
   if (remoteMode && rtc != null) rtc.Call("volume", lastVolume);
   else if (web != null) web.Call("volume", lastVolume);
 }
 public void SetResolution(int width, int height) {
  if(width<640 || height<360) return;
  if(WebWidth==width && WebHeight==height && pixels!=null) return;
  WebWidth=width; WebHeight=height;
  pixels=new byte[WebWidth*WebHeight*4];
  if(Active) { Close(); Toggle(); }
 }
 string ProfileKey(){
  if(desktop)return desktop.BrowserProfileKey(new Uri(Room.ApiEndpoint).GetLeftPart(UriPartial.Authority));
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
  if(type=="browser-signal"&&message["data"]!=null){if(desktop&&watching)desktop.Rtc("receiver-signal",new JObject{["data"]=message["data"]});else rtc?.Call("signal",message["data"].ToString());}
  if(type=="state"&&!Room.IsSolo){bool enabled=(bool?)message["browser"]?["ready"]==true;if(enabled&&!watching)StartReceiver();else if(!enabled&&remoteMode)Release();}
 }
 void StartReceiver(){
  if(desktop){Release();desktop.ClearFrame();remoteMode=true;watching=true;Active=true;Playback.Suspended=true;Playback.Video.Pause();desktop.Rtc("receiver-start",new JObject{["ice"]=JArray.Parse(ice)});Attach();return;}
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
  if(desktop){desktop.ClearFrame();Active=true;Playback.Suspended=true;Playback.Video.Pause();Local("open");Attach();return;}
#if UNITY_ANDROID && !UNITY_EDITOR
  try {
   if(pixels==null || pixels.Length!=WebWidth*WebHeight*4) pixels=new byte[WebWidth*WebHeight*4];
   texture=new Texture2D(WebWidth,WebHeight,TextureFormat.RGBA32,false); texture.wrapMode=TextureWrapMode.Clamp;
   using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer")) using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity")) web=new AndroidJavaObject("com.stremiovr.CinemaWebView",activity,gameObject.name,WebWidth,WebHeight);
   Active=true; Playback.Suspended=true; Playback.Video.Pause(); Playback.Video.renderMode=VideoRenderMode.APIOnly; Attach();
  } catch(Exception e) { Close(); Status?.Invoke("Unable to open web screen: "+e.Message); }
#else
  Status?.Invoke("The in-world web screen is available in the Android APK.");
#endif
 }
 void Attach() {
  screen=World.Screen;
  if(desktop){screen.sharedMaterial.mainTexture=desktop.Frame?(Texture)desktop.Frame:Texture2D.blackTexture;screen.sharedMaterial.mainTextureScale=new Vector2(1,-1);screen.sharedMaterial.mainTextureOffset=new Vector2(0,1);Ambient.Source=desktop.Frame;return;}
  screen.sharedMaterial.mainTexture=remoteMode?(Texture)videoTexture:texture; screen.sharedMaterial.mainTextureScale=remoteMode?Vector2.one:new Vector2(1,-1); screen.sharedMaterial.mainTextureOffset=remoteMode?Vector2.zero:new Vector2(0,1);
  screen.transform.localScale=new Vector3(World.Data.ScreenSize.x,World.Data.ScreenSize.y,1); Ambient.Source=remoteMode?(Texture)videoTexture:texture;
 }
 void Update() {
  if(!Active) return; if(!remoteMode&&!Room.IsHost) { Release();return; } if(screen!=World.Screen) Attach();
  if(desktop){
    if(screen.sharedMaterial.mainTexture!=desktop.Frame&&desktop.Frame)Attach();
    if(remoteMode&&Math.Abs(lastVolume-Playback.Volume)>0.01f){lastVolume=Playback.Volume;desktop.Rtc("receiver-volume",new JObject{["value"]=lastVolume});}
    return;
  }
  if(Playback != null && Math.Abs(lastVolume - Playback.Volume) > 0.01f) {
    lastVolume = Playback.Volume;
    if(remoteMode && rtc != null) rtc.Call("volume", lastVolume);
    else if(web != null) web.Call("volume", lastVolume);
  }
  if(remoteMode){try{ReceiveFrame();}catch(Exception e){Status?.Invoke("Browser frame failed: "+e.GetType().Name);Release();}return;}
  float targetFps = WebWidth >= 3840 ? 24f : 30f; if(Time.unscaledTime<nextFrame)return; nextFrame=Time.unscaledTime+1f/targetFps;
  try {
    var bytes=web?.Call<sbyte[]>("takeFrame");
    if(bytes!=null && bytes.Length==WebWidth*WebHeight*4) {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      Buffer.BlockCopy(bytes,0,pixels,0,bytes.Length);
      texture.LoadRawTextureData(pixels);
      texture.Apply(false);
      sw.Stop();
      if(WebWidth >= 3840 && sw.ElapsedMilliseconds > 25) {
        if(++consecutiveSlowFrames >= 3) {
          consecutiveSlowFrames = 0;
          SetResolution(2560, 1440);
          Status?.Invoke("4K display throttled to 1440p Quad HD for thermal & VR performance.");
        }
      } else { consecutiveSlowFrames = 0; }
      if(!receivedFrame){receivedFrame=true;Debug.Log("[WebScreen] Hardware frame received: "+WebWidth+"x"+WebHeight+" RGBA");}
    }
   } catch(Exception e) { Status?.Invoke("Web screen stopped: "+e.GetType().Name);Close(); }
 }
 public bool Point(Ray ray,bool held,float scroll,out Vector3 point) {
  point=Vector3.zero; if(!Active||!screen||!Room.IsHost)return false;
  if(new Plane(-screen.transform.forward,screen.transform.position).Raycast(ray,out var hit)&&hit>0&&hit<4) {
   point=ray.GetPoint(hit); Vector3 local=screen.transform.InverseTransformPoint(point);
   Vector2 uv=new Vector2(local.x+0.5f,local.y+0.5f);
   if(uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1) {
    if(remoteMode){
     if(Time.unscaledTime>=nextPointer){nextPointer=Time.unscaledTime+0.033f;Command("pointer",new JObject{["x"]=uv.x,["y"]=1-uv.y,["pressed"]=held});}
     if(Math.Abs(scroll)>0.01f&&Time.unscaledTime>=nextScroll){nextScroll=Time.unscaledTime+0.05f;Command("scroll",new JObject{["delta"]=-Math.Sign(scroll)*120});}
     return true;
    }
    if(Math.Abs(scroll)>0.01f) Local("scroll",0,(int)(-Math.Sign(scroll)*120));
    if(held&&!pressed) { Local("click",(int)(uv.x*WebWidth),(int)((1-uv.y)*WebHeight)); lastPoint=uv; pressed=true; return true; }
    if(held&&pressed&&Vector2.Distance(lastPoint,uv)>0.005f) { Local("move",(int)(uv.x*WebWidth),(int)((1-uv.y)*WebHeight)); lastPoint=uv; return true; }
    if(!held&&pressed) { Local("release",(int)(uv.x*WebWidth),(int)((1-uv.y)*WebHeight)); pressed=false; return true; }
    return true;
   }
  }
  if(pressed){Local("release",(int)(lastPoint.x*WebWidth),(int)((1-lastPoint.y)*WebHeight));pressed=false;}
  return false;
 }
 public void Open(string url){if(!Room.IsSolo&&!Active){Command("open",new JObject{["profileKey"]=ProfileKey(),["url"]=url});return;}if(!Active)Toggle();if(Active)Navigate(url);}
 public void Text(string value){if(remoteMode)Command("text",new JObject{["value"]=value});else if(Active&&Room.IsHost)Local("text",value);}
 public JArray Tabs {get{try{return remoteMode?remoteTabs:Active?(desktop?desktop.Tabs:JArray.Parse(web.Call<string>("tabs"))):new JArray();}catch{return new JArray();}}}
 public void Navigate(string url){if(remoteMode)Command("navigate",new JObject{["url"]=url});else if(Active&&Room.IsHost)Local("navigate",url);}
 public void NewTab(string url){if(remoteMode)Command("open-tab",new JObject{["url"]=url});else if(Active&&Room.IsHost)Local("newTab",url);}
 public void SelectTab(int index){if(remoteMode){if(index>=0&&index<remoteTabs.Count)Command("select-tab",new JObject{["id"]=remoteTabs[index]["id"]});}else if(Active&&Room.IsHost)Local("selectTab",index);}
 public void CloseTab(int index){if(remoteMode){if(index>=0&&index<remoteTabs.Count)Command("close-tab",new JObject{["id"]=remoteTabs[index]["id"]});}else if(Active&&Room.IsHost)Local("closeTab",index);}
 public void Forward(){if(remoteMode)Command("forward");else if(Active&&Room.IsHost)Local("forward");}
 public void Back(){if(remoteMode)Command("back");else if(Active&&Room.IsHost)Local("back");}
 public void Reload(){if(remoteMode)Command("reload");else if(Active&&Room.IsHost)Local("reload");}
 public void ForgetSession(){if(remoteMode)Command("forget");else if(Active&&Room.IsHost)Local("forgetSession");}
 public void Share(){if(remoteMode)Status?.Invoke("Everyone in the room already sees and hears this browser.");else if(Active&&Room.IsHost)Local("shareVideo");}
 public void BrowserStatus(string value) { Status?.Invoke(value); }
 public void Close(){if(remoteMode&&Room.Connected)Command("close");Release();}
 void Release(){if(Active&&Playback!=null){Playback.Suspended=false;Playback.Video.renderMode=VideoRenderMode.MaterialOverride;}Active=false;remoteMode=false;watching=false;consecutiveSlowFrames=0;DestroyFrames();if(desktop){desktop.ClearFrame();Local("close");}if(web!=null){web.Call("close");web=null;}if(rtc!=null){rtc.Call("close");rtc=null;}if(texture!=null){Destroy(texture);texture=null;}pixels=null;}
 void BrowserVideo(string json){
  try{if(string.IsNullOrEmpty(json)||"null"==json)return;var item=JObject.Parse(json);string url=(string)item["url"],title=(string)item["title"]??"Web video";double pos=(double?)item["position"]??0;bool paused=(bool?)item["paused"]??false;if(string.IsNullOrEmpty(url)||!url.StartsWith("http"))return;Close();Playback.Load(url,title);Playback.SendPlayback(paused,pos,true);Status?.Invoke("Playing video from browser: "+title);}catch(Exception e){Debug.Log("[WebCinema] Browser video extraction failed: "+e.Message);}
 }
 public void ShareVideo(){if(remoteMode){Command("video");return;}Local("shareVideo");}
 void OnDestroy(){Release();if(Room!=null)Room.Message-=OnMessage;}
}
}
