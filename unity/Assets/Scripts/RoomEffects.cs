using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
namespace StremioVR {
public sealed class RoomEffects : MonoBehaviour {
 public bool Enabled=true;
 PostProcessVolume volume; PostProcessProfile profile; PostProcessLayer layer;
 void Start() {
  var resources=Resources.Load<PostProcessResources>("MovieNightPostProcess"); if(!resources)return;
  var camera=GetComponent<Camera>(); camera.allowHDR=true;
  layer=gameObject.AddComponent<PostProcessLayer>(); layer.Init(resources); layer.volumeLayer=1<<27; layer.volumeTrigger=transform;
  layer.antialiasingMode=PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
  var go=new GameObject("Room glow",typeof(PostProcessVolume)); go.layer=27; go.transform.SetParent(transform,false);
  volume=go.GetComponent<PostProcessVolume>(); volume.isGlobal=true; volume.priority=10;
  profile=ScriptableObject.CreateInstance<PostProcessProfile>(); var bloom=profile.AddSettings<Bloom>(); bloom.enabled.Override(true); bloom.intensity.Override(1.5f); bloom.threshold.Override(1); bloom.diffusion.Override(5); bloom.fastMode.Override(true); volume.sharedProfile=profile;
 }
 void Update() { if(volume) volume.weight=Enabled?1:0; }
 void OnDestroy() { if(profile) { foreach(var setting in profile.settings)Destroy(setting); Destroy(profile); } }
}
}
