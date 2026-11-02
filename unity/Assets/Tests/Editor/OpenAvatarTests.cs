using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UniVRM10;
namespace StremioVR.Tests {
public class OpenAvatarTests {
 static byte[] Glb(string json){byte[] text=Encoding.UTF8.GetBytes(json.PadRight((json.Length+3)/4*4));using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(0x46546c67);writer.Write(2);writer.Write(20+text.Length);writer.Write(text.Length);writer.Write(0x4e4f534a);writer.Write(text);return stream.ToArray();}}
 [Test] public void RejectsExternalResourcesAndOversizedMeshes(){
  Assert.Throws<Exception>(()=>OpenAvatarCatalog.Validate(new byte[20]));
  Assert.Throws<Exception>(()=>OpenAvatarCatalog.Validate(Glb("{\"extensions\":{\"VRM\":{}},\"images\":[{\"uri\":\"https://localhost/private\"}]}")));
  Assert.Throws<Exception>(()=>OpenAvatarCatalog.Validate(Glb("{\"extensions\":{\"VRM\":{}},\"accessors\":[{\"count\":999999}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]}")));
 }
 [Test] public void CatalogHasStableIdsAndCredits(){Assert.Greater(OpenAvatarCatalog.Entries.Count(),300);foreach(var entry in OpenAvatarCatalog.Entries){Assert.IsNotNull(OpenAvatarCatalog.Find((string)entry["id"]));Assert.IsNotEmpty((string)entry["creator"]);Assert.IsNotEmpty((string)entry["license"]);}Assert.IsNull(OpenAvatarCatalog.Find("https://example.com/arbitrary.vrm"));}
 [UnityTest] public IEnumerator RegistryVrmLoadsWithHumanoidAndVisibleMaterials(){
  string path=Path.Combine(Application.dataPath,"../Builds/avatar-fixture.vrm");if(!File.Exists(path))Assert.Ignore("Download the CC0 registry fixture for the integration check");
  var bytes=File.ReadAllBytes(path);OpenAvatarCatalog.Validate(bytes);var task=Vrm10.LoadBytesAsync(bytes,canLoadVrm0X:true,controlRigGenerationOption:ControlRigGenerationOption.None,showMeshes:true);
  while(!task.IsCompleted)yield return null;
  if(task.IsFaulted)throw task.Exception;var model=task.Result;
  try{Assert.IsNotNull(model);var animator=model.GetComponent<Animator>();Assert.IsTrue(animator.isHuman);Assert.IsNotNull(animator.GetBoneTransform(HumanBodyBones.Head));Assert.IsNotNull(animator.GetBoneTransform(HumanBodyBones.LeftHand));Assert.Greater(model.GetComponentsInChildren<Renderer>().Length,0);foreach(var renderer in model.GetComponentsInChildren<Renderer>())foreach(var material in renderer.sharedMaterials){Assert.IsNotNull(material.shader);Assert.IsTrue(material.shader.isSupported);}
   var exported=Vrm10Exporter.Export(new UniGLTF.GltfExportSettings(),model.gameObject);OpenAvatarCatalog.Validate(exported);
   File.WriteAllBytes(Path.Combine(Application.dataPath,"../Builds/avatar-fixture-v1.vrm"),exported);
   var modern=Vrm10.LoadBytesAsync(exported,canLoadVrm0X:false,controlRigGenerationOption:ControlRigGenerationOption.None);
   while(!modern.IsCompleted)yield return null;if(modern.IsFaulted)throw modern.Exception;Assert.IsTrue(modern.Result.GetComponent<Animator>().isHuman);UnityEngine.Object.DestroyImmediate(modern.Result.gameObject);
  }
  finally{if(model)UnityEngine.Object.DestroyImmediate(model.gameObject);}
 }
}
}
