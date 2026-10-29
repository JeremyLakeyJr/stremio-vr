using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
namespace StremioVR {
public sealed class VideoAmbient : MonoBehaviour {
 public Texture Source; public VideoPlayer Video; public Transform Screen;
 public bool Enabled=true; public float Strength=0.65f;
 readonly Vector4[] positions=new Vector4[8];
 static readonly Vector2[] Points={new Vector2(-0.5f,-0.25f),new Vector2(-0.5f,0.25f),new Vector2(0.5f,-0.25f),new Vector2(0.5f,0.25f),new Vector2(-0.25f,-0.5f),new Vector2(0.25f,-0.5f),new Vector2(-0.25f,0.5f),new Vector2(0.25f,0.5f)};
 RenderTexture current,next; Material filter; float dim;
 void Awake() {
  filter=new Material(Shader.Find("MovieNight/EdgeSmooth"));
  current=new RenderTexture(8,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear); next=new RenderTexture(8,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
  current.Create();next.Create(); BlitPreservingTarget(Texture2D.blackTexture,current); BlitPreservingTarget(Texture2D.blackTexture,next); Shader.SetGlobalTexture("_CinemaEdgeColors",current);
 }
 // Blit changes the active framebuffer. Never leave our 8x1 target bound for XR rendering.
 public static void BlitPreservingTarget(Texture source,RenderTexture target,Material material=null) {
  var previous=RenderTexture.active; bool srgb=GL.sRGBWrite;
  try { GL.sRGBWrite=false; if(material) Graphics.Blit(source,target,material); else Graphics.Blit(source,target); }
  finally { RenderTexture.active=previous; GL.sRGBWrite=srgb; }
 }
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
  Texture source=Source?Source:(Video && Video.isPrepared?Video.texture:null); bool available=Enabled && source && Screen;
  float blend=1-Mathf.Exp(-8*Mathf.Min(Time.unscaledDeltaTime,0.1f));
  filter.SetTexture("_History",current); filter.SetFloat("_Blend",blend); filter.SetFloat("_Available",available?1:0);
  BlitPreservingTarget(source?source:Texture2D.blackTexture,next,filter); var old=current;current=next;next=old;
  Shader.SetGlobalTexture("_CinemaEdgeColors",current);
  dim=Mathf.Lerp(dim,available?1:0,blend); Shader.SetGlobalFloat("_CinemaAmbientDim",dim);
  if(Screen) for(int i=0;i<8;i++) positions[i]=Screen.TransformPoint(new Vector3(Points[i].x,Points[i].y,-0.15f));
  Shader.SetGlobalVectorArray("_CinemaBouncePositions",positions); Shader.SetGlobalFloat("_CinemaBounceStrength",Strength);
 }
 void OnDestroy() {
  Shader.SetGlobalTexture("_CinemaEdgeColors",Texture2D.blackTexture); Shader.SetGlobalFloat("_CinemaBounceStrength",0);Shader.SetGlobalFloat("_CinemaAmbientDim",0);
  if(current){current.Release();Destroy(current);} if(next){next.Release();Destroy(next);} if(filter)Destroy(filter);
 }
}
}
