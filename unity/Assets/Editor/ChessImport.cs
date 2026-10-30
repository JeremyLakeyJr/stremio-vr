using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace StremioVR.Editor {
// Format conversion only: retain every source vertex and triangle, with no decimation.
public static class ChessImport {
 public static void Prepare(){
  const string folder="Assets/Resources/Chess";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  foreach(string name in new[]{"Pawn","Rook","Knight","Bishop","Queen","King"}){
   if(name=="Pawn"){ImportPawn(folder);continue;}
   string path=Path.GetFullPath("../assets/chess/"+name+".3mf");
   using(var archive=ZipFile.OpenRead(path))using(var stream=archive.GetEntry("3D/3dmodel.model").Open()){
    var doc=XDocument.Load(stream);XNamespace ns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    var vertices=new List<Vector3>();var indices=new List<int>();float bodyTop=0;
    foreach(var obj in doc.Root.Element(ns+"resources").Elements(ns+"object")){
     var mesh=obj.Element(ns+"mesh");if(mesh==null)continue;int offset=vertices.Count;
     var points=mesh.Element(ns+"vertices").Elements(ns+"vertex").Select(v=>new Vector3(Number(v,"x"),Number(v,"z"),Number(v,"y"))).ToArray();
     // The king is supplied in two printable parts. Seat the original cross in its slot.
     if(offset>0 && name=="King"){float bottom=points.Min(v=>v.y);for(int i=0;i<points.Length;i++)points[i].y+=bodyTop-bottom-4f;}
     else bodyTop=points.Max(v=>v.y);
     vertices.AddRange(points);
     foreach(var t in mesh.Element(ns+"triangles").Elements(ns+"triangle")){indices.Add(offset+(int)t.Attribute("v1"));indices.Add(offset+(int)t.Attribute("v3"));indices.Add(offset+(int)t.Attribute("v2"));}
    }
    SaveMesh(folder,name,vertices,indices);
   }
  }
  AssetDatabase.SaveAssets();
 }
 static void ImportPawn(string folder){
  // The author's Pawn.3mf duplicates Rook.3mf; use the original, correctly shaped STL.
  var vertices=new List<Vector3>();var indices=new List<int>();var lookup=new Dictionary<Vector3,int>();
  using(var reader=new BinaryReader(File.OpenRead(Path.GetFullPath("../assets/chess/Pawn.stl")))){
   reader.ReadBytes(80);uint count=reader.ReadUInt32();
   for(uint face=0;face<count;face++){
    reader.ReadBytes(12);var triangle=new int[3];
    for(int j=0;j<3;j++){float x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle();var v=new Vector3(x,z,y);if(!lookup.TryGetValue(v,out int index)){index=vertices.Count;vertices.Add(v);lookup.Add(v,index);}triangle[j]=index;}
    indices.Add(triangle[0]);indices.Add(triangle[2]);indices.Add(triangle[1]);reader.ReadUInt16();
   }
  }
  SaveMesh(folder,"Pawn",vertices,indices);
 }
 static void SaveMesh(string folder,string name,List<Vector3> vertices,List<int> indices){
    // Millimetres to table display scale, coordinate handedness and bottom-centred origin.
    float min=vertices.Min(v=>v.y);for(int i=0;i<vertices.Count;i++){var v=vertices[i];v.y-=min;vertices[i]=v*0.003f;}
    var result=new Mesh{name="Jacob G — "+name,indexFormat=IndexFormat.UInt32};result.SetVertices(vertices);result.SetTriangles(indices,0);result.RecalculateNormals();result.RecalculateBounds();
    string target=folder+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(target);if(old){EditorUtility.CopySerialized(result,old);UnityEngine.Object.DestroyImmediate(result);}else AssetDatabase.CreateAsset(result,target);
    Debug.Log("CHESS "+name+" vertices="+vertices.Count+" triangles="+indices.Count/3);
 }
 static float Number(XElement e,string key)=>float.Parse((string)e.Attribute(key),CultureInfo.InvariantCulture);
}
}
