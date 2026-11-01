using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    public class NuvioMellonSync
    {
        [Serializable]
        private class MellonProfileResponse
        {
            public string[] addons; // Array of manifest.json URLs
        }

        /// <summary>
        /// Connects to a Mellon or Nuvio sync instance, pulls the profile's Stremio-compatible
        /// manifest.json URLs, and registers them with Movie Night's existing addon manager.
        /// </summary>
        public async Task<List<string>> FetchProfileAddonManifestsAsync(string mellonServerUrl, string authToken)
        {
            var manifests = new List<string>();
            string baseUrl = mellonServerUrl.Trim().TrimEnd('/');
            using var req = UnityWebRequest.Get($"{baseUrl}/api/addons");
            req.SetRequestHeader("Authorization", $"Bearer {authToken}");

            var tcs = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
            await tcs.Task;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[NuvioMellonSync] Failed to pull Mellon addons: {req.error}");
                return manifests;
            }

            var parsed = JsonUtility.FromJson<MellonProfileResponse>(req.downloadHandler.text);
            if (parsed?.addons != null)
            {
                foreach (var url in parsed.addons)
                {
                    if (!string.IsNullOrWhiteSpace(url) && url.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        manifests.Add(url.Trim());
                    }
                }
            }
            return manifests;
        }
    }
}
