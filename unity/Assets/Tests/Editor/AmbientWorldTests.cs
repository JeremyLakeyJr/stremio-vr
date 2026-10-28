using NUnit.Framework;
using UnityEngine;
namespace StremioVR.Tests {
public class AmbientWorldTests {
 [Test] public void EdgeSamplerIgnoresBrightCenter() {
  var pixels=new Color32[32*18];
  for(int y=0;y<18;y++) for(int x=0;x<32;x++) pixels[y*32+x]=x<2||x>=30||y<2||y>=16?Color.green:Color.red;
  foreach(var color in VideoAmbient.SampleEdges(pixels,32,18)) { Assert.Greater(color.g,0.99f); Assert.Less(color.r,0.01f); }
 }
 [Test] public void EdgeSamplerKeepsDifferentSidesIndependent() {
  var pixels=new Color32[32*18]; for(int y=0;y<18;y++) for(int x=0;x<32;x++) pixels[y*32+x]=x<2?Color.red:x>=30?Color.blue:Color.black;
  var colors=VideoAmbient.SampleEdges(pixels,32,18); Assert.Greater(colors[0].r,0.99f); Assert.Greater(colors[2].b,0.99f); Assert.Less(colors[4].maxColorComponent,0.01f);
 }
 [Test] public void MirrorReflectionPreservesPlaneAndFlipsDistance() {
  var reflection=RoomMirror.Reflection(new Vector4(0,0,1,-3));
  Assert.That(reflection.MultiplyPoint(new Vector3(2,1,3)),Is.EqualTo(new Vector3(2,1,3)));
  Assert.That(reflection.MultiplyPoint(new Vector3(2,1,5)),Is.EqualTo(new Vector3(2,1,1)));
  Assert.That(reflection*reflection,Is.EqualTo(Matrix4x4.identity));
 }
 [Test] public void InstalledPrivateHomeHasScreenAndSixSeats() {
  var prefab=Resources.Load<GameObject>("Worlds/Home"); if(!prefab) Assert.Ignore("Private home asset is not installed");
  var data=prefab.GetComponent<CinemaWorldData>(); Assert.AreEqual(6,data.Seats.Length); Assert.Greater(data.ScreenSize.x,data.ScreenSize.y); Assert.Greater(data.ScreenPosition.z,2); Assert.Greater(prefab.GetComponentsInChildren<MeshCollider>().Length,0);
 }
}
}
