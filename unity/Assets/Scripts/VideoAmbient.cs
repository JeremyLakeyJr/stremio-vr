using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
namespace StremioVR {
public sealed class VideoAmbient : MonoBehaviour {
 public WorldManager World; public Texture Source; public VideoPlayer Video; public Transform Screen;
 public bool Enabled=true; public float Strength=0.65f;
 public const int SegmentsPerEdge=8,SampleCount=32;
 public const int MaxScreens=7;
 readonly Vector4[] centers=new Vector4[MaxScreens],rights=new Vector4[MaxScreens],ups=new Vector4[MaxScreens],fronts=new Vector4[MaxScreens];
 static Vector2 Point(int index){int edge=index/SegmentsPerEdge;float t=(index%SegmentsPerEdge+0.5f)/SegmentsPerEdge-0.5f;return edge<2?new Vector2(edge==0?-0.5f:0.5f,t):new Vector2(t,edge==2?-0.5f:0.5f);}
 RenderTexture current,next,bounceField; Material filter,fieldFilter; float dim;
 void Awake() {
  filter=new Material(Shader.Find("MovieNight/EdgeSmooth"));fieldFilter=new Material(Shader.Find("MovieNight/BounceField"));
  bounceField=new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);bounceField.wrapMode=TextureWrapMode.Clamp;bounceField.filterMode=FilterMode.Bilinear;bounceField.Create();
  current=new RenderTexture(SampleCount,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear); next=new RenderTexture(SampleCount,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
  current.filterMode=next.filterMode=FilterMode.Bilinear;current.wrapMode=next.wrapMode=TextureWrapMode.Clamp;current.Create();next.Create(); BlitPreservingTarget(Texture2D.blackTexture,current); BlitPreservingTarget(Texture2D.blackTexture,next); Shader.SetGlobalTexture("_CinemaEdgeColors",current);
 }
 // Blit changes the active framebuffer. Never leave our 32x1 target bound for XR rendering.
 public static void BlitPreservingTarget(Texture source,RenderTexture target,Material material=null) {
  var previous=RenderTexture.active; bool srgb=GL.sRGBWrite;
  try { GL.sRGBWrite=false; if(material) Graphics.Blit(source,target,material); else Graphics.Blit(source,target); }
  finally { RenderTexture.active=previous; GL.sRGBWrite=srgb; }
 }
 public static Color[] SampleEdges(Color32[] pixels,int width,int height) {
  var result=new Color[SampleCount]; var counts=new int[SampleCount];
  for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
   int index=-1;
   if(x<2) index=Mathf.Min(7,y*8/height); else if(x>=width-2) index=8+Mathf.Min(7,y*8/height);
   else if(y<2) index=16+Mathf.Min(7,x*8/width); else if(y>=height-2) index=24+Mathf.Min(7,x*8/width);
   if(index>=0) { result[index]+=(Color)pixels[y*width+x]; counts[index]++; }
  }
  for(int i=0;i<SampleCount;i++) if(counts[i]>0) result[i]/=counts[i]; return result;
 }
 void Update() {
  Texture source=Source?Source:(Video && Video.isPrepared?Video.texture:null); bool available=Enabled && source && Screen;
  float blend=1-Mathf.Exp(-8*Mathf.Min(Time.unscaledDeltaTime,0.1f));
  var display=Screen?Screen.GetComponent<Renderer>():null;var scale=display?display.sharedMaterial.mainTextureScale:Vector2.one;var offset=display?display.sharedMaterial.mainTextureOffset:Vector2.zero;
  filter.SetVector("_SourceScaleOffset",new Vector4(scale.x,scale.y,offset.x,offset.y));
  filter.SetTexture("_History",current); filter.SetFloat("_Blend",blend); filter.SetFloat("_Available",available?1:0);
  BlitPreservingTarget(source?source:Texture2D.blackTexture,next,filter); var old=current;current=next;next=old;
  Shader.SetGlobalTexture("_CinemaEdgeColors",current);
  BlitPreservingTarget(current,bounceField,fieldFilter);Shader.SetGlobalTexture("_CinemaBounceField",bounceField);
  dim=Mathf.Lerp(dim,available?1:0,blend); Shader.SetGlobalFloat("_CinemaAmbientDim",dim);
  int count=World?Mathf.Min(MaxScreens,World.Screens.Count):(Screen?1:0);
  for(int i=0;i<count;i++){
   var t=World?World.Screens[i].transform:Screen;Vector3 right=t.TransformVector(Vector3.right),up=t.TransformVector(Vector3.up),front=-t.forward;
   centers[i]=t.position;rights[i]=new Vector4(right.x,right.y,right.z,1/Mathf.Max(.001f,right.sqrMagnitude));ups[i]=new Vector4(up.x,up.y,up.z,1/Mathf.Max(.001f,up.sqrMagnitude));fronts[i]=front;
  }
  Shader.SetGlobalInt("_CinemaScreenCount",count);Shader.SetGlobalVectorArray("_CinemaScreenCenters",centers);Shader.SetGlobalVectorArray("_CinemaScreenRights",rights);Shader.SetGlobalVectorArray("_CinemaScreenUps",ups);Shader.SetGlobalVectorArray("_CinemaScreenFronts",fronts);Shader.SetGlobalFloat("_CinemaBounceStrength",Strength);
 }
 void OnDestroy() {
  Shader.SetGlobalInt("_CinemaScreenCount",0);Shader.SetGlobalTexture("_CinemaEdgeColors",Texture2D.blackTexture); Shader.SetGlobalFloat("_CinemaBounceStrength",0);Shader.SetGlobalFloat("_CinemaAmbientDim",0);
  Shader.SetGlobalTexture("_CinemaBounceField",Texture2D.blackTexture);if(bounceField){bounceField.Release();Destroy(bounceField);}if(fieldFilter)Destroy(fieldFilter);
  if(current){current.Release();Destroy(current);} if(next){next.Release();Destroy(next);} if(filter)Destroy(filter);
 }
}
}
