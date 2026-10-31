using NUnit.Framework;
using UnityEngine;
namespace StremioVR.Tests {
public class AmbientWorldTests {
 [Test] public void AmbientBlitRestoresActiveFramebuffer() {
  if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device");
  var original=RenderTexture.active; var previous=new RenderTexture(16,16,0);var target=new RenderTexture(8,1,0);
  try { previous.Create();target.Create();RenderTexture.active=previous;
   VideoAmbient.BlitPreservingTarget(Texture2D.whiteTexture,target);Assert.AreSame(previous,RenderTexture.active);
  } finally {RenderTexture.active=original;previous.Release();target.Release();Object.DestroyImmediate(previous);Object.DestroyImmediate(target);}
 }
 [Test] public void EdgeSamplerIgnoresBrightCenter() {
  var pixels=new Color32[32*18];
  for(int y=0;y<18;y++) for(int x=0;x<32;x++) pixels[y*32+x]=x<2||x>=30||y<2||y>=16?Color.green:Color.red;
  foreach(var color in VideoAmbient.SampleEdges(pixels,32,18)) { Assert.Greater(color.g,0.99f); Assert.Less(color.r,0.01f); }
 }
 [Test] public void EdgeSamplerKeepsDifferentSidesIndependent() {
  var pixels=new Color32[32*18]; for(int y=0;y<18;y++) for(int x=0;x<32;x++) pixels[y*32+x]=x<2?Color.red:x>=30?Color.blue:Color.black;
  var colors=VideoAmbient.SampleEdges(pixels,32,18); Assert.Greater(colors[0].r,0.99f); Assert.Greater(colors[8].b,0.99f); Assert.Less(colors[20].maxColorComponent,0.01f);
 }
 [Test] public void EachEdgeHasEightIndependentSamplesAndVerticalBarsMatchTheImage(){
  const int width=80,height=80;var pixels=new Color32[width*height];
  for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=x<2?new Color(y/10f/8,0,0):x>=width-2?new Color(0,y/10f/8,0):y<2?Color.blue:y>=height-2?Color.yellow:Color.black;
  var samples=VideoAmbient.SampleEdges(pixels,width,height);Assert.AreEqual(32,samples.Length);
  for(int n=1;n<8;n++){Assert.Greater(samples[n].r,samples[n-1].r);Assert.Greater(samples[8+n].g,samples[8+n-1].g);}
  for(int n=0;n<8;n++){Assert.Greater(samples[24+n].r,.99f);Assert.Greater(samples[16+n].b,.99f);}
 }
 [Test] public void GpuEdgeSamplesFollowDisplayTextureOrientation(){
  if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics");
  var source=new Texture2D(32,32,TextureFormat.RGBA32,false,true);var target=new RenderTexture(32,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var read=new Texture2D(32,1,TextureFormat.RGBAFloat,false,true);var material=new Material(Shader.Find("MovieNight/EdgeSmooth"));var previous=RenderTexture.active;
  try{for(int y=0;y<32;y++)for(int x=0;x<32;x++)source.SetPixel(x,y,y<16?Color.blue:Color.red);source.Apply();target.Create();material.SetTexture("_History",Texture2D.blackTexture);material.SetFloat("_Blend",1);material.SetFloat("_Available",1);
   foreach(bool flipped in new[]{false,true}){material.SetVector("_SourceScaleOffset",flipped?new Vector4(1,-1,0,1):new Vector4(1,1,0,0));VideoAmbient.BlitPreservingTarget(source,target,material);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,32,1),0,0);read.Apply();Assert.That(flipped?read.GetPixel(20,0).r:read.GetPixel(20,0).b,Is.GreaterThan(.99f));Assert.That(flipped?read.GetPixel(28,0).b:read.GetPixel(28,0).r,Is.GreaterThan(.99f));}
  }finally{RenderTexture.active=previous;target.Release();Object.DestroyImmediate(source);Object.DestroyImmediate(target);Object.DestroyImmediate(read);Object.DestroyImmediate(material);}
 }
 [Test] public void BrowserYuvMidGrayIsConvertedToLinearOnce(){
  if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics");
  var y=new Texture2D(2,2,TextureFormat.R8,false,true);var uv=new Texture2D(1,1,TextureFormat.R8,false,true);var target=new RenderTexture(2,2,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var read=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);var material=new Material(Shader.Find("MovieNight/BrowserYuv"));var previous=RenderTexture.active;
  try{y.LoadRawTextureData(new byte[]{126,126,126,126});y.Apply();uv.LoadRawTextureData(new byte[]{128});uv.Apply();material.SetTexture("_U",uv);material.SetTexture("_V",uv);target.Create();VideoAmbient.BlitPreservingTarget(y,target,material);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,2,2),0,0);read.Apply();float expected=QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.GammaToLinearSpace(110f/219):110f/219;Assert.That(read.GetPixel(0,0).r,Is.EqualTo(expected).Within(.008f));}
  finally{RenderTexture.active=previous;target.Release();Object.DestroyImmediate(y);Object.DestroyImmediate(uv);Object.DestroyImmediate(target);Object.DestroyImmediate(read);Object.DestroyImmediate(material);}
 }
 [Test] public void GlowRingInnerBoundaryMatchesFlatAndCurvedScreens(){
  foreach(float curve in new[]{0f,60f,120f}){
   var screen=MonitorSurface.BuildMesh(3,1.6875f,curve,false);var glow=MonitorSurface.BuildMesh(3,1.6875f,curve,true);
   try{var sv=screen.vertices;var su=screen.uv;var gv=glow.vertices;var gu=glow.uv;
    for(int i=0;i<sv.Length;i++){bool found=false;for(int j=0;j<gv.Length;j++)if(Vector2.Distance(su[i],gu[j])<.00001f){Assert.That(gv[j].x,Is.EqualTo(sv[i].x).Within(.00001));Assert.That(gv[j].y,Is.EqualTo(sv[i].y).Within(.00001));Assert.That(gv[j].z,Is.EqualTo(sv[i].z+.001f).Within(.00001));found=true;break;}Assert.IsTrue(found,"Every screen boundary vertex needs a matching halo vertex");}
   }finally{Object.DestroyImmediate(screen);Object.DestroyImmediate(glow);}
  }
 }
 [Test] public void CurvedMonitorPointerMatchesSurfaceUv(){
  foreach(float curve in new[]{0f,30f,90f,120f})foreach(float width in new[]{.6f,2.1f,8f})foreach(float u in new[]{-.45f,0f,.45f}){
   var p=MonitorSurface.SurfacePoint(width,width*9/16,curve,u,.2f);var origin=p+Vector3.back*10;
   Assert.IsTrue(MonitorSurface.Intersect(width,width*9/16,curve,origin,Vector3.forward,out float distance,out var uv));Assert.That(distance,Is.EqualTo(10).Within(.001));Assert.That(uv.x,Is.EqualTo(u+.5f).Within(.001));Assert.That(uv.y,Is.EqualTo(.7f).Within(.001));
  }
 }
 [Test] public void MonitorPointerRejectsBackAndOutside(){
  foreach(float curve in new[]{0f,90f}){Assert.IsFalse(MonitorSurface.Intersect(2,1,curve,new Vector3(0,0,3),Vector3.back,out _,out _));Assert.IsFalse(MonitorSurface.Intersect(2,1,curve,new Vector3(0,2,-3),Vector3.forward,out _,out _));}
 }
 [Test] public void TableSquaresRoundTripAcrossEveryRank() {for(int i=0;i<64;i++)Assert.AreEqual(i,TabletopGames.Index(TabletopGames.Square(i)));Assert.AreEqual("a8",TabletopGames.Square(0));Assert.AreEqual("h1",TabletopGames.Square(63));}
 [Test] public void MirrorReflectionPreservesPlaneAndFlipsDistance() {
  var reflection=RoomMirror.Reflection(new Vector4(0,0,1,-3));
  Assert.That(reflection.MultiplyPoint(new Vector3(2,1,3)),Is.EqualTo(new Vector3(2,1,3)));
  Assert.That(reflection.MultiplyPoint(new Vector3(2,1,5)),Is.EqualTo(new Vector3(2,1,1)));
  Assert.That(reflection*reflection,Is.EqualTo(Matrix4x4.identity));
 }
 [Test] public void DownstairsFloorRetainsTextureWithoutFalseEmission() {
  var prefab=Resources.Load<GameObject>("Worlds/Home");if(!prefab)Assert.Ignore("Home source is not installed");
  Renderer floor=null;foreach(var r in prefab.GetComponentsInChildren<Renderer>())if(r.name=="Cyberpunk_4")floor=r;
  Assert.IsNotNull(floor);Assert.IsNotNull(floor.sharedMaterial.mainTexture);
  Assert.Less(floor.sharedMaterial.GetColor("_EmissionColor").maxColorComponent,0.001f);
  Assert.AreEqual(0,floor.sharedMaterial.GetFloat("_VideoReactive"));
 }
 [Test] public void OriginalChessMeshesFitBoardSquares() {
  foreach(string name in new[]{"Pawn","Rook","Knight","Bishop","Queen","King"}){
   var mesh=Resources.Load<Mesh>("Chess/"+name);Assert.IsNotNull(mesh,name);
   Assert.Greater(mesh.vertexCount,20000,name);Assert.Less(mesh.bounds.size.x,.14f,name);Assert.Less(mesh.bounds.size.z,.14f,name);
   Assert.Less(Mathf.Abs(mesh.bounds.min.y),0.0001f,name);Assert.Greater(mesh.bounds.size.y,.1f,name);
  }
 }
 [Test] public void PawnIsDistinctFromRookDespiteMislabeledSource3mf(){
  var pawn=Resources.Load<Mesh>("Chess/Pawn");var rook=Resources.Load<Mesh>("Chess/Rook");
  Assert.IsNotNull(pawn);Assert.IsNotNull(rook);Assert.Less(pawn.bounds.size.y,rook.bounds.size.y);
  Assert.AreEqual(54134*3,pawn.triangles.Length,"Original Pawn.stl triangles must be preserved");
 }
 [Test] public void InstalledPrivateHomeHasScreenAndSixSeats() {
  var prefab=Resources.Load<GameObject>("Worlds/Home"); if(!prefab) Assert.Ignore("Private home asset is not installed");
  var data=prefab.GetComponent<CinemaWorldData>(); Assert.AreEqual(6,data.Seats.Length); Assert.Greater(data.ScreenSize.x,data.ScreenSize.y); Assert.Greater(data.ScreenPosition.z,2); Assert.Greater(prefab.GetComponentsInChildren<MeshCollider>().Length,0);
 }
}
}
