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
 readonly Dictionary<Renderer,Renderer> glows=new Dictionary<Renderer,Renderer>(); readonly MaterialPropertyBlock glowProperties=new MaterialPropertyBlock();
 Material glowMaterial; GameObject container; RoomClient room;
 public void Initialize(RoomClient client) { room=client; Load("home"); room.Message+=Message; }
 void Update() {if(mirror)mirror.Visible=MirrorVisible;}
 void Message(JObject message) { if((string)message["type"]=="disconnected" || (string)message["type"]=="left") SyncScreens(null); if((string)message["type"]=="state") { string id=(string)message["world"]??"home"; if(id!=CurrentWorld) Load(id); SyncScreens(message["layout"]?["items"] as JArray); } }
 public void Load(string id) {
  id="home"; var prefab=Resources.Load<GameObject>("Worlds/Home");
  if(!prefab) { Debug.LogWarning("World is not included in this build: "+id); return; }
  if(glowMaterial) Destroy(glowMaterial);
  if(container) { if(Screen) Destroy(Screen.sharedMaterial); container.SetActive(false); Destroy(container); }
  Screens.Clear();screenSizes.Clear();extras.Clear();glows.Clear();
  container=new GameObject("World: "+id); container.transform.SetParent(transform,false);
  var world=Instantiate(prefab,container.transform,false); Data=world.GetComponent<CinemaWorldData>(); CurrentWorld=id; RenderSettings.skybox=Data.Skybox; if(Camera.main) Camera.main.clearFlags=Data.Skybox?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;
  var screen=GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name="Cinema screen"; screen.transform.SetParent(container.transform,false); screen.transform.position=Data.ScreenPosition; screen.transform.localScale=new Vector3(Data.ScreenSize.x,Data.ScreenSize.y,1); Destroy(screen.GetComponent<Collider>());
  Screen=screen.GetComponent<Renderer>(); Screen.material=new Material(Shader.Find("Unlit/Texture")); Screen.material.mainTexture=Texture2D.blackTexture;
  Screens.Add(Screen);
  foreach(var display in Screens)screenSizes[display]=display.transform.localScale;
  for(int i=0;i<Data.Seats.Length;i++) {
   var target=new GameObject("Sit in chair "+(i+1)); target.transform.SetParent(container.transform,false); target.transform.position=Data.Seats[i]-Vector3.up*0.65f; target.layer=9;
   var box=target.AddComponent<BoxCollider>(); box.isTrigger=true; box.size=new Vector3(0.65f,0.65f,0.65f); target.AddComponent<SeatTarget>().Index=i;
  }
  glowMaterial=new Material(Shader.Find("StremioVR/ScreenGlow"));AddGlow(Screen);
  var mirrorObject=GameObject.CreatePrimitive(PrimitiveType.Quad); mirrorObject.name="Room mirror"; mirrorObject.transform.SetParent(container.transform,false); mirrorObject.transform.position=Data.Spawn+new Vector3(-1.4f,1.5f,-1.5f); mirrorObject.transform.rotation=Quaternion.Euler(0,180,0); mirrorObject.transform.localScale=new Vector3(1.5f,2.2f,1); mirror=mirrorObject.AddComponent<RoomMirror>();
  Changed?.Invoke();
 }
 void AddGlow(Renderer display){
  var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Video edge glow";go.transform.SetParent(display.transform,false);go.transform.localPosition=new Vector3(0,0,-.04f);Destroy(go.GetComponent<Collider>());var r=go.GetComponent<Renderer>();r.sharedMaterial=glowMaterial;glows[display]=r;
 }
 public static Vector3 LayoutVector(JToken value) => new Vector3((float)value[0],(float)value[1],(float)value[2]);
 void SyncScreens(JArray items) {
  var keep=new HashSet<string>();
  foreach(var item in items??new JArray()) {
   if((string)item["kind"]!="screen")continue;string id=(string)item["id"];keep.Add(id);
   if(!extras.TryGetValue(id,out var display)) {
    var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Placed movie screen";go.transform.SetParent(container.transform,false);Destroy(go.GetComponent<Collider>());
    display=go.GetComponent<Renderer>();display.sharedMaterial=Screen.sharedMaterial;extras[id]=display;Screens.Add(display);screenSizes[display]=new Vector2(2.1f,1.18125f);go.transform.localScale=new Vector3(2.1f,1.18125f,1);AddGlow(display);
   }
   display.transform.position=LayoutVector(item["position"]);display.transform.rotation=Quaternion.Euler(LayoutVector(item["rotation"]));
  }
  foreach(string id in new List<string>(extras.Keys))if(!keep.Contains(id)){var display=extras[id];Screens.Remove(display);screenSizes.Remove(display);glows.Remove(display);extras.Remove(id);display.gameObject.SetActive(false);Destroy(display.gameObject);}
 }
 void LateUpdate() {
  if(!Screen)return;var texture=Screen.sharedMaterial.mainTexture;
  foreach(var display in Screens){
   var size=screenSizes[display];if(texture&&texture!=Texture2D.blackTexture){float aspect=(float)texture.width/Mathf.Max(1,texture.height);float width=Mathf.Min(size.x,size.y*aspect);display.transform.localScale=new Vector3(width,width/aspect,1);}
   if(glows.TryGetValue(display,out var glow)){var actual=display.transform.localScale;glow.transform.localScale=new Vector3(1+2/actual.x,1+2/actual.y,1);glowProperties.SetVector("_ScreenFraction",new Vector4(actual.x/(actual.x+2),actual.y/(actual.y+2),0,0));glow.SetPropertyBlock(glowProperties);}
  }
 }
 void OnDestroy() { if(room) room.Message-=Message; }
}
public sealed class SeatTarget : MonoBehaviour { public int Index; }
}
