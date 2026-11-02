using UnityEngine;
using UnityEngine.XR;
namespace StremioVR {
// Unity OpenXR on Windows/Quest; OpenVR's legacy controller state on Linux.
public static class VrInput {
 public static Device DeviceAt(XRNode node)=>new Device(node);
 public struct Device {
  public bool isValid=>device.isValid;
  readonly XRNode node;readonly InputDevice device;
  public Device(XRNode value){node=value;device=InputDevices.GetDeviceAtXRNode(value);}
  public bool TryGetFeatureValue(InputFeatureUsage<Vector3> usage,out Vector3 value)=>device.TryGetFeatureValue(usage,out value);
  public bool TryGetFeatureValue(InputFeatureUsage<Quaternion> usage,out Quaternion value)=>device.TryGetFeatureValue(usage,out value);
  public bool TryGetFeatureValue(InputFeatureUsage<bool> usage,out bool value){
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
   if(State(out var state,out _)){Valve.VR.EVRButtonId button;if(usage.name==CommonUsages.triggerButton.name)button=Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger;else if(usage.name==CommonUsages.gripButton.name)button=Valve.VR.EVRButtonId.k_EButton_Grip;else if(usage.name==CommonUsages.menuButton.name||usage.name==CommonUsages.secondaryButton.name)button=Valve.VR.EVRButtonId.k_EButton_ApplicationMenu;else if(usage.name==CommonUsages.primaryButton.name)button=Valve.VR.EVRButtonId.k_EButton_A;else return device.TryGetFeatureValue(usage,out value);value=(state.ulButtonPressed&(1UL<<(int)button))!=0;return true;}
#endif
   return device.TryGetFeatureValue(usage,out value);
  }
  public bool TryGetFeatureValue(InputFeatureUsage<Vector2> usage,out Vector2 value){
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
   if(usage.name==CommonUsages.primary2DAxis.name&&State(out var state,out uint id)){var axes=new[]{state.rAxis0,state.rAxis1,state.rAxis2,state.rAxis3,state.rAxis4};var error=Valve.VR.ETrackedPropertyError.TrackedProp_Success;int chosen=0;for(int i=0;i<5;i++){int type=Valve.VR.OpenVR.System.GetInt32TrackedDeviceProperty(id,(Valve.VR.ETrackedDeviceProperty)((int)Valve.VR.ETrackedDeviceProperty.Prop_Axis0Type_Int32+i),ref error);if(type==(int)Valve.VR.EVRControllerAxisType.k_eControllerAxis_Joystick){chosen=i;break;}}value=new Vector2(axes[chosen].x,axes[chosen].y);return true;}
#endif
   return device.TryGetFeatureValue(usage,out value);
  }
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
  bool State(out Valve.VR.VRControllerState_t state,out uint id){state=new Valve.VR.VRControllerState_t();id=uint.MaxValue;if(node!=XRNode.LeftHand&&node!=XRNode.RightHand)return false;try{var system=Valve.VR.OpenVR.System;if(system==null)return false;id=system.GetTrackedDeviceIndexForControllerRole(node==XRNode.LeftHand?Valve.VR.ETrackedControllerRole.LeftHand:Valve.VR.ETrackedControllerRole.RightHand);return id!=uint.MaxValue&&system.GetControllerState(id,ref state,(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Valve.VR.VRControllerState_t)));}catch{return false;}}
#endif
 }
}
}
