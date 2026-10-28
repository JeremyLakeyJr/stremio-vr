using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace StremioVR.Editor {
public static class CinemaWorldImport {
 const string Folder="Assets/Resources/Worlds";
 [MenuItem("Stremio VR/Prepare cinema world")]
 public static void Prepare() {
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldSources/Cinema.fbx");
  var original=UnityEngine.Object.Instantiate(source); var renderers=original.GetComponentsInChildren<MeshRenderer>();
  foreach(var r in renderers.Where(r=>r.name.Contains("018-cinema-screen") || r.name.Contains("seat-1"))) Debug.Log("WORLD NODE "+r.name+" "+r.bounds);
  var seats=renderers.Where(r=>r.name.Contains("/seat-")).OrderBy(r=>r.name,StringComparer.Ordinal).ToArray();
  var screen=renderers.Single(r=>r.name=="018-cinema-screen-and-masking/2");
  Vector3 center=seats.Aggregate(Vector3.zero,(sum,r)=>sum+r.bounds.center)/seats.Length; center.y=0;
  Vector3 forward=screen.bounds.center-center; forward.y=0;
  var rotation=Quaternion.FromToRotation(forward.normalized,Vector3.forward);
  var alignment=new GameObject("Alignment"); original.transform.SetParent(alignment.transform,true); alignment.transform.rotation=rotation; alignment.transform.position=-(rotation*center);
  var root=new GameObject("Cinema multiplex"); var data=root.AddComponent<CinemaWorldData>();
  data.Seats=seats.Select(r=>new Vector3(r.bounds.center.x,r.bounds.max.y+0.88f,r.bounds.center.z)).ToArray();
  data.ScreenPosition=screen.bounds.center-Vector3.forward*0.025f; data.ScreenSize=new Vector2(screen.bounds.size.x,screen.bounds.size.y);
  data.Spawn=new Vector3(3.8f,0.05f,-2.0f);
  var groups=new Dictionary<string,List<CombineInstance>>(); var colors=new Dictionary<string,Color>();
  foreach(var r in renderers) {
   if(r==screen) continue;
   var mesh=r.GetComponent<MeshFilter>().sharedMesh;
   for(int sub=0;sub<mesh.subMeshCount;sub++) {
    var material=r.sharedMaterials[Math.Min(sub,r.sharedMaterials.Length-1)]; string key=material?material.name:"Default";
    if(!groups.ContainsKey(key)) { groups[key]=new List<CombineInstance>(); colors[key]=material?material.color:Color.gray; }
    groups[key].Add(new CombineInstance { mesh=mesh,subMeshIndex=sub,transform=r.transform.localToWorldMatrix });
   }
  }
  int index=0;
  foreach(var group in groups) {
   var mesh=new Mesh { name=group.Key,indexFormat=IndexFormat.UInt32 }; mesh.CombineMeshes(group.Value.ToArray(),true,true);
   string path=Folder+"/CinemaMesh"+index+".asset"; var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(old) { EditorUtility.CopySerialized(mesh,old); UnityEngine.Object.DestroyImmediate(mesh); mesh=old; } else AssetDatabase.CreateAsset(mesh,path);
   string materialPath=Folder+"/CinemaMaterial"+index+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
   if(!material) { material=new Material(Shader.Find("StremioVR/World")); AssetDatabase.CreateAsset(material,materialPath); }
   material.shader=Shader.Find("StremioVR/World"); material.color=colors[group.Key]; EditorUtility.SetDirty(material);
   var part=new GameObject(group.Key,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider)); part.transform.SetParent(root.transform,false);
   part.GetComponent<MeshFilter>().sharedMesh=mesh; part.GetComponent<MeshRenderer>().sharedMaterial=material; part.GetComponent<MeshCollider>().sharedMesh=mesh; index++;
  }
  PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Cinema.prefab");
  Debug.Log("WORLD READY seats="+data.Seats.Length+" screen="+data.ScreenPosition+" size="+data.ScreenSize+" material batches="+index);
  UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(alignment); AssetDatabase.SaveAssets();
 }
}
}
