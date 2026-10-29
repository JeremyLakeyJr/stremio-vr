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
 public bool MirrorVisible=true; RoomMirror mirror; Material glowMaterial; GameObject container; RoomClient room;
 void Update() { if(mirror) mirror.Visible=MirrorVisible; }
 public void Initialize(RoomClient client) { room=client; Load("cinema"); room.Message+=Message; }
 void Message(JObject message) { if((string)message["type"]=="state") { string id=(string)message["world"]??"cinema"; if(id!=CurrentWorld) Load(id); } }
 public void Load(string id) {
  var prefab=Resources.Load<GameObject>(id=="home"?"Worlds/Home":"Worlds/Cinema");
  if(!prefab) { Debug.LogWarning("World is not included in this build: "+id); return; }
  if(glowMaterial) Destroy(glowMaterial);
  if(container) { if(Screen) Destroy(Screen.sharedMaterial); container.SetActive(false); Destroy(container); }
  Screens.Clear();screenSizes.Clear();
  container=new GameObject("World: "+id); container.transform.SetParent(transform,false);
  var world=Instantiate(prefab,container.transform,false); Data=world.GetComponent<CinemaWorldData>(); CurrentWorld=id; RenderSettings.skybox=Data.Skybox; if(Camera.main) Camera.main.clearFlags=Data.Skybox?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;
  var screen=GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name="Cinema screen"; screen.transform.SetParent(container.transform,false); screen.transform.position=Data.ScreenPosition; screen.transform.localScale=new Vector3(Data.ScreenSize.x,Data.ScreenSize.y,1); Destroy(screen.GetComponent<Collider>());
  Screen=screen.GetComponent<Renderer>(); Screen.material=new Material(Shader.Find("Unlit/Texture")); Screen.material.mainTexture=Texture2D.blackTexture;
  Screens.Add(Screen);
  if(id=="home") foreach(float side in new[]{-1f,1f}) {
   var extra=GameObject.CreatePrimitive(PrimitiveType.Quad); extra.name="Mirrored cinema display"; extra.transform.SetParent(container.transform,false);
   extra.transform.position=Data.ScreenPosition+new Vector3(side*2.8f,-0.15f,-1.5f); extra.transform.rotation=Quaternion.Euler(0,side*35,0);extra.transform.localScale=new Vector3(2.1f,1.18125f,1); Destroy(extra.GetComponent<Collider>());
   var renderer=extra.GetComponent<Renderer>();renderer.sharedMaterial=Screen.sharedMaterial;Screens.Add(renderer);
  }
  foreach(var display in Screens)screenSizes[display]=display.transform.localScale;
  for(int i=0;i<Data.Seats.Length;i++) {
   var target=new GameObject("Sit in chair "+(i+1)); target.transform.SetParent(container.transform,false); target.transform.position=Data.Seats[i]-Vector3.up*0.65f; target.layer=9;
   var box=target.AddComponent<BoxCollider>(); box.isTrigger=true; box.size=new Vector3(0.65f,0.65f,0.65f); target.AddComponent<SeatTarget>().Index=i;
  }
  var glow=GameObject.CreatePrimitive(PrimitiveType.Quad); glow.name="Video edge glow"; glow.transform.SetParent(container.transform,false); glow.transform.position=Data.ScreenPosition-Vector3.forward*0.04f; glow.transform.localScale=new Vector3(Data.ScreenSize.x+2,Data.ScreenSize.y+2,1); Destroy(glow.GetComponent<Collider>());
  glowMaterial=new Material(Shader.Find("StremioVR/ScreenGlow")); glowMaterial.SetVector("_ScreenFraction",new Vector4(Data.ScreenSize.x/(Data.ScreenSize.x+2),Data.ScreenSize.y/(Data.ScreenSize.y+2),0,0)); glow.GetComponent<Renderer>().sharedMaterial=glowMaterial;
  var mirrorObject=GameObject.CreatePrimitive(PrimitiveType.Quad); mirrorObject.name="Room mirror"; mirrorObject.transform.SetParent(container.transform,false); mirrorObject.transform.position=Data.Spawn+new Vector3(-1.4f,1.5f,-1.5f); mirrorObject.transform.rotation=Quaternion.Euler(0,180,0); mirrorObject.transform.localScale=new Vector3(1.5f,2.2f,1); mirror=mirrorObject.AddComponent<RoomMirror>();
  var probeObject=new GameObject("Room reflections",typeof(ReflectionProbe)); probeObject.transform.SetParent(container.transform,false); probeObject.transform.position=Data.Spawn+Vector3.up*1.6f;
  var probe=probeObject.GetComponent<ReflectionProbe>(); probe.mode=UnityEngine.Rendering.ReflectionProbeMode.Realtime; probe.refreshMode=UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting; probe.timeSlicingMode=UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces; probe.resolution=128; probe.size=Vector3.one*100; probe.boxProjection=true; probe.cullingMask=~((1<<26)|(1<<28)|(1<<29)|(1<<30)); probe.RenderProbe();
  Changed?.Invoke();
 }
 void LateUpdate() {
  if(!Screen)return;var texture=Screen.sharedMaterial.mainTexture;if(!texture||texture==Texture2D.blackTexture)return;
  float aspect=(float)texture.width/Mathf.Max(1,texture.height);foreach(var display in Screens){var size=screenSizes[display];float width=Mathf.Min(size.x,size.y*aspect);display.transform.localScale=new Vector3(width,width/aspect,1);}
 }
 void OnDestroy() { if(room) room.Message-=Message; }
}
public sealed class SeatTarget : MonoBehaviour { public int Index; }
}
