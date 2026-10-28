using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;
namespace StremioVR {
[DefaultExecutionOrder(-50)]
public sealed class RigMotion : MonoBehaviour {
 public Transform Rig, Tracking, Head;
 public RoomClient Room; public WorldManager World;
 public Func<bool> MenuVisible;
 public event Action<string> Status;
 public float MoveSpeed=1.5f, TurnSpeed=60, RayPitch=45;
 public bool Seated => seatedSeat>=0;
 public float ViewPitch => Mathf.DeltaAngle(0,Tracking.localEulerAngles.x);
 CharacterController controller; int seatedSeat=-1; string joinedRoom; bool standDown; float verticalVelocity;
 public void Initialize() {
  Rig.gameObject.layer=8; controller=Rig.gameObject.AddComponent<CharacterController>(); controller.radius=0.2f; controller.height=1.65f; controller.center=Vector3.up*0.825f; controller.stepOffset=0.35f; controller.slopeLimit=55;
  Rig.position=World.Data.Spawn; Room.Message+=Message;
 }
 void Message(JObject message) {
  if((string)message["type"]!="state") return;
  foreach(var player in (JArray)message["players"]) if((string)player["id"]==Room.PlayerId) {
   if(joinedRoom!=Room.RoomId) { joinedRoom=Room.RoomId; seatedSeat=-1; Teleport(World.Data.Spawn+Vector3.right*((int)player["seat"]*0.3f),false); }
   int seat=(int?)player["seatedSeat"]??-1;
   if(seat!=seatedSeat) { seatedSeat=seat; if(seat>=0 && seat<World.Data.Seats.Length) SitAt(seat); else StandAtAisle(); }
  }
 }
 public static Vector3 RayDirection(Quaternion controllerRotation,float pitch) => controllerRotation*Quaternion.Euler(pitch,0,0)*Vector3.forward;
 public static Vector3 FlatDirection(Vector3 forward) { forward.y=0; return forward.sqrMagnitude>0.001f?forward.normalized:Vector3.forward; }
 public void ResetWorld() { seatedSeat=-1; Teleport(World.Data.Spawn,false); }
 public void RequestSeat(int index) {
  if(index<0 || index>=World.Data.Seats.Length) return;
  if(Room.State!=null && Room.Connected) Room.Send(new JObject { ["type"]="seat",["seatIndex"]=index });
  else { seatedSeat=index; SitAt(index); }
 }
 public void Stand() {
  if(!Seated) return;
  if(Room.State!=null && Room.Connected) Room.Send(new JObject { ["type"]="seat",["seatIndex"]=-1 });
  else { seatedSeat=-1; StandAtAisle(); }
 }
 void SitAt(int index) {
  controller.enabled=false;
  Rig.RotateAround(Head.position,Vector3.up,-Head.eulerAngles.y);
  Rig.position+=World.Data.Seats[index]-Head.position; verticalVelocity=0;
  Status?.Invoke("Seated in chair "+(index+1)+". Press B or Stand up to leave.");
 }
 void StandAtAisle() { Teleport(World.Data.Spawn,false); Status?.Invoke("Standing in the aisle"); }
 void Teleport(Vector3 point,bool eye) {
  controller.enabled=false;
  Tracking.localPosition=Vector3.zero;
  Rig.position=point-new Vector3(Head.position.x-Rig.position.x,0,Head.position.z-Rig.position.z);
  if(eye) Rig.position+=Vector3.up*(point.y-Head.position.y);
  controller.enabled=true; verticalVelocity=0;
 }
 public void CalibrateReclined() {
  var pivot=Head.position; Tracking.localRotation=Quaternion.Euler(0,Head.localEulerAngles.y,0)*Quaternion.Inverse(Head.localRotation);
  Tracking.position+=pivot-Head.position;
  Status?.Invoke("Reclined view calibrated. Your current gaze is now level.");
 }
 public void AdjustView(float degrees) { var pivot=Head.position; Tracking.localRotation=Quaternion.Euler(degrees,0,0)*Tracking.localRotation; Tracking.position+=pivot-Head.position; }
 public void ResetView() { var pivot=Head.position; Tracking.localRotation=Quaternion.identity; Tracking.position+=pivot-Head.position; Status?.Invoke("View angle reset"); }
 void Update() {
  var left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand); var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
  right.TryGetFeatureValue(CommonUsages.secondaryButton,out bool stand); if(stand && !standDown) Stand(); standDown=stand;
  if(MenuVisible?.Invoke()==true) return;
  right.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 turn);
  if(Mathf.Abs(turn.x)>0.2f) Rig.RotateAround(Head.position,Vector3.up,turn.x*TurnSpeed*Time.deltaTime);
  if(Seated) return;
  left.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 stick); if(stick.magnitude<0.2f) stick=Vector2.zero; stick=Vector2.ClampMagnitude(stick,1);
  var forward=FlatDirection(Head.forward); var rightward=Vector3.Cross(Vector3.up,forward);
  var localHead=Rig.InverseTransformPoint(Head.position); float height=Mathf.Clamp(localHead.y,0.8f,2.2f);
  controller.height=height; controller.center=new Vector3(localHead.x,height*0.5f,localHead.z);
  verticalVelocity=controller.isGrounded?-1:Mathf.Max(-10,verticalVelocity-9.81f*Time.deltaTime);
  controller.Move(((forward*stick.y+rightward*stick.x)*MoveSpeed+Vector3.up*verticalVelocity)*Time.deltaTime);
  if(Head.position.y< -3) Teleport(World.Data.Spawn,false);
 }
 void OnDestroy() { if(Room!=null) Room.Message-=Message; }
}
}
