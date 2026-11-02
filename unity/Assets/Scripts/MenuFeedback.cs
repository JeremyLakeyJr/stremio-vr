using UnityEngine;
using UnityEngine.XR;
namespace StremioVR {
// One quiet, spatial source shared by controls; no assets or microphone access required.
public sealed class MenuFeedback : MonoBehaviour {
 public static MenuFeedback Current { get; private set; }
 public bool Sounds=true,Haptics=true,ReducedMotion;
 AudioSource source; AudioClip hover,click,error; float nextHover,nextError;
 void Awake(){Current=this;source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=1;source.dopplerLevel=0;source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=1;source.maxDistance=5;hover=Tone("Menu hover",680,.025f);click=Tone("Menu select",920,.055f);error=Tone("Menu attention",240,.11f);}
 static AudioClip Tone(string name,float frequency,float duration){int count=(int)(24000*duration);var samples=new float[count];for(int i=0;i<count;i++){float t=(float)i/count;samples[i]=Mathf.Sin(2*Mathf.PI*frequency*i/24000)*Mathf.Sin(Mathf.PI*t)*.16f;}var clip=AudioClip.Create(name,count,1,24000,false);clip.SetData(samples,0);return clip;}
 public void Play(Vector3 position,bool selected=false,bool attention=false){if(attention){if(Time.unscaledTime<nextError)return;nextError=Time.unscaledTime+.5f;}else if(!selected){if(Time.unscaledTime<nextHover)return;nextHover=Time.unscaledTime+.08f;}transform.position=position;if(Sounds)source.PlayOneShot(attention?error:selected?click:hover,(selected||attention)? .6f : .3f);if(Haptics)Pulse(selected?.22f:.08f,selected?.035f:.018f);}
 static void Pulse(float amplitude,float duration){
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
  var system=Valve.VR.OpenVR.System;if(system!=null){uint id=system.GetTrackedDeviceIndexForControllerRole(Valve.VR.ETrackedControllerRole.RightHand);if(id!=uint.MaxValue)system.TriggerHapticPulse(id,0,(ushort)(amplitude*3000));}
#else
  var device=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);if(device.TryGetHapticCapabilities(out var capabilities)&&capabilities.supportsImpulse)device.SendHapticImpulse(0,amplitude,duration);
#endif
 }
 void OnDestroy(){if(Current==this)Current=null;if(Application.isPlaying){Destroy(hover);Destroy(click);Destroy(error);}else{DestroyImmediate(hover);DestroyImmediate(click);DestroyImmediate(error);}}
}
}
