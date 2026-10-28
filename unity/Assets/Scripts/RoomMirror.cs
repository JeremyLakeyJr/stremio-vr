using UnityEngine;
namespace StremioVR {
public sealed class RoomMirror : MonoBehaviour {
 public bool Visible=true;
 Camera reflection; RenderTexture left,right; Renderer surface; Material material; float nextFrame;
 void Awake() {
  gameObject.layer=28; surface=GetComponent<Renderer>(); material=new Material(Shader.Find("MovieNight/Mirror")); surface.sharedMaterial=material;
  left=new RenderTexture(1024,1024,24); right=new RenderTexture(1024,1024,24); left.Create(); right.Create(); material.mainTexture=left; material.SetTexture("_RightTex",right);
  reflection=new GameObject("Mirror camera",typeof(Camera)).GetComponent<Camera>(); reflection.enabled=false; reflection.stereoTargetEye=StereoTargetEyeMask.None;
 }
 void LateUpdate() {
  surface.enabled=Visible; var viewer=Camera.main;
  if(!Visible || !viewer || Time.unscaledTime<nextFrame || !surface.isVisible || Vector3.Distance(viewer.transform.position,transform.position)>12) return;
  nextFrame=Time.unscaledTime+1f/24;
  Render(viewer,Camera.StereoscopicEye.Left,left); if(viewer.stereoEnabled) Render(viewer,Camera.StereoscopicEye.Right,right); else Graphics.Blit(left,right);
 }
 void Render(Camera viewer,Camera.StereoscopicEye eye,RenderTexture texture) {
  reflection.CopyFrom(viewer); reflection.enabled=false; reflection.stereoTargetEye=StereoTargetEyeMask.None; reflection.targetTexture=texture; reflection.cullingMask=viewer.cullingMask & ~(1<<28); reflection.cullingMask|=1<<29;
  Vector3 normal=transform.forward; if(Vector3.Dot(normal,viewer.transform.position-transform.position)<0) normal=-normal;
  Vector4 plane=new Vector4(normal.x,normal.y,normal.z,-Vector3.Dot(normal,transform.position));
  var reflect=Reflection(plane); var view=viewer.stereoEnabled?viewer.GetStereoViewMatrix(eye):viewer.worldToCameraMatrix;
  reflection.worldToCameraMatrix=view*reflect; reflection.projectionMatrix=viewer.stereoEnabled?viewer.GetStereoProjectionMatrix(eye):viewer.projectionMatrix;
  var position=reflection.worldToCameraMatrix.MultiplyPoint(transform.position+normal*0.015f); var cameraNormal=reflection.worldToCameraMatrix.MultiplyVector(normal).normalized;
  reflection.projectionMatrix=reflection.CalculateObliqueMatrix(new Vector4(cameraNormal.x,cameraNormal.y,cameraNormal.z,-Vector3.Dot(position,cameraNormal)));
  reflection.transform.position=reflect.MultiplyPoint(viewer.transform.position);
  bool old=GL.invertCulling; try { GL.invertCulling=!old; reflection.Render(); } finally { GL.invertCulling=old; }
 }
 public static Matrix4x4 Reflection(Vector4 p) {
  var m=Matrix4x4.identity; for(int r=0;r<3;r++) for(int c=0;c<4;c++) m[r,c]-=2*p[r]*p[c]; return m;
 }
 void OnDestroy() { if(reflection) Destroy(reflection.gameObject); if(left) {left.Release();Destroy(left);} if(right) {right.Release();Destroy(right);} if(material) Destroy(material); }
}
}
