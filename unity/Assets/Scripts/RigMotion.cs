using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;
namespace StremioVR {
[DefaultExecutionOrder(-50)]
public sealed class RigMotion : MonoBehaviour {
 public Transform Rig, Tracking, Head, LeftHand;
 public RoomClient Room; public WorldManager World;
 public Func<bool> MenuVisible;
 public event Action<string> Status;
 public float MoveSpeed=1.5f, TurnSpeed=60, RayPitch=45;
 public bool SnapTurn = false;
 public float SnapAngle = 45f;
 public bool HandOriented = false;
 public bool SprintEnabled = true;
 public bool TeleportMode = false;
 Vector3 teleportTarget; bool teleportAiming;
 public float SprintMultiplier = 1.75f;
 public bool Seated => seatedSeat>=0;
 public float ViewPitch => Mathf.DeltaAngle(0,Tracking.localEulerAngles.x);
 public float AvatarEyeHeight { get; private set; } = 1.65f;
 CharacterController controller; int seatedSeat=-1; string joinedRoom; bool standDown; float verticalVelocity;
 bool snapReset = true;

 public void Initialize() {
  Rig.gameObject.layer=8; controller=Rig.gameObject.AddComponent<CharacterController>(); controller.radius=0.2f; controller.height=1.65f; controller.center=Vector3.up*0.825f; controller.stepOffset=0.35f; controller.slopeLimit=55;
  Rig.position=World.Data.Spawn; Room.Message+=Message;
 }
 public void SetAvatarHeight(float eyeHeight) {
  if (eyeHeight > 0.3f && eyeHeight < 4.0f) {
   AvatarEyeHeight = eyeHeight;
   if (controller != null) {
    controller.height = AvatarEyeHeight;
    controller.center = Vector3.up * (AvatarEyeHeight * 0.5f);
   }
   float currentHeadY = Head != null ? Head.localPosition.y : 1.65f;
   if (currentHeadY > 0.2f && Tracking != null) {
    float scaleRatio = AvatarEyeHeight / currentHeadY;
    Tracking.localScale = Vector3.one * Mathf.Clamp(scaleRatio, 0.35f, 2.5f);
   }
  }
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
  
  // Smooth thumbstick input with quadratic curve & deadzone
  left.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 rawStick);
  Vector2 stick = Vector2.zero;
  float mag = rawStick.magnitude;
  if(mag >= 0.15f) {
   float curved = (mag - 0.15f) / 0.85f;
   stick = rawStick.normalized * (curved * curved);
  }
  
  // Left stick click sprint / fast walk
  left.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool sprintClick);
  float activeSpeed = (SprintEnabled && sprintClick) ? MoveSpeed * SprintMultiplier : MoveSpeed;

  if(Seated && rawStick.magnitude>0.4f) Stand();
  
  // Turn controls: Snap Turn vs Smooth Turn
  right.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 turn);
  if(SnapTurn) {
   if(Mathf.Abs(turn.x) > 0.65f) {
    if(snapReset) {
     float snap = Mathf.Sign(turn.x) * SnapAngle;
     Rig.RotateAround(Head.position, Vector3.up, snap);
     snapReset = false;
    }
   } else if(Mathf.Abs(turn.x) < 0.25f) {
    snapReset = true;
   }
  } else {
   if(Mathf.Abs(turn.x) > 0.15f) {
    float curveTurn = Mathf.Sign(turn.x) * ((Mathf.Abs(turn.x) - 0.15f) / 0.85f);
    Rig.RotateAround(Head.position, Vector3.up, curveTurn * TurnSpeed * Time.deltaTime);
   }
  }

  if(MenuVisible?.Invoke()==true || Seated) return;
  
  // Movement direction: Hand-oriented vs Head-oriented
  Vector3 forward = (HandOriented && LeftHand != null) ? FlatDirection(LeftHand.forward) : FlatDirection(Head.forward);
  Vector3 rightward = Vector3.Cross(Vector3.up, forward);
  
  var localHead = Rig.InverseTransformPoint(Head.position);
  float height = AvatarEyeHeight > 0.4f ? AvatarEyeHeight : Mathf.Clamp(localHead.y, 0.8f, 2.4f);
  controller.height = height;
  controller.center = new Vector3(localHead.x, height * 0.5f, localHead.z);
  
  if (TeleportMode) {
   if (stick.y > 0.5f) {
    teleportAiming = true;
    teleportTarget = Rig.position + forward * (stick.y * 3f);
    if (Physics.Raycast(teleportTarget + Vector3.up * 2f, Vector3.down, out var hit, 5f)) {
     teleportTarget = hit.point;
    }
   } else if (teleportAiming && stick.magnitude < 0.2f) {
    teleportAiming = false;
    Teleport(teleportTarget, false);
   }
  } else {
   verticalVelocity = controller.isGrounded ? -2.5f : Mathf.Max(-10f, verticalVelocity - 9.81f * Time.deltaTime);
   controller.Move(((forward * stick.y + rightward * stick.x) * activeSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
  }
  if(Head.position.y < -3) Teleport(World.Data.Spawn, false);
 }
 void OnDestroy() { if(Room!=null) Room.Message-=Message; }
}
}
