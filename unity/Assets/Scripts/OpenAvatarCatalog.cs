using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
namespace StremioVR {
public static class OpenAvatarCatalog {
 public const int MaxBytes=32*1024*1024;
 static readonly System.Threading.SemaphoreSlim transfers=new System.Threading.SemaphoreSlim(2,2);
 static Dictionary<string,JObject> entries;
 static readonly Dictionary<string,Task<byte[]>> downloads=new Dictionary<string,Task<byte[]>>();
 public static IEnumerable<JObject> Entries { get { Ensure();return entries.Values; } }
 static void Ensure(){if(entries!=null)return;var data=Resources.Load<TextAsset>("Avatars/OpenSourceCatalog");entries=((JArray)JObject.Parse(data.text)["entries"]).Cast<JObject>().ToDictionary(a=>(string)a["id"]);}
 public static JObject Find(string id){Ensure();return id!=null&&entries.TryGetValue(id,out var entry)?entry:null;}
 public static string Credit(JObject entry)=>entry==null?"R6":$"{entry["name"]} — {entry["creator"]} · {entry["license"]}";
 public static string CachePath(string id){using(var hash=System.Security.Cryptography.SHA256.Create()){string key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-","").ToLowerInvariant();return Path.Combine(Application.persistentDataPath,"avatars",key+".vrm");}}
 public static Task<byte[]> Download(string id){if(downloads.TryGetValue(id,out var pending))return pending;var task=Fetch(id);downloads[id]=task;return RemoveWhenDone(id,task);}
 static async Task<byte[]> RemoveWhenDone(string id,Task<byte[]> task){try{return await task;}finally{downloads.Remove(id);}}
 static async Task<byte[]> Fetch(string id){await transfers.WaitAsync();try{return await FetchBounded(id);}finally{transfers.Release();}}
 static async Task<byte[]> FetchBounded(string id){
  var entry=Find(id)??throw new Exception("Avatar is not in this version's catalog");string path=CachePath(id);
  if(File.Exists(path)){try{if(new FileInfo(path).Length<=MaxBytes){var cached=File.ReadAllBytes(path);Validate(cached);File.SetLastWriteTimeUtc(path,DateTime.UtcNow);return cached;}}catch{}File.Delete(path);}
  using(var request=UnityWebRequest.Get((string)entry["url"])){
   request.timeout=60;request.redirectLimit=4;var op=request.SendWebRequest();
   while(!op.isDone){if(request.downloadedBytes>MaxBytes){request.Abort();throw new Exception("Avatar exceeds the 32 MB Quest download limit");}await Task.Yield();}
   if(request.result!=UnityWebRequest.Result.Success)throw new Exception("Avatar download failed; check your connection or try another avatar");
   var bytes=request.downloadHandler.data;Validate(bytes);
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   // Bound the disk cache; loaded instances do not depend on the cached file.
   var files=new DirectoryInfo(Path.GetDirectoryName(path)).GetFiles("*.vrm").OrderBy(f=>f.LastWriteTimeUtc).ToArray();long total=files.Sum(f=>f.Length);
   foreach(var file in files){if(total+bytes.Length<=256L*1024*1024)break;total-=file.Length;file.Delete();}
   File.WriteAllBytes(path,bytes);return bytes;
  }
 }
 public static void Validate(byte[] bytes){
  if(bytes==null||bytes.Length<20||bytes.Length>MaxBytes||BitConverter.ToUInt32(bytes,0)!=0x46546c67||BitConverter.ToUInt32(bytes,4)!=2||BitConverter.ToUInt32(bytes,8)!=bytes.Length||BitConverter.ToUInt32(bytes,16)!=0x4e4f534a)throw new Exception("Invalid or oversized VRM file");
  uint length=BitConverter.ToUInt32(bytes,12);if(length>bytes.Length-20||length>4*1024*1024)throw new Exception("Invalid avatar metadata");
  var json=JObject.Parse(Encoding.UTF8.GetString(bytes,20,(int)length));
  if(json["extensions"]?["VRM"]==null&&json["extensions"]?["VRMC_vrm"]==null)throw new Exception("This model is not a VRM avatar");
  foreach(string key in new[]{"buffers","images"})foreach(var item in json[key] as JArray??new JArray())if(item["uri"]!=null)throw new Exception("Avatars must contain their own textures and buffers");
  long vertices=0;int primitives=0;var accessors=json["accessors"] as JArray??new JArray();
  foreach(var mesh in json["meshes"] as JArray??new JArray())foreach(var part in mesh["primitives"] as JArray??new JArray()){int index=(int?)part["attributes"]?["POSITION"]??-1;if(index<0||index>=accessors.Count)throw new Exception("Invalid avatar mesh");vertices+=(long?)accessors[index]["count"]??0;primitives++;}
  if(vertices>120000||primitives>64||(json["images"] as JArray)?.Count>16||(json["nodes"] as JArray)?.Count>512)throw new Exception("This avatar exceeds the Quest complexity limit; choose a simpler avatar");
 }
}
}
