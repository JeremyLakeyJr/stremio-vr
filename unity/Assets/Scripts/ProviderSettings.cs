using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StremioVR {
// Provider credentials share the existing platform vault, never profile.json.
public static class ProviderSettings {
    public const string VaultName = "provider-settings";
    public static readonly string[] SecretKeys = {
        SettingsKeys.JellyfinPass, SettingsKeys.JellyfinToken,
        SettingsKeys.MediaStormKey, SettingsKeys.SiloPass,
        SettingsKeys.SiloToken, SettingsKeys.SiloUser, SettingsKeys.MellonToken
    };
    public static JObject PublicSettings(JObject settings) {
        var result = (JObject)settings.DeepClone();
        foreach(var key in SecretKeys) result.Remove(key);
        return result;
    }
    public static JObject Secrets(JObject settings) {
        var result = new JObject();
        foreach(var key in SecretKeys) if(settings[key] != null) result[key] = settings[key].DeepClone();
        return result;
    }
    public static string Load() {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using(var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using(var vault = new AndroidJavaClass("com.stremiovr.SecureSession"))
            return vault.CallStatic<string>("loadNamed", activity, VaultName);
#elif (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
        return DesktopBridge.Get().LoadSecret(VaultName);
#else
        return null;
#endif
    }
    public static void Save(JObject settings) {
        string value = Secrets(settings).ToString(Newtonsoft.Json.Formatting.None);
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using(var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using(var vault = new AndroidJavaClass("com.stremiovr.SecureSession"))
            vault.CallStatic("saveNamed", activity, VaultName, value);
#elif (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
        var desktop = DesktopBridge.Get();
        if(!desktop.VaultReady) throw new Exception("Secure storage is still starting");
        desktop.SaveSecret(VaultName, value);
#endif
    }
}
}
