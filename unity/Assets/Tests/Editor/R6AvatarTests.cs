using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace StremioVR.Tests {
public class R6AvatarTests {
 static JObject Profile => new JObject { ["skin"]="#b98059", ["color"]="#7958ff", ["head"]="square", ["body"]="wide", ["hair"]="tall", ["accessory"]="glasses", ["outfit"]="formal" };
 [Test] public void SuppliedModelHasSixUsablePartsAndFace() {
  var prefab=Resources.Load<GameObject>("Avatars/R6"); Assert.IsNotNull(prefab); Assert.AreEqual(6,prefab.transform.childCount);
  foreach(string name in new[]{"Head","Torso","Left Arm","Right Arm","Left Leg","Right Leg"}) {
   var part=prefab.transform.Find(name); Assert.IsNotNull(part,name); var mesh=part.GetComponent<MeshFilter>().sharedMesh;
   Assert.Greater(mesh.vertexCount,0); Assert.Greater(mesh.bounds.size.y,0.1f);
  }
  Assert.IsNotNull(Resources.Load<Texture2D>("Avatars/Head1_diff")); Assert.IsNotNull(Shader.Find("StremioVR/Avatar"));
 }
 [Test] public void TrackedHeadRemainsAlignedAcrossYawAndSeatChanges() {
  var parent=new GameObject("Test"); var avatar=R6Avatar.Create(parent.transform,Profile);
  foreach(float yaw in new[]{0f,90f,180f,270f}) {
   var head=new Vector3(2,1.65f,-3); var rotation=Quaternion.Euler(15,yaw,0);
   avatar.SetPose(head,rotation,head+new Vector3(-0.4f,-0.5f,0.3f),head+new Vector3(0.4f,-0.5f,0.3f));
   Assert.Less(Vector3.Distance(head,avatar.Head.position),0.001f); Assert.Less(Quaternion.Angle(rotation,avatar.Head.rotation),0.01f);
   Assert.Less(Vector3.Distance(avatar.transform.Find("Left Arm").position,head),1.5f);
  }
  Object.DestroyImmediate(parent);
 }
}
}
