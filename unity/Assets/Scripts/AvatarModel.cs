using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UniVRM10;
namespace StremioVR {
// Shared loading, tracking and fallback path for preview, mirror and remote players.
public sealed class AvatarModel:MonoBehaviour {
 public string Status {get;private set;}="R6";
 public bool Ready {get;private set;}
 public float EyeHeight {get;private set;}=1.65f;
 public event Action<float> HeightReady;
 static Light avatarLight;
 R6Avatar fallback;Vrm10Instance vrm;Animator animator;Vector3 restHead;bool seated,hasPose;
 Vector3 headPosition,leftPosition,rightPosition;Quaternion headRotation,restHeadRotation;
 Quaternion leftRotation=Quaternion.identity,rightRotation=Quaternion.identity,restLeftHand=Quaternion.identity,restRightHand=Quaternion.identity;
 readonly Dictionary<Transform,Quaternion> rest=new Dictionary<Transform,Quaternion>();
 readonly CancellationTokenSource cancellation=new CancellationTokenSource();
 static readonly System.Threading.SemaphoreSlim imports=new System.Threading.SemaphoreSlim(1,1);
 public static AvatarModel Create(Transform parent,JObject profile){var go=new GameObject("Player avatar");go.transform.SetParent(parent,false);var model=go.AddComponent<AvatarModel>();model.fallback=R6Avatar.Create(go.transform,profile);string id=(string)profile["avatarId"];model.Ready=string.IsNullOrEmpty(id);if(!string.IsNullOrEmpty(id))model.Load(id);return model;}
 async void Load(string id){
  Vrm10Instance loaded=null;bool locked=false;var token=cancellation.Token;
  try{
   Status="Downloading avatar…";var bytes=await OpenAvatarCatalog.Download(id);token.ThrowIfCancellationRequested();
   Status="Loading avatar…";await imports.WaitAsync(token);locked=true;
   loaded=await Vrm10.LoadBytesAsync(bytes,canLoadVrm0X:true,controlRigGenerationOption:ControlRigGenerationOption.None,showMeshes:false,ct:token);
   token.ThrowIfCancellationRequested();if(!loaded)throw new Exception("Unsupported VRM avatar");
   loaded.UpdateType=Vrm10Instance.UpdateTypes.None;
   animator=loaded.GetComponent<Animator>();if(!animator||!animator.isHuman)throw new Exception("Avatar has no humanoid rig");animator.enabled=false;
   var head=animator.GetBoneTransform(HumanBodyBones.Head);if(!head)throw new Exception("Avatar has no head bone");
   loaded.transform.SetParent(transform,false);loaded.transform.localPosition=Vector3.zero;loaded.transform.localRotation=Quaternion.identity;
   float height=loaded.transform.InverseTransformPoint(head.position).y;if(height<.2f||height>5)throw new Exception("Unsupported avatar proportions");
   EyeHeight=height;
   loaded.transform.localScale=Vector3.one;
   foreach(var part in loaded.GetComponentsInChildren<Transform>(true)){part.gameObject.layer=gameObject.layer;rest[part]=part.localRotation;}
   foreach(var renderer in loaded.GetComponentsInChildren<Renderer>(true)){renderer.enabled=true;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;}
   var lHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);var rHand=animator.GetBoneTransform(HumanBodyBones.RightHand);
   if(lHand)restLeftHand=Quaternion.Inverse(transform.rotation)*lHand.rotation;
   if(rHand)restRightHand=Quaternion.Inverse(transform.rotation)*rHand.rotation;
   restHead=transform.InverseTransformPoint(head.position);restHeadRotation=Quaternion.Inverse(transform.rotation)*head.rotation;vrm=loaded;loaded=null;
   fallback.gameObject.SetActive(false);Destroy(fallback.gameObject);fallback=null;Ready=true;
   HeightReady?.Invoke(EyeHeight);
   if(!avatarLight){var lightObject=new GameObject("Avatar studio lighting");avatarLight=lightObject.AddComponent<Light>();avatarLight.type=LightType.Directional;avatarLight.intensity=.9f;avatarLight.shadows=LightShadows.None;avatarLight.cullingMask=(1<<0)|(1<<29)|(1<<30);lightObject.transform.rotation=Quaternion.Euler(45,-30,0);}
   Status=OpenAvatarCatalog.Credit(OpenAvatarCatalog.Find(id));
   if(hasPose)SetPose(headPosition,headRotation,leftPosition,leftRotation,rightPosition,rightRotation);
   else{PoseArm(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,transform.TransformPoint(new Vector3(-.35f,.9f,.12f)),Quaternion.identity,restLeftHand);PoseArm(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,transform.TransformPoint(new Vector3(.35f,.9f,.12f)),Quaternion.identity,restRightHand);}
  }catch(OperationCanceledException){}catch(Exception error){if(this){Status="Using R6: "+error.Message;Debug.LogWarning("[Avatar] "+Status);}}
  finally{if(loaded)Destroy(loaded.gameObject);if(locked)imports.Release();}
 }
 public void SetSeated(bool value){seated=value;if(fallback)fallback.SetSeated(value);}
 public void SetPose(Vector3 head,Quaternion rotation,Vector3 left,Quaternion leftRot,Vector3 right,Quaternion rightRot){
  hasPose=true;headPosition=head;headRotation=rotation;leftPosition=left;leftRotation=leftRot;rightPosition=right;rightRotation=rightRot;
  if(fallback){fallback.SetPose(head,rotation,left,right);return;}if(!vrm)return;
  foreach(var item in rest)if(item.Key)item.Key.localRotation=item.Value;
  transform.rotation=Quaternion.Euler(0,rotation.eulerAngles.y,0);transform.position=head-transform.TransformVector(restHead);
  animator.GetBoneTransform(HumanBodyBones.Head).rotation=rotation*restHeadRotation;
  PoseArm(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,left,leftRot,restLeftHand);
  PoseArm(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,right,rightRot,restRightHand);
  if(seated){SeatLeg(HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot);SeatLeg(HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot);}
 }
 public void SetPose(Vector3 head,Quaternion rotation,Vector3 left,Vector3 right){
  SetPose(head,rotation,left,Quaternion.identity,right,Quaternion.identity);
 }
 void PoseArm(HumanBodyBones upperBone,HumanBodyBones lowerBone,HumanBodyBones handBone,Vector3 target,Quaternion handRot,Quaternion restHand){
  var upper=animator.GetBoneTransform(upperBone);var lower=animator.GetBoneTransform(lowerBone);var hand=animator.GetBoneTransform(handBone);if(!upper||!lower||!hand)return;
  float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,hand.position);if(a<.001f||b<.001f)return;
  Vector3 delta=target-upper.position;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);Vector3 direction=delta.sqrMagnitude>.00001f?delta.normalized:transform.forward;
  Vector3 bend=Vector3.ProjectOnPlane(-transform.up,direction).normalized;if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(-transform.forward,direction).normalized;
  float along=(a*a-b*b+distance*distance)/(2*distance);Vector3 elbow=upper.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
  upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
  lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,upper.position+direction*distance-lower.position)*lower.rotation;
  if(handRot!=Quaternion.identity){hand.rotation=handRot*restHand;}
 }
 void SeatLeg(HumanBodyBones upperBone,HumanBodyBones lowerBone,HumanBodyBones footBone){var upper=animator.GetBoneTransform(upperBone);var lower=animator.GetBoneTransform(lowerBone);var foot=animator.GetBoneTransform(footBone);if(!upper||!lower||!foot)return;upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,transform.forward)*upper.rotation;lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,-transform.up)*lower.rotation;}
 void OnDestroy(){cancellation.Cancel();cancellation.Dispose();}
}
}
