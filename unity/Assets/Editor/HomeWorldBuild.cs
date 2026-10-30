using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StremioVR.Editor {
public static class HomeWorldBuild {
 const string Folder="Assets/Resources/Worlds";
 public static void Prepare() {
  if(!File.Exists(HomeWorldImport.ScenePath)) { Debug.Log("Home source is not installed; retaining existing home prefab if present."); return; }
  var scene=EditorSceneManager.OpenScene(HomeWorldImport.ScenePath,OpenSceneMode.Additive);
  var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(false)).Where(r=>r.enabled && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh && !r.GetComponentInParent<Canvas>()).ToArray();
  var screen=renderers.First(r=>r.name.Contains("VideoScreen"));
  var couch=renderers.First(r=>r.name=="Couches_Curved_Couch_Red");
  var origin=couch.bounds.center; origin.y=4.2f; Vector3 forward=screen.bounds.center-origin; forward.y=0;
  var rotation=Quaternion.FromToRotation(forward.normalized,Vector3.forward); var transform=Matrix4x4.TRS(-(rotation*origin),rotation,Vector3.one);
  var root=new GameObject("Open Concept Home"); var data=root.AddComponent<CinemaWorldData>();
  data.ScreenPosition=transform.MultiplyPoint3x4(screen.bounds.center)-Vector3.forward*0.03f; data.ScreenSize=new Vector2(4.34f,2.44f); data.Spawn=new Vector3(3.2f,0.15f,-1.1f);
  data.Seats=Enumerable.Range(0,6).Select(i=>new Vector3((i-2.5f)*0.73f,1.42f,Mathf.Abs(i-2.5f)*0.12f)).ToArray();
  var bake=JObject.Parse(File.ReadAllText("Assets/Editor/HomeBake.json"));
  var lightmap=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PrivateHome/BakeryLightmaps/Customizable Home_LM0_final.hdr");
  var skySource=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("e4b014022a99bc941b5594a6907bc2f4"));
  if(skySource) { data.Skybox=skySource; }
  var groups=new Dictionary<(Material material,bool baked),List<CombineInstance>>();
  var temporaryMeshes=new List<Mesh>(); int bakedCount=0;
  foreach(var r in renderers) {
   if(r==screen || r.name.IndexOf("mirror",StringComparison.OrdinalIgnoreCase)>=0) continue;
   var mesh=r.GetComponent<MeshFilter>().sharedMesh;
   var id=GlobalObjectId.GetGlobalObjectIdSlow(r); JArray uv=bake[id.targetObjectId.ToString()] as JArray;
   // Prefab instance renderer IDs are stored in scene YAML as stripped objects.
   if(uv==null) {
    var so=new SerializedObject(r); typeof(SerializedObject).GetProperty("inspectorMode",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance)?.SetValue(so,InspectorMode.Debug);
    var local=so.FindProperty("m_LocalIdentfierInFile"); if(local!=null) uv=bake[local.longValue.ToString()] as JArray;
   }
   bool baked=uv!=null && lightmap;
   if(baked) { var copy=UnityEngine.Object.Instantiate(mesh); var coords=copy.uv2.Length==copy.vertexCount?copy.uv2:copy.uv; for(int v=0;v<coords.Length;v++) coords[v]=new Vector2(coords[v].x*(float)uv[0]+(float)uv[2],coords[v].y*(float)uv[1]+(float)uv[3]); copy.uv2=coords; mesh=copy; temporaryMeshes.Add(copy); bakedCount++; }
   for(int sub=0;sub<mesh.subMeshCount && sub<r.sharedMaterials.Length;sub++) {
    var material=r.sharedMaterials[sub]; if(!material) continue;
    var key=(material,baked); if(!groups.ContainsKey(key)) groups[key]=new List<CombineInstance>();
    groups[key].Add(new CombineInstance { mesh=mesh,subMeshIndex=sub,transform=transform*r.transform.localToWorldMatrix });
   }
  }
  int index=0, vertices=0;
  foreach(var group in groups) {
   var mesh=new Mesh { name=group.Key.material.name,indexFormat=IndexFormat.UInt32 }; mesh.CombineMeshes(group.Value.ToArray(),true,true); vertices+=mesh.vertexCount;
   string path=Folder+"/HomeMesh"+index+".asset"; var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(old) { EditorUtility.CopySerialized(mesh,old); UnityEngine.Object.DestroyImmediate(mesh); mesh=old; } else AssetDatabase.CreateAsset(mesh,path);
   string matPath=Folder+"/HomeMaterial"+index+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
   if(!material) { material=new Material(Shader.Find("StremioVR/World")); AssetDatabase.CreateAsset(material,matPath); }
   material.shader=Shader.Find("StremioVR/World");
   material.mainTexture=null;material.mainTextureScale=Vector2.one;material.mainTextureOffset=Vector2.zero;
   material.SetTexture("_EmissionMap",null);material.SetTexture("_BumpMap",null);material.SetColor("_EmissionColor",Color.black);
   material.SetFloat("_Glossiness",0.25f);material.SetFloat("_Metallic",0);material.SetFloat("_BumpScale",1);
   material.SetTexture("_BakedLight",group.Key.baked?lightmap:null); material.SetFloat("_HasBakedLight",group.Key.baked?1:0);
   var source=new SerializedObject(group.Key.material); var colors=source.FindProperty("m_SavedProperties.m_Colors"); material.color=Color.white;
   for(int i=0;i<colors.arraySize;i++) { var entry=colors.GetArrayElementAtIndex(i); if(entry.FindPropertyRelative("first").stringValue=="_Color") material.color=entry.FindPropertyRelative("second").colorValue; else if(entry.FindPropertyRelative("first").stringValue=="_EmissionColor") material.SetColor("_EmissionColor",entry.FindPropertyRelative("second").colorValue); }
   var textures=source.FindProperty("m_SavedProperties.m_TexEnvs");
   for(int i=0;i<textures.arraySize;i++) {
    var entry=textures.GetArrayElementAtIndex(i); string property=entry.FindPropertyRelative("first").stringValue;
    if(property=="_EmissionMap") { material.SetTexture(property,entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture); continue; }
    if(property=="_BumpMap") { material.SetTexture("_BumpMap",entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture); continue; }
    if(property!="_MainTex") continue;
    var tex=entry.FindPropertyRelative("second"); material.mainTexture=tex.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
    material.mainTextureScale=tex.FindPropertyRelative("m_Scale").vector2Value; material.mainTextureOffset=tex.FindPropertyRelative("m_Offset").vector2Value;
   }
   var floats=source.FindProperty("m_SavedProperties.m_Floats"); for(int i=0;i<floats.arraySize;i++) { var entry=floats.GetArrayElementAtIndex(i); string name=entry.FindPropertyRelative("first").stringValue; if(name=="_Glossiness"||name=="_BumpScale"||name=="_Metallic") material.SetFloat(name,entry.FindPropertyRelative("second").floatValue); }
   // Saved emission colors can remain white while Standard emission is disabled.
   // In particular Cyberpunk_4 (the downstairs floor) must not glow solid white.
   bool lightBar=group.Key.material.name.IndexOf("Emmissive",StringComparison.OrdinalIgnoreCase)>=0;
   if(!lightBar && !group.Key.material.IsKeywordEnabled("_EMISSION"))material.SetColor("_EmissionColor",Color.black);
   material.SetFloat("_VideoReactive",lightBar?1:0);
   EditorUtility.SetDirty(material);
   var part=new GameObject(group.Key.material.name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider)); part.transform.SetParent(root.transform,false); part.GetComponent<MeshFilter>().sharedMesh=mesh; part.GetComponent<MeshRenderer>().sharedMaterial=material;
   if(mesh.vertexCount<500000) part.GetComponent<MeshCollider>().sharedMesh=mesh;
   index++;
  }
  foreach(var temporary in temporaryMeshes) UnityEngine.Object.DestroyImmediate(temporary);
  Debug.Log("Home baked lighting restored on "+bakedCount+" renderers");
  PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Home.prefab"); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.CloseScene(scene,true); AssetDatabase.SaveAssets();
  Debug.Log("HOME READY batches="+index+" vertices="+vertices);
 }
}
public sealed class HomeAssetSettings : AssetPostprocessor {
 void OnPreprocessTexture() {
  if(!assetPath.StartsWith("Assets/PrivateHome/")) return;
  var importer=(TextureImporter)assetImporter; importer.maxTextureSize=4096; importer.textureCompression=TextureImporterCompression.Compressed;
  var android=importer.GetPlatformTextureSettings("Android"); android.overridden=true; android.maxTextureSize=4096; android.format=TextureImporterFormat.ASTC_4x4; if(assetPath.EndsWith("_final.hdr")) { importer.textureType=TextureImporterType.Default; importer.sRGBTexture=false; android.format=TextureImporterFormat.RGB9E5; }
  importer.SetPlatformTextureSettings(android);
 }
}
}
