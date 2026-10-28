using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
namespace StremioVR {
public sealed class VideoAmbient : MonoBehaviour {
 public Texture Source; public VideoPlayer Video; public Transform Screen;
 public bool Enabled=true; public float Strength=0.65f;
 readonly Color[] target=new Color[8], smooth=new Color[8]; readonly Vector4[] colors=new Vector4[8], positions=new Vector4[8];
 RenderTexture thumbnail; Texture2D fallback; bool pending, forceFallback; float nextSample, nextDiagnostic, dim;
 void Awake() { thumbnail=new RenderTexture(32,18,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); thumbnail.Create(); }
 public static Color[] SampleEdges(Color32[] pixels,int width,int height) {
  var result=new Color[8]; var counts=new int[8];
  for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
   int index=-1;
   if(x<2) index=y<height/2?0:1; else if(x>=width-2) index=y<height/2?2:3;
   else if(y<2) index=x<width/2?4:5; else if(y>=height-2) index=x<width/2?6:7;
   if(index>=0) { result[index]+=(Color)pixels[y*width+x]; counts[index]++; }
  }
  for(int i=0;i<8;i++) if(counts[i]>0) result[i]/=counts[i]; return result;
 }
 void Update() {
  Texture source=Source?Source:(Video && Video.isPrepared?Video.texture:null);
  bool available=Enabled && source && Screen;
  if(available && !pending && Time.unscaledTime>=nextSample) {
   nextSample=Time.unscaledTime+0.12f; Graphics.Blit(source,thumbnail);
   if(SystemInfo.supportsAsyncGPUReadback && !forceFallback) {
    pending=true; AsyncGPUReadback.Request(thumbnail,0,TextureFormat.RGBA32,result=> {
     pending=false; if(!this) return; if(result.hasError) { forceFallback=true; return; } var data=result.GetData<Color32>(); var sampled=SampleEdges(data.ToArray(),32,18); for(int i=0;i<8;i++) target[i]=sampled[i];
    });
   } else {
    if(!fallback) fallback=new Texture2D(32,18,TextureFormat.RGBA32,false,true);
    var previous=RenderTexture.active; RenderTexture.active=thumbnail; fallback.ReadPixels(new Rect(0,0,32,18),0,0,false); RenderTexture.active=previous;
    var sampled=SampleEdges(fallback.GetPixels32(),32,18); for(int i=0;i<8;i++) target[i]=sampled[i];
   }
  }
  float blend=1-Mathf.Exp(-4*Time.unscaledDeltaTime);
  dim=Mathf.Lerp(dim,available?1:0,blend); Shader.SetGlobalFloat("_CinemaAmbientDim",dim);
  if(Debug.isDebugBuild && available && Time.unscaledTime>=nextDiagnostic) { nextDiagnostic=Time.unscaledTime+10; Debug.Log("[Ambient] edge sampling active; left="+target[0]+" right="+target[2]+" strength="+Strength); }
  for(int i=0;i<8;i++) { smooth[i]=Color.Lerp(smooth[i],available?target[i]:Color.black,blend); colors[i]=smooth[i]; }
  if(Screen) {
   Vector2[] points={new Vector2(-0.5f,-0.25f),new Vector2(-0.5f,0.25f),new Vector2(0.5f,-0.25f),new Vector2(0.5f,0.25f),new Vector2(-0.25f,-0.5f),new Vector2(0.25f,-0.5f),new Vector2(-0.25f,0.5f),new Vector2(0.25f,0.5f)};
   for(int i=0;i<8;i++) positions[i]=Screen.TransformPoint(new Vector3(points[i].x,points[i].y,-0.15f));
  }
  Shader.SetGlobalVectorArray("_CinemaBounceColors",colors); Shader.SetGlobalVectorArray("_CinemaBouncePositions",positions); Shader.SetGlobalFloat("_CinemaBounceStrength",Strength);
 }
 void OnDestroy() { Shader.SetGlobalFloat("_CinemaBounceStrength",0); Shader.SetGlobalFloat("_CinemaAmbientDim",0); if(thumbnail) { thumbnail.Release(); Destroy(thumbnail); } if(fallback) Destroy(fallback); }
}
}
