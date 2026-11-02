using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Unity.XR.OpenVR;
namespace StremioVR.Editor {
public static class PcBuild {
 [MenuItem("Movie Night/Build Windows PC VR")]
 public static void Windows()=>Build(BuildTarget.StandaloneWindows64);
 [MenuItem("Movie Night/Build Linux PC VR")]
 public static void Linux()=>Build(BuildTarget.StandaloneLinux64);
 public static void Configure(BuildTarget target){
  QuestBuild.Configure();
  var general=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
  if(!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,out XRGeneralSettingsPerBuildTarget perTarget))throw new BuildFailedException("XR settings missing");
  if(general==null){general=ScriptableObject.CreateInstance<XRGeneralSettings>();AssetDatabase.AddObjectToAsset(general,perTarget);perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Standalone,general);}
  if(general.Manager==null){general.Manager=ScriptableObject.CreateInstance<XRManagerSettings>();AssetDatabase.AddObjectToAsset(general.Manager,general);}
  foreach(var loader in general.Manager.activeLoaders.ToArray())XRPackageMetadataStore.RemoveLoader(general.Manager,loader.GetType().FullName,BuildTargetGroup.Standalone);
  string selected=target==BuildTarget.StandaloneLinux64?"Unity.XR.OpenVR.OpenVRLoader":"UnityEngine.XR.OpenXR.OpenXRLoader";
  if(!XRPackageMetadataStore.AssignLoader(general.Manager,selected,BuildTargetGroup.Standalone))throw new BuildFailedException("Could not enable "+selected);
  general.InitManagerOnStart=true;general.Manager.automaticLoading=true;general.Manager.automaticRunning=true;
  if(target==BuildTarget.StandaloneWindows64){var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);xr.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;foreach(var feature in xr.GetFeatures()){if(feature is OculusTouchControllerProfile||feature is ValveIndexControllerProfile||feature is HTCViveControllerProfile||feature is MicrosoftMotionControllerProfile)feature.enabled=true;}EditorUtility.SetDirty(xr);}
  else{if(!EditorBuildSettings.TryGetConfigObject("Unity.XR.OpenVR.Settings",out OpenVRSettings settings)){settings=ScriptableObject.CreateInstance<OpenVRSettings>();AssetDatabase.CreateAsset(settings,"Assets/XR/PcOpenVRSettings.asset");EditorBuildSettings.AddConfigObject("Unity.XR.OpenVR.Settings",settings,true);}settings.StereoRenderingMode=OpenVRSettings.StereoRenderingModes.MultiPass;settings.PromptToUpgradePackage=false;settings.PromptToUpgradePreviewPackages=false;EditorUtility.SetDirty(settings);}
  PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,target==BuildTarget.StandaloneLinux64?new[]{GraphicsDeviceType.Vulkan}:new[]{GraphicsDeviceType.Direct3D11});
  PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.resizableWindow=true;
  EditorUtility.SetDirty(general);EditorUtility.SetDirty(general.Manager);AssetDatabase.SaveAssets();
 }
 static void Build(BuildTarget target){
  R6Import.Prepare();ChessImport.Prepare();HomeWorldBuild.Prepare();Configure(target);
  string folder=target==BuildTarget.StandaloneLinux64?"Linux":"Windows",file=target==BuildTarget.StandaloneLinux64?"MovieNight.x86_64":"MovieNight.exe";Directory.CreateDirectory("Builds/"+folder);
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Cinema.unity"},locationPathName="Builds/"+folder+"/"+file,target=target,options=BuildOptions.Development});if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("PC build failed: "+report.summary.result);
 }
}
}
