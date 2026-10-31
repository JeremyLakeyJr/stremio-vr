using NUnit.Framework;
using UnityEngine;
namespace StremioVR.Tests {
public class HaloBlendTests {
 [Test] public void HaloFadesAtBothBoundariesAndPreservesBackgroundAlpha(){
  if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics");
  var cameraObject=new GameObject("Halo test camera");var surface=new GameObject("Halo test surface",typeof(MeshFilter),typeof(MeshRenderer));
  var mesh=MonitorSurface.BuildMesh(2,1,0,true);var material=new Material(Shader.Find("StremioVR/ScreenGlow"));var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var read=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);var oldTarget=RenderTexture.active;var oldField=Shader.GetGlobalTexture("_CinemaBounceField");float oldStrength=Shader.GetGlobalFloat("_CinemaBounceStrength");
  try{
   surface.layer=31;surface.GetComponent<MeshFilter>().sharedMesh=mesh;surface.GetComponent<Renderer>().sharedMaterial=material;surface.transform.localScale=new Vector3(2,1,1);material.SetVector("_CinemaHaloMetrics",new Vector4(2,1,.95f,0));Shader.SetGlobalTexture("_CinemaBounceField",Texture2D.whiteTexture);Shader.SetGlobalFloat("_CinemaBounceStrength",1);
   var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,0,-3);camera.orthographic=true;camera.orthographicSize=2;camera.aspect=1;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.02f,.02f,.25f);camera.targetTexture=target;target.Create();camera.Render();
   RenderTexture.active=target;read.ReadPixels(new Rect(0,0,256,256),0,0);read.Apply();
   Color background=read.GetPixel(128,128),inner=read.GetPixel(192,128),shoulder=read.GetPixel(202,128),outer=read.GetPixel(250,128);
   Assert.Greater(shoulder.r,background.r+.05f);Assert.Less(inner.r,shoulder.r*.4f);Assert.That(outer.r,Is.EqualTo(background.r).Within(.002));Assert.That(shoulder.a,Is.EqualTo(background.a).Within(.001));
  }finally{RenderTexture.active=oldTarget;Shader.SetGlobalTexture("_CinemaBounceField",oldField);Shader.SetGlobalFloat("_CinemaBounceStrength",oldStrength);target.Release();Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(surface);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);Object.DestroyImmediate(target);Object.DestroyImmediate(read);}
 }
}
}
