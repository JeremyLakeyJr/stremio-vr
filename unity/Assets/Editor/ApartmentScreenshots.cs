using System.IO;
using UnityEditor;
using UnityEngine;
namespace StremioVR.Editor {
public static class ApartmentScreenshots {
 public static void Capture(){
  HomeWorldBuild.Prepare();var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Worlds/Home.prefab");if(!prefab)throw new System.Exception("Home required");
  var world=Object.Instantiate(prefab);var data=world.GetComponent<CinemaWorldData>();RenderSettings.skybox=data.Skybox;
  Shader.SetGlobalTexture("_CinemaEdgeColors",Texture2D.blackTexture);Shader.SetGlobalFloat("_CinemaAmbientDim",0);Shader.SetGlobalFloat("_CinemaBounceStrength",0);
  var go=new GameObject("Photo camera",typeof(Camera));var camera=go.GetComponent<Camera>();camera.enabled=false;camera.fieldOfView=72;camera.nearClipPlane=0.05f;camera.farClipPlane=150;camera.allowHDR=false;camera.clearFlags=CameraClearFlags.Skybox;
  var texture=new RenderTexture(1600,900,24);texture.Create();camera.targetTexture=texture;var original=RenderTexture.active;
  string folder=Path.GetFullPath("../docs/screenshots");Directory.CreateDirectory(folder);
  Vector3[] positions={new Vector3(0,1.75f,3.2f),new Vector3(3,1.75f,1),new Vector3(-2,2.2f,2)};
  Vector3[] targets={new Vector3(0,1.1f,-2),new Vector3(-2,1.2f,-2),new Vector3(2,1.2f,-2)};
  string[] names={"lounge","screen","overview"};
  try{for(int i=0;i<3;i++){camera.transform.position=positions[i];camera.transform.LookAt(targets[i]);camera.Render();RenderTexture.active=texture;var photo=new Texture2D(1600,900,TextureFormat.RGB24,false);photo.ReadPixels(new Rect(0,0,1600,900),0,0);photo.Apply();File.WriteAllBytes(Path.Combine(folder,"apartment-"+names[i]+".png"),photo.EncodeToPNG());Object.DestroyImmediate(photo);}}
  finally{RenderTexture.active=original;camera.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(go);Object.DestroyImmediate(world);}
 }
}
}
