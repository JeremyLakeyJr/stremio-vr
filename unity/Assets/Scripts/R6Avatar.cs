using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
// The six rigid meshes are extracted from the supplied rig, preserving its geometry and UVs.
public sealed class R6Avatar : MonoBehaviour {
 public Transform Head { get; private set; }
 Transform torso, leftArm, rightArm; Vector3 restHead, restLeft, restRight;
 readonly List<Material> materials = new List<Material>();
 public static R6Avatar Create(Transform parent, JObject profile) {
  var prefab=Resources.Load<GameObject>("Avatars/R6");
  if(!prefab) throw new System.InvalidOperationException("Prepare the R6 prefab using Stremio VR/Prepare R6 avatar");
  var root=Instantiate(prefab,parent,false); var avatar=root.AddComponent<R6Avatar>(); avatar.Configure(profile); return avatar;
 }
 void Configure(JObject profile) {
  Head=transform.Find("Head"); torso=transform.Find("Torso"); leftArm=transform.Find("Left Arm"); rightArm=transform.Find("Right Arm");
  restHead=Head.localPosition; restLeft=leftArm.localPosition; restRight=rightArm.localPosition;
  ColorUtility.TryParseHtmlString((string)profile["skin"],out var skin); ColorUtility.TryParseHtmlString((string)profile["color"],out var outfit);
  foreach(var renderer in GetComponentsInChildren<MeshRenderer>()) {
   var material=new Material(Shader.Find("StremioVR/Avatar")); materials.Add(material); material.SetFloat("_AmbientResponse",0.35f);
   material.color=renderer.name=="Torso" ? outfit : renderer.name.Contains("Leg") ? outfit*0.55f : skin;
   if(renderer.name=="Head") material.mainTexture=Resources.Load<Texture2D>("Avatars/Head1_diff");
   renderer.sharedMaterial=material;
  }
  if((string)profile["body"]=="wide") torso.localScale=new Vector3(1.18f,1,1);
  if((string)profile["head"]=="square") Head.localScale=new Vector3(1.06f,0.94f,1.06f);
  string hair=(string)profile["hair"];
  if(hair!="none") Add(PrimitiveType.Cube,Head,new Vector3(0,0.2f,0),new Vector3(0.43f,hair=="tall"?0.2f:0.08f,0.4f),new Color(0.12f,0.08f,0.04f));
  string accessory=(string)profile["accessory"];
  if(accessory=="hat") { Add(PrimitiveType.Cylinder,Head,Vector3.up*0.26f,new Vector3(0.5f,0.06f,0.5f),outfit); }
  if(accessory=="headset") Add(PrimitiveType.Cube,Head,new Vector3(0,0,0.23f),new Vector3(0.43f,0.18f,0.12f),Color.gray);
  if(accessory=="glasses") foreach(float x in new[]{-0.11f,0.11f}) Add(PrimitiveType.Cube,Head,new Vector3(x,0.035f,0.22f),new Vector3(0.17f,0.07f,0.025f),Color.black);
  string clothing=(string)profile["outfit"];
  if(clothing!="casual") Add(PrimitiveType.Cube,torso,new Vector3(0,0,0.183f),clothing=="formal"?new Vector3(0.08f,0.55f,0.015f):new Vector3(0.65f,0.08f,0.015f),Color.white);
 }
 void Add(PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Color color) { var shape=AvatarPresence.Shape(type,parent,position,scale,color); materials.Add(shape.GetComponent<Renderer>().sharedMaterial); }
 public void SetSeated(bool value) {
  foreach(string name in new[]{"Left Leg","Right Leg"}) {
   var leg=transform.Find(name); var rest=Resources.Load<GameObject>("Avatars/R6").transform.Find(name);
   leg.localRotation=value?Quaternion.Euler(-80,0,0):Quaternion.identity;
   leg.localPosition=rest.localPosition+(value?new Vector3(0,0.23f,0.3f):Vector3.zero);
  }
 }
 public void SetPose(Vector3 headPosition,Quaternion headRotation,Vector3 leftHand,Vector3 rightHand) {
  transform.rotation=Quaternion.Euler(0,headRotation.eulerAngles.y,0);
  transform.position=headPosition-transform.TransformVector(restHead);
  Head.rotation=headRotation;
  Aim(leftArm,restLeft,leftHand); Aim(rightArm,restRight,rightHand);
 }
 void Aim(Transform arm,Vector3 rest,Vector3 hand) {
  var shoulder=transform.TransformPoint(rest+Vector3.up*0.32f); var direction=hand-shoulder;
  if(direction.sqrMagnitude<0.001f) direction=Vector3.down;
  arm.rotation=Quaternion.FromToRotation(Vector3.down,direction.normalized)*transform.rotation;
  arm.position=shoulder+direction.normalized*0.32f;
 }
 void OnDestroy() { foreach(var material in materials) if(material) { if(Application.isPlaying) Destroy(material); else DestroyImmediate(material); } }
}
}
