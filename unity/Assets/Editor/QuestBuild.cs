using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace StremioVR.Editor {
public static class QuestBuild {
    [MenuItem("Movie Night/Configure Quest project")]
    public static void Configure() {
        Directory.CreateDirectory("Assets/Resources");
        const string effects="Assets/Resources/MovieNightPostProcess.asset";
        if(!File.Exists(effects)) AssetDatabase.CopyAsset("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset",effects);
        Directory.CreateDirectory("Assets/Scenes"); Directory.CreateDirectory("Assets/XR");
        PlayerSettings.companyName = "StremioVR"; PlayerSettings.productName = "Movie Night";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.jeremylakey.stremiovr");
        PlayerSettings.bundleVersion = "0.1.2"; PlayerSettings.Android.bundleVersionCode = 3;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input = player.FindProperty("activeInputHandler"); if (input != null) { input.intValue = 2; player.ApplyModifiedPropertiesWithoutUndo(); }
        var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (general == null) {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget)) {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/GeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            general = ScriptableObject.CreateInstance<XRGeneralSettings>();
            AssetDatabase.AddObjectToAsset(general, perTarget); perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, general); EditorUtility.SetDirty(perTarget);
        }
        if (general.Manager == null) { general.Manager = ScriptableObject.CreateInstance<XRManagerSettings>(); AssetDatabase.AddObjectToAsset(general.Manager, general); }
        general.InitManagerOnStart = true; general.Manager.automaticLoading = true; general.Manager.automaticRunning = true;
        if (!XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android)) throw new BuildFailedException("Unable to assign OpenXR loader");
        var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (xr == null) throw new BuildFailedException("Open XR Plug-in Management once, then rerun Configure");
        xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        var quest = xr.GetFeature<MetaQuestFeature>(); var touch = xr.GetFeature<OculusTouchControllerProfile>();
        if (quest == null || touch == null) throw new BuildFailedException("Open Android OpenXR settings to initialize Meta Quest and Touch features, then rerun Configure");
        quest.enabled = true; touch.enabled = true;
        var questSettings = new SerializedObject(quest);
        questSettings.FindProperty("optimizeBufferDiscards").boolValue = false;
        questSettings.ApplyModifiedPropertiesWithoutUndo();
        xr.optimizeBufferDiscards = false;
        EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager); EditorUtility.SetDirty(xr); EditorUtility.SetDirty(quest); EditorUtility.SetDirty(touch);
        // Runtime-created materials must survive shader stripping in an APK.
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
        foreach (string name in new[] { "Unlit/Color", "Unlit/Texture", "UI/Default", "StremioVR/Avatar", "StremioVR/World", "StremioVR/ScreenGlow", "MovieNight/Mirror", "MovieNight/EdgeSmooth", "Skybox/Panoramic" }) {
            var shader = Shader.Find(name); if (shader == null) throw new BuildFailedException("Missing shader: " + name);
            bool found = false; for (int i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) found = true;
            if (!found) { int index = shaders.arraySize; shaders.InsertArrayElementAtIndex(index); shaders.GetArrayElementAtIndex(index).objectReferenceValue = shader; }
        }
        graphics.ApplyModifiedPropertiesWithoutUndo();
        if (!File.Exists("Assets/Scenes/Cinema.unity")) {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Stremio VR").AddComponent<CinemaApp>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Cinema.unity");
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Cinema.unity", true) };
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("Quest configuration saved. Restart Unity if requested for input-system changes.");
    }
    [MenuItem("Movie Night/Build Quest APK")]
    public static void Build() {
        R6Import.Prepare(); HomeWorldBuild.Prepare(); if(!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Worlds/Home.prefab")) throw new BuildFailedException("Import Open Concept Home first with scripts/import-private-home.py; Home is required."); Configure(); Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Cinema.unity" }, locationPathName = "Builds/movie-night.apk", target = BuildTarget.Android, options = BuildOptions.Development });
        if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("APK build failed: " + report.summary.result);
    }
}
}
