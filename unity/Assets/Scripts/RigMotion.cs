using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;
namespace StremioVR {
[DefaultExecutionOrder(-50)]
public sealed class RigMotion : MonoBehaviour {
 public Transform Rig, Tracking, Head, LeftHand, RightHand;
 public RoomClient Room; public WorldManager World;
 public Func<bool> MenuVisible;
 public event Action<string> Status;
 public float MoveSpeed=1.5f, TurnSpeed=60, RayPitch=45;
 public bool SnapTurn = false;
 public float SnapAngle = 45f;
 public bool HandOriented = false;
 public bool SprintEnabled = true;
 public bool TeleportMode = false;
 Vector3 teleportTarget; bool teleportAiming, teleportValid;
 LineRenderer teleportLine; GameObject teleportMarker, teleportArrow;
 Material teleportLineMat, teleportMarkerMat;
 public float SprintMultiplier = 1.75f;
 public bool Seated => seatedSeat>=0;
 public float ViewPitch => Mathf.DeltaAngle(0,Tracking.localEulerAngles.x);
 public float AvatarEyeHeight { get; private set; } = 1.65f;
 CharacterController controller; int seatedSeat=-1; string joinedRoom; bool standDown; float verticalVelocity;
 bool snapReset = true;

 public void Initialize() {
  Rig.gameObject.layer=8; controller=Rig.gameObject.AddComponent<CharacterController>(); controller.radius=0.2f; controller.height=1.65f; controller.center=Vector3.up*0.825f; controller.stepOffset=0.35f; controller.slopeLimit=55;
  Rig.position=World.Data.Spawn; Room.Message+=Message;
  var lineObj = new GameObject("TeleportArc", typeof(LineRenderer));
  lineObj.transform.SetParent(Rig, false);
  teleportLine = lineObj.GetComponent<LineRenderer>();
  teleportLine.useWorldSpace = true;
  teleportLine.startWidth = 0.02f;
  teleportLine.endWidth = 0.05f;
  teleportLine.positionCount = 20;
  var unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
  teleportLineMat = new Material(unlitShader) { color = new Color(0.15f, 0.85f, 0.45f, 0.85f) };
  teleportLine.material = teleportLineMat;
  teleportLine.enabled = false;

  teleportMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
  teleportMarker.name = "TeleportMarker";
  teleportMarker.transform.SetParent(Rig, false);
  UnityEngine.Object.Destroy(teleportMarker.GetComponent<Collider>());
  teleportMarker.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);
  teleportMarkerMat = new Material(unlitShader) { color = new Color(0.15f, 0.85f, 0.45f, 0.7f) };
  teleportMarker.GetComponent<Renderer>().material = teleportMarkerMat;

  teleportArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
  teleportArrow.name = "TeleportDirection";
  teleportArrow.transform.SetParent(teleportMarker.transform, false);
  UnityEngine.Object.Destroy(teleportArrow.GetComponent<Collider>());
  teleportArrow.transform.localPosition = new Vector3(0, 0.8f, 0.35f);
  teleportArrow.transform.localScale = new Vector3(0.12f, 0.4f, 0.35f);
  teleportArrow.GetComponent<Renderer>().material = teleportMarkerMat;
  teleportMarker.SetActive(false);
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
  var left=VrInput.DeviceAt(XRNode.LeftHand); var right=VrInput.DeviceAt(XRNode.RightHand);
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

  // Teleport stick controls:
  // Can be activated via dedicated TeleportMode (Left stick forward > 0.35f)
  // OR via Right stick forward (turn.y > 0.55f) in any locomotion mode!
  bool rightStickTeleport = turn.y > 0.55f;
  bool leftStickTeleport = TeleportMode && stick.y > 0.35f;
  bool wantTeleport = rightStickTeleport || leftStickTeleport;

  if (wantTeleport) {
   teleportAiming = true;
   Vector3 startPos = rightStickTeleport
       ? (RightHand != null ? RightHand.position : Head.position + Vector3.down * 0.2f)
       : (LeftHand != null ? LeftHand.position : Head.position + Vector3.down * 0.2f);
   Vector3 aimDir = rightStickTeleport
       ? (RightHand != null ? RightHand.forward : (Head.forward + Vector3.down * 0.35f).normalized)
       : ((HandOriented && LeftHand != null) ? LeftHand.forward : (Head.forward + Vector3.down * 0.35f).normalized);
   float power = rightStickTeleport ? turn.y : stick.y;
   Vector3 velocity = (aimDir + Vector3.up * 0.2f).normalized * (power * 8.5f);
   Vector3 current = startPos;
   Vector3 last = current;
   bool hitGround = false;
   Vector3 groundPoint = current;
   Vector3 groundNormal = Vector3.up;

   if (teleportLine != null) {
    teleportLine.enabled = true;
    teleportLine.positionCount = 20;
   }

   for (int p = 0; p < 20; p++) {
    if (teleportLine != null) teleportLine.SetPosition(p, current);
    if (!hitGround && p > 0) {
     if (Physics.Raycast(last, (current - last).normalized, out var hit, Vector3.Distance(last, current), ~((1 << 8) | (1 << 9) | (1 << 2)))) {
      // Validate surface steepness: only walkable surfaces with slope <= 56 degrees
      if (Vector3.Dot(hit.normal, Vector3.up) > 0.55f) {
       hitGround = true;
       groundPoint = hit.point;
       groundNormal = hit.normal;
      }
      if (teleportLine != null) {
       for (int r = p; r < 20; r++) teleportLine.SetPosition(r, hit.point);
      }
      break;
     }
    }
    last = current;
    current += velocity * 0.05f;
    velocity += Vector3.down * (9.81f * 0.05f);
   }

   teleportValid = hitGround;
   if (teleportValid) {
    teleportTarget = groundPoint;
    if (teleportLineMat != null) teleportLineMat.color = new Color(0.15f, 0.85f, 0.45f, 0.85f);
    if (teleportMarker != null) {
     teleportMarker.SetActive(true);
     teleportMarker.transform.position = groundPoint + Vector3.up * 0.02f;
     teleportMarker.transform.up = groundNormal;
     Vector3 fwd = FlatDirection(Head.forward);
     if (fwd.sqrMagnitude > 0.01f) teleportMarker.transform.rotation = Quaternion.LookRotation(fwd, groundNormal);
     if (teleportMarkerMat != null) teleportMarkerMat.color = new Color(0.15f, 0.85f, 0.45f, 0.7f);
    }
   } else {
    if (teleportLineMat != null) teleportLineMat.color = new Color(0.95f, 0.25f, 0.2f, 0.75f);
    if (teleportMarker != null) teleportMarker.SetActive(false);
   }
  } else if (teleportAiming && !wantTeleport) {
   teleportAiming = false;
   if (teleportLine != null) teleportLine.enabled = false;
   if (teleportMarker != null) teleportMarker.SetActive(false);
   if (teleportValid) {
    Teleport(teleportTarget, false);
   }
   teleportValid = false;
  } else if (!teleportAiming) {
   if (teleportLine != null && teleportLine.enabled) teleportLine.enabled = false;
   if (teleportMarker != null && teleportMarker.activeSelf) teleportMarker.SetActive(false);
  }

  if (!TeleportMode) {
   verticalVelocity = controller.isGrounded ? -2.5f : Mathf.Max(-10f, verticalVelocity - 9.81f * Time.deltaTime);
   controller.Move(((forward * stick.y + rightward * stick.x) * activeSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
  }

  // Prevent endless falling in Void or Passthrough modes
  if (World != null && (World.CurrentEnvMode == WorldManager.EnvironmentMode.Void || World.CurrentEnvMode == WorldManager.EnvironmentMode.Passthrough)) {
   if (Rig.position.y < 0f) {
    var p = Rig.position;
    p.y = 0f;
    Rig.position = p;
    verticalVelocity = 0;
   }
  }
  if (Head.position.y < -3 || Rig.position.y < -5) Teleport(World.Data.Spawn, false);
 }
 void OnDestroy() {
  if (teleportLine) Destroy(teleportLine.gameObject);
  if (teleportMarker) Destroy(teleportMarker);
  if (teleportLineMat) Destroy(teleportLineMat);
  if (teleportMarkerMat) Destroy(teleportMarkerMat);
  if (Room != null) Room.Message -= Message;
 }
}
}
