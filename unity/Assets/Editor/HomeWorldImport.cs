using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StremioVR.Editor {
public static class HomeWorldImport {
 public const string ScenePath="Assets/PrivateHome/Home Ulkan/Customizable Home.unity";
 public static void Inspect() {
  var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
  var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
  Debug.Log("HOME renderers="+renderers.Length);
  foreach(var r in renderers.Where(r=>r.name.ToLower().Contains("screen")||r.name.ToLower().Contains("couch")||r.name.ToLower().Contains("floor")||r.name.ToLower().Contains("sofa"))) Debug.Log("HOME OBJECT "+r.name+" active="+r.gameObject.activeInHierarchy+" bounds="+r.bounds+" forward="+r.transform.forward);
  foreach(var r in renderers.Where(r=>r.gameObject.activeInHierarchy && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).OrderByDescending(r=>r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount??0).Take(15)) Debug.Log("HOME COMPLEX "+r.name+" vertices="+r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount);
  EditorSceneManager.CloseScene(scene,true);
 }
}
}
