using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace StremioVR {
public sealed class AvatarPreview : MonoBehaviour {
 GameObject stage; RenderTexture texture; R6Avatar avatar;
 public void Initialize(JObject profile) {
  stage=new GameObject("Avatar preview stage"); stage.transform.position=new Vector3(100,0,100);
  avatar=R6Avatar.Create(stage.transform,profile);
  foreach(var t in stage.GetComponentsInChildren<Transform>()) t.gameObject.layer=30;
  var cameraObject=new GameObject("Avatar preview camera"); cameraObject.transform.SetParent(stage.transform,false); cameraObject.transform.localPosition=new Vector3(0,1.05f,3.7f); cameraObject.transform.LookAt(stage.transform.position+Vector3.up*0.95f);
  var camera=cameraObject.AddComponent<Camera>(); camera.stereoTargetEye=StereoTargetEyeMask.None; camera.cullingMask=1<<30; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.105f,0.13f,0.16f); camera.orthographic=true; camera.orthographicSize=1.08f; camera.nearClipPlane=0.1f; camera.farClipPlane=10;
  texture=new RenderTexture(384,512,16); texture.Create(); camera.targetTexture=texture; GetComponent<RawImage>().texture=texture;
 }
 public void Turn(float angle) { avatar.transform.Rotate(Vector3.up,angle,Space.Self); }
 void OnDestroy() { if(stage) Destroy(stage); if(texture) { texture.Release(); Destroy(texture); } }
}
}
