using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
[DefaultExecutionOrder(20)]
public sealed class LocalBody : MonoBehaviour {
 public Transform Head,Left,Right; public RoomClient Room;
 R6Avatar model;
 public void Apply(JObject profile) {
  if(model) { model.gameObject.SetActive(false); Destroy(model.gameObject); }
  model=R6Avatar.Create(transform,profile);
  // Head/accessories stay visible in the mirror without clipping into the viewer's eyes.
  foreach(var part in model.Head.GetComponentsInChildren<Transform>(true)) part.gameObject.layer=29;
 }
 void LateUpdate() {
  if(!model || !Head) return;
  model.SetPose(Head.position,Head.rotation,Left.position,Right.position);
  bool seated=false; if(Room && Room.State?["players"] is JArray players) foreach(var player in players) if((string)player["id"]==Room.PlayerId) seated=(int?)player["seatedSeat"]>=0;
  model.SetSeated(seated);
 }
}
}
