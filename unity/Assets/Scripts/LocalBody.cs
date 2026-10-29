using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
[DefaultExecutionOrder(20)]
public sealed class LocalBody : MonoBehaviour {
 public Transform Head,Left,Right; public RoomClient Room;
 R6Avatar model; GameObject nubs;
 public void Apply(JObject profile) {
  if(model) { model.gameObject.SetActive(false); Destroy(model.gameObject); }
  model=R6Avatar.Create(transform,profile);
  // The mirror and peers retain the full R6 avatar; only the local camera sees nubs.
  foreach(var part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer=29;
  if(nubs) {foreach(var renderer in nubs.GetComponentsInChildren<Renderer>())Destroy(renderer.sharedMaterial); Destroy(nubs);}
  nubs=new GameObject("First-person hand nubs"); nubs.transform.SetParent(transform,false);
  ColorUtility.TryParseHtmlString((string)profile["skin"],out var skin);
  foreach(var hand in new[]{Left,Right}) {var nub=AvatarPresence.Shape(PrimitiveType.Sphere,nubs.transform,Vector3.zero,new Vector3(0.11f,0.11f,0.14f),skin);nub.layer=26;var follow=nub.AddComponent<HandNub>();follow.Target=hand;}

 }
 void OnDestroy() {if(nubs)foreach(var renderer in nubs.GetComponentsInChildren<Renderer>())Destroy(renderer.sharedMaterial);}
 void LateUpdate() {
  if(!model || !Head) return;
  model.SetPose(Head.position,Head.rotation,Left.position,Right.position);
  bool seated=false; if(Room && Room.State?["players"] is JArray players) foreach(var player in players) if((string)player["id"]==Room.PlayerId) seated=(int?)player["seatedSeat"]>=0;
  model.SetSeated(seated);
 }
}
public sealed class HandNub : MonoBehaviour {public Transform Target;void LateUpdate(){if(Target){transform.position=Target.position;transform.rotation=Target.rotation;}}}
}
