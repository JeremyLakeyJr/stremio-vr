using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class WorldManager : MonoBehaviour {
 public CinemaWorldData Data { get; private set; }
 public Renderer Screen { get; private set; }
 public readonly List<Renderer> Screens=new List<Renderer>(); readonly Dictionary<Renderer,Vector2> screenSizes=new Dictionary<Renderer,Vector2>();
 public string CurrentWorld { get; private set; }
 public event Action Changed;
 public bool MirrorVisible=true; RoomMirror mirror;
 readonly Dictionary<string,Renderer> extras=new Dictionary<string,Renderer>();

 public enum EnvironmentMode { Cinema = 0, Void = 1, Passthrough = 2 }
 public EnvironmentMode CurrentEnvMode { get; private set; } = EnvironmentMode.Cinema;
 public int StereoMode { get; private set; } = 0; // 0=2D, 1=SBS, 2=OU
 public bool SwapEyes { get; private set; } = false;
 public int HdrMode { get; private set; } = 0; // 0=SDR, 1=HLG, 2=HDR10/PQ, 3=Gamut Boost
 public float Brightness { get; private set; } = 1.0f;
 public float Contrast { get; private set; } = 1.0f;
 public float Saturation { get; private set; } = 1.0f;
 public bool BedMode { get; private set; } = false;
 public float CustomCurvature { get; private set; } = 0f;

 Material glowMaterial; GameObject container; GameObject worldInstance; RoomClient room;
 public void Initialize(RoomClient client) { room=client; Load("home"); room.Message+=Message; }
 void Update() {if(mirror)mirror.Visible=MirrorVisible;}
 void Message(JObject message) { if((string)message["type"]=="disconnected" || (string)message["type"]=="left") SyncScreens(null); if((string)message["type"]=="state") { string id=(string)message["world"]??"home"; if(id!=CurrentWorld) Load(id); SyncScreens(message["layout"]?["items"] as JArray,message["layout"]?["mainScreen"] as JObject); } }
 public void Load(string id) {
  id="home"; var prefab=Resources.Load<GameObject>("Worlds/Home");
  if(!prefab) { Debug.LogWarning("World is not included in this build: "+id); return; }
  if(glowMaterial) Destroy(glowMaterial);
  if(container) { if(Screen) Destroy(Screen.sharedMaterial); container.SetActive(false); Destroy(container); }
  Screens.Clear();screenSizes.Clear();extras.Clear();
  container=new GameObject("World: "+id); container.transform.SetParent(transform,false);
  var world=Instantiate(prefab,container.transform,false); worldInstance=world; Data=world.GetComponent<CinemaWorldData>(); CurrentWorld=id; RenderSettings.skybox=Data.Skybox; if(Camera.main) Camera.main.clearFlags=Data.Skybox?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;
  var screen=GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name="Cinema screen"; screen.transform.SetParent(container.transform,false); screen.transform.position=Data.ScreenPosition; screen.transform.localScale=new Vector3(Data.ScreenSize.x,Data.ScreenSize.y,1); Destroy(screen.GetComponent<Collider>());
  var screenShader=Shader.Find("StremioVR/CinemaScreen") ?? Shader.Find("Unlit/Texture");
  Screen=screen.GetComponent<Renderer>(); Screen.material=new Material(screenShader); Screen.material.mainTexture=Texture2D.blackTexture;
  Screens.Add(Screen);
  foreach(var display in Screens)screenSizes[display]=display.transform.localScale;
  for(int i=0;i<Data.Seats.Length;i++) {
   var target=new GameObject("Sit in chair "+(i+1)); target.transform.SetParent(container.transform,false); target.transform.position=Data.Seats[i]-Vector3.up*0.65f; target.layer=9;
   var box=target.AddComponent<BoxCollider>(); box.isTrigger=true; box.size=new Vector3(0.65f,0.65f,0.65f); target.AddComponent<SeatTarget>().Index=i;
  }
  glowMaterial=new Material(Shader.Find("StremioVR/ScreenGlow"));AddGlow(Screen);
  var ground = new GameObject("SolidGroundPlane"); ground.transform.SetParent(container.transform,false);
  var groundBox = ground.AddComponent<BoxCollider>(); groundBox.size = new Vector3(100f, 0.4f, 100f); groundBox.center = new Vector3(0, -0.2f, 0);
  var mirrorObject=GameObject.CreatePrimitive(PrimitiveType.Quad); mirrorObject.name="Room mirror"; mirrorObject.transform.SetParent(container.transform,false); mirrorObject.transform.position=Data.Spawn+new Vector3(-1.4f,1.5f,-1.5f); mirrorObject.transform.rotation=Quaternion.Euler(0,180,0); mirrorObject.transform.localScale=new Vector3(1.5f,2.2f,1); mirror=mirrorObject.AddComponent<RoomMirror>();
  ApplyShaderProperties();
  SetEnvironmentMode(CurrentEnvMode);
  SetBedMode(BedMode);
  if(CustomCurvature>0) SetCurvature(CustomCurvature);
  Changed?.Invoke();
 }

 public void SetEnvironmentMode(EnvironmentMode mode) {
  CurrentEnvMode = mode;
  if (worldInstance != null) {
   worldInstance.SetActive(mode == EnvironmentMode.Cinema);
  }
  if (mirror != null) {
   mirror.gameObject.SetActive(mode == EnvironmentMode.Cinema && MirrorVisible);
  }
  var cam = Camera.main;
  if (cam != null) {
   if (mode == EnvironmentMode.Cinema) {
    RenderSettings.skybox = Data != null ? Data.Skybox : null;
    cam.clearFlags = (Data != null && Data.Skybox) ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.02f, 0.025f, 0.05f);
   } else if (mode == EnvironmentMode.Void) {
    RenderSettings.skybox = null;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = Color.black;
   } else if (mode == EnvironmentMode.Passthrough) {
    RenderSettings.skybox = null;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
   }
  }
 }

 public void SetStereoMode(int mode, bool swap = false) {
  StereoMode = Mathf.Clamp(mode, 0, 2);
  SwapEyes = swap;
  ApplyShaderProperties();
 }

 public void SetHdrMode(int mode) {
  HdrMode = Mathf.Clamp(mode, 0, 3);
  ApplyShaderProperties();
 }

 public void SetPictureAdjustments(float brightness, float contrast, float saturation) {
  Brightness = Mathf.Clamp(brightness, 0.2f, 2.0f);
  Contrast = Mathf.Clamp(contrast, 0.5f, 2.0f);
  Saturation = Mathf.Clamp(saturation, 0.0f, 2.0f);
  ApplyShaderProperties();
 }

 void ApplyShaderProperties() {
  if (Screen != null && Screen.material != null) {
   if (Screen.material.HasProperty("_StereoMode")) Screen.material.SetFloat("_StereoMode", StereoMode);
   if (Screen.material.HasProperty("_SwapEyes")) Screen.material.SetFloat("_SwapEyes", SwapEyes ? 1f : 0f);
   if (Screen.material.HasProperty("_HdrMode")) Screen.material.SetFloat("_HdrMode", HdrMode);
   if (Screen.material.HasProperty("_Brightness")) Screen.material.SetFloat("_Brightness", Brightness);
   if (Screen.material.HasProperty("_Contrast")) Screen.material.SetFloat("_Contrast", Contrast);
   if (Screen.material.HasProperty("_Saturation")) Screen.material.SetFloat("_Saturation", Saturation);
  }
 }

 public void SetBedMode(bool enabled) {
  BedMode = enabled;
  if (Screen != null && Data != null) {
   if (enabled) {
    Screen.transform.position = Data.Spawn + new Vector3(0, 2.2f, 1.4f);
    Screen.transform.rotation = Quaternion.Euler(-75f, 0, 0);
   } else {
    Screen.transform.position = Data.ScreenPosition;
    Screen.transform.rotation = Quaternion.identity;
   }
  }
 }

 public void SetCurvature(float degrees) {
  CustomCurvature = Mathf.Clamp(degrees, 0f, 90f);
  if (Screen != null) {
   var surface = Screen.GetComponent<MonitorSurface>();
   if (surface != null) {
    surface.Curvature = CustomCurvature;
   }
  }
 }
 void AddGlow(Renderer display){display.gameObject.AddComponent<MonitorSurface>().Initialize(glowMaterial);}
 public static Vector3 LayoutVector(JToken value) => new Vector3((float)value[0],(float)value[1],(float)value[2]);
 void SyncScreens(JArray items,JObject main=null) {
  if(Screen){float width=(float?)main?["width"]??Data.ScreenSize.x;screenSizes[Screen]=new Vector2(width,width*Data.ScreenSize.y/Data.ScreenSize.x);Screen.GetComponent<MonitorSurface>().Curvature=(float?)main?["curve"]??0;}
  var keep=new HashSet<string>();
  foreach(var item in items??new JArray()) {
   if((string)item["kind"]!="screen")continue;string id=(string)item["id"];keep.Add(id);
   if(!extras.TryGetValue(id,out var display)) {
    var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Placed movie screen";go.transform.SetParent(container.transform,false);Destroy(go.GetComponent<Collider>());
    display=go.GetComponent<Renderer>();display.sharedMaterial=Screen.sharedMaterial;extras[id]=display;Screens.Add(display);screenSizes[display]=new Vector2(2.1f,1.18125f);go.transform.localScale=new Vector3(2.1f,1.18125f,1);AddGlow(display);
   }
   float width=(float?)item["width"]??2.1f;screenSizes[display]=new Vector2(width,width*9/16);display.GetComponent<MonitorSurface>().Curvature=(float?)item["curve"]??0;
   display.transform.position=LayoutVector(item["position"]);display.transform.rotation=Quaternion.Euler(LayoutVector(item["rotation"]));
  }
  foreach(string id in new List<string>(extras.Keys))if(!keep.Contains(id)){var display=extras[id];Screens.Remove(display);screenSizes.Remove(display);extras.Remove(id);display.gameObject.SetActive(false);Destroy(display.gameObject);}
 }
 void LateUpdate() {
  if(!Screen)return;var texture=Screen.sharedMaterial.mainTexture;
  foreach(var display in Screens){
   var size=screenSizes[display];float aspect=texture&&texture!=Texture2D.blackTexture?(float)texture.width/Mathf.Max(1,texture.height):size.x/size.y;float width=Mathf.Min(size.x,size.y*aspect);display.transform.localScale=new Vector3(width,width/aspect,1);
   display.GetComponent<MonitorSurface>().Configure(display.transform.localScale.x,display.transform.localScale.y);
  }
 }
 void OnDestroy() { if(room) room.Message-=Message; }
}
public sealed class SeatTarget : MonoBehaviour { public int Index; }
}
