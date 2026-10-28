using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace StremioVR.Editor {
public static class R6Import {
 const string Folder = "Assets/Resources/Avatars";
 [MenuItem("Stremio VR/Prepare R6 avatar")]
 public static void Prepare() {
  var source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/r6_rigged.fbx");
  if (!source) throw new Exception("R6 source model is missing");
  var instance = UnityEngine.Object.Instantiate(source);
  var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
  var mesh = renderer.sharedMesh; var weights = mesh.boneWeights; var vertices = mesh.vertices;
  var world = new Vector3[vertices.Length]; var owners = new int[vertices.Length];
  for(int i=0;i<vertices.Length;i++) {
   var w = weights[i]; int[] indices = {w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3}; float[] values={w.weight0,w.weight1,w.weight2,w.weight3}; float best=-1;
   for(int j=0;j<4;j++) { world[i] += renderer.bones[indices[j]].localToWorldMatrix.MultiplyPoint3x4(mesh.bindposes[indices[j]].MultiplyPoint3x4(vertices[i])) * values[j]; if(values[j]>best) { best=values[j]; owners[i]=indices[j]; } }
  }
  var full = new Bounds(world[0], Vector3.zero); foreach(var v in world) full.Encapsulate(v);
  float scale=1.8f/full.size.y; Vector3 origin=new Vector3(full.center.x,full.min.y,full.center.z);
  var root=new GameObject("R6 Avatar");
  for(int bone=0;bone<renderer.bones.Length;bone++) {
   var vv=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
   for(int sub=0;sub<mesh.subMeshCount;sub++) { var tt=mesh.GetTriangles(sub); for(int t=0;t<tt.Length;t+=3) {
    int owner=owners[tt[t]]; if(owner!=bone) continue;
    for(int k=0;k<3;k++) { int index=tt[t+k]; triangles.Add(vv.Count); vv.Add((world[index]-origin)*scale); uv.Add(mesh.uv[index]); }
   }}
   if(vv.Count==0) continue;
   var bounds=new Bounds(vv[0],Vector3.zero); foreach(var v in vv) bounds.Encapsulate(v);
   for(int i=0;i<vv.Count;i++) vv[i]-=bounds.center;
   var partMesh=new Mesh { name=renderer.bones[bone].name }; partMesh.SetVertices(vv); partMesh.SetUVs(0,uv); partMesh.SetTriangles(triangles,0); partMesh.RecalculateNormals(); partMesh.RecalculateBounds();
   string path=Folder+"/R6_"+partMesh.name.Replace(" ","")+".asset";
   var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(existing) { EditorUtility.CopySerialized(partMesh,existing); UnityEngine.Object.DestroyImmediate(partMesh); partMesh=existing; } else AssetDatabase.CreateAsset(partMesh,path);
   var part=new GameObject(renderer.bones[bone].name,typeof(MeshFilter),typeof(MeshRenderer)); part.transform.SetParent(root.transform,false); part.transform.localPosition=bounds.center; part.GetComponent<MeshFilter>().sharedMesh=partMesh;
   Debug.Log("R6 PART " + part.name+" center="+bounds.center+" size="+bounds.size);
  }
  if(root.transform.childCount!=6) throw new Exception("Expected six R6 body parts");
  PrefabUtility.SaveAsPrefabAsset(root,Folder+"/R6.prefab"); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(instance); AssetDatabase.SaveAssets();
 }
}
}
