using UnityEngine;
namespace StremioVR {
// Screen, halo boundary and pointer UVs share one cylindrical surface definition.
public sealed class MonitorSurface:MonoBehaviour {
 public float Curvature;
 public float Width {get;private set;} public float Height {get;private set;}
 const int Segments=48;const float Halo=.7f;
 Mesh screenMesh,haloMesh;MeshFilter screenFilter,haloFilter;Renderer haloRenderer;MaterialPropertyBlock properties=new MaterialPropertyBlock();float builtCurve=-1;
 public void Initialize(Material glow){
  screenFilter=GetComponent<MeshFilter>();var go=new GameObject("Video edge glow",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);haloFilter=go.GetComponent<MeshFilter>();haloRenderer=go.GetComponent<Renderer>();haloRenderer.sharedMaterial=glow;
 }
 public static Vector3 SurfacePoint(float width,float height,float degrees,float u,float v){
  float angle=degrees*Mathf.Deg2Rad,x=u*width,z=0;
  if(angle>.0001f){float clamped=Mathf.Clamp(u,-.5f,.5f),t=clamped*angle,radius=width/angle,extra=(u-clamped)*width;x=radius*Mathf.Sin(t)+extra*Mathf.Cos(t);z=radius*(Mathf.Cos(t)-1)-extra*Mathf.Sin(t);}
  return new Vector3(x,v*height,z);
 }
 public void Configure(float width,float height){
  if(Mathf.Abs(width-Width)<.0001f&&Mathf.Abs(height-Height)<.0001f&&Mathf.Abs(Curvature-builtCurve)<.0001f)return;
  Width=width;Height=height;builtCurve=Curvature;
  if(screenMesh)Destroy(screenMesh);if(haloMesh)Destroy(haloMesh);
  screenMesh=BuildMesh(width,height,Curvature,false);haloMesh=BuildMesh(width,height,Curvature,true);screenFilter.sharedMesh=screenMesh;haloFilter.sharedMesh=haloMesh;
  properties.SetVector("_ScreenSize",new Vector4(width,height,Halo,0));haloRenderer.SetPropertyBlock(properties);
 }
 public static Mesh BuildMesh(float width,float height,float curve,bool halo){
  var vertices=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var triangles=new System.Collections.Generic.List<int>();
  System.Action<float,float> vertex=(u,v)=>{var p=SurfacePoint(width,height,curve,u,v);if(halo)p.z+=.001f;vertices.Add(new Vector3(p.x/width,p.y/height,p.z));uv.Add(new Vector2(u+.5f,v+.5f));};
  System.Action<float,float,float,float,int> strip=(left,right,bottom,top,segments)=>{int start=vertices.Count;for(int i=0;i<=segments;i++){float u=Mathf.Lerp(left,right,(float)i/segments);vertex(u,bottom);vertex(u,top);}for(int i=0;i<segments;i++){int k=start+i*2;triangles.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});}};
  if(!halo)strip(-.5f,.5f,-.5f,.5f,Segments);
  else{
   // Matching screen subdivisions keep the inner ring exactly on the visible edge.
   float x=Halo/width,y=Halo/height;
   strip(-.5f,.5f,.5f,.5f+y,Segments);strip(-.5f,.5f,-.5f-y,-.5f,Segments);
   strip(-.5f-x,-.5f,-.5f-y,.5f+y,1);strip(.5f,.5f+x,-.5f-y,.5f+y,1);
  }
  var mesh=new Mesh{name=halo?"Monitor glow ring":"Monitor surface"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
 }
 public static bool Intersect(float width,float height,float degrees,Vector3 origin,Vector3 direction,out float distance,out Vector2 uv){
  distance=float.PositiveInfinity;uv=Vector2.zero;float angle=degrees*Mathf.Deg2Rad;
  if(angle<.0001f){if(direction.z<=.00001f)return false;float t=-origin.z/direction.z;Vector3 p=origin+direction*t;if(t<=0||Mathf.Abs(p.x)>width*.5f||Mathf.Abs(p.y)>height*.5f)return false;distance=t;uv=new Vector2(p.x/width+.5f,p.y/height+.5f);return true;}
  float radius=width/angle,oz=origin.z+radius,a=direction.x*direction.x+direction.z*direction.z,b=2*(origin.x*direction.x+oz*direction.z),c=origin.x*origin.x+oz*oz-radius*radius,disc=b*b-4*a*c;
  if(a<.000001f||disc<0)return false;
  for(int i=0;i<2;i++){float t=(-b+(i==0?-1:1)*Mathf.Sqrt(disc))/(2*a);if(t<=0||t>=distance)continue;var p=origin+direction*t;float theta=Mathf.Atan2(p.x,p.z+radius);if(Mathf.Abs(theta)>angle*.5f+.00001f||Mathf.Abs(p.y)>height*.5f)continue;var normal=new Vector3(-Mathf.Sin(theta),0,-Mathf.Cos(theta));if(Vector3.Dot(direction,normal)>=0)continue;distance=t;uv=new Vector2(theta/angle+.5f,p.y/height+.5f);}
  return !float.IsPositiveInfinity(distance);
 }
 public bool Point(Ray ray,out float distance,out Vector2 uv){
  if(Width<=0||Height<=0){distance=0;uv=Vector2.zero;return false;}
  var o=transform.InverseTransformPoint(ray.origin);var d=transform.InverseTransformVector(ray.direction);o=new Vector3(o.x*Width,o.y*Height,o.z);d=new Vector3(d.x*Width,d.y*Height,d.z);return Intersect(Width,Height,Curvature,o,d,out distance,out uv);
 }
 void OnDestroy(){if(screenMesh)Destroy(screenMesh);if(haloMesh)Destroy(haloMesh);}
}
}
