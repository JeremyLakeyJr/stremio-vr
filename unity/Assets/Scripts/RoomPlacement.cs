using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class RoomPlacement:MonoBehaviour {
 public RoomClient Room; public Transform Head; public event Action<string> Status; public event Action Finished;
 public bool Active => preview;
 GameObject preview; Material material; string kind,id; int revision; float reach=3,yaw,pitch,height,started;
 public void Begin(string type,string objectId=null) {
  if(!Room.IsHost){Status?.Invoke("Create a room first. Only the host can arrange it.");return;}
  if(Room.State?["layout"]==null){Status?.Invoke("Update the room server to enable placement.");return;}
  Cancel(false); kind=type;id=objectId;revision=(int)Room.State["layout"]["revision"];yaw=Head.eulerAngles.y;pitch=height=0;reach=3;started=Time.unscaledTime;
  preview=GameObject.CreatePrimitive(PrimitiveType.Cube);preview.name="Placement preview";preview.layer=31;Destroy(preview.GetComponent<Collider>());
  material=new Material(Shader.Find("UI/Default"));material.color=new Color(0.1f,0.9f,0.65f,0.45f);preview.GetComponent<Renderer>().sharedMaterial=material;
  preview.transform.localScale=kind=="screen"?new Vector3(2.1f,1.18125f,0.025f):new Vector3(1.45f,0.85f,1.8f);
  Status?.Invoke("Aim to place. Right stick: rotate / distance. Left stick: height / screen tilt. Trigger: place. Menu: cancel.");
 }
 public void Tick(Ray ray,Vector2 right,Vector2 left,bool confirm) {
  if(!Active)return;
  if(!Room.IsHost||(int?)Room.State?["layout"]?["revision"]!=revision){Status?.Invoke("Placement cancelled: host or room layout changed.");Cancel();return;}
  float dt=Mathf.Min(Time.unscaledDeltaTime,0.05f);
  if(Mathf.Abs(right.y)>0.2f)reach=Mathf.Clamp(reach+right.y*dt*2,0.5f,12);
  if(Mathf.Abs(right.x)>0.2f)yaw=Mathf.Repeat(yaw+right.x*dt*70,360);
  if(Mathf.Abs(left.y)>0.2f)height=Mathf.Clamp(height+left.y*dt,-5,5);
  if(kind=="screen"&&Mathf.Abs(left.x)>0.2f)pitch=Mathf.Clamp(pitch+left.x*dt*50,-85,85);
  var point=ray.GetPoint(reach);
  if(Physics.Raycast(ray,out var hit,reach,~((1<<8)|(1<<26)|(1<<28)|(1<<29)|(1<<30)|(1<<31)),QueryTriggerInteraction.Ignore)) point=hit.point;
  point+=Vector3.up*height;preview.transform.position=point+(kind=="screen"?Vector3.zero:Vector3.up*0.425f);preview.transform.rotation=Quaternion.Euler(pitch,yaw,0);
  if(confirm&&Time.unscaledTime-started>0.35f){Room.Send(new JObject{["type"]="layout",["action"]="place",["kind"]=kind,["id"]=id,["revision"]=revision,["position"]=new JArray(point.x,point.y,point.z),["rotation"]=new JArray(pitch,yaw,0)});Status?.Invoke("Placement sent to room");Cancel();}
 }
 public void Remove(string objectId) {
  if(!Room.IsHost){Status?.Invoke("Only the host can remove room objects");return;}
  Room.Send(new JObject{["type"]="layout",["action"]="remove",["id"]=objectId,["revision"]=(int?)Room.State?["layout"]?["revision"]??-1});
 }
 public void Cancel(bool notify=true){if(preview){preview.SetActive(false);Destroy(preview);preview=null;}if(material){Destroy(material);material=null;}if(notify)Finished?.Invoke();}
 void OnDestroy(){Cancel(false);}
}
}
