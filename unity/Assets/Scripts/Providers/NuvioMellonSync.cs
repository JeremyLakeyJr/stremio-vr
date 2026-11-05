using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    public class NuvioMellonSync
    {
        private const int RequestTimeoutSeconds = 15;

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
            if (string.IsNullOrWhiteSpace(mellonServerUrl) || string.IsNullOrWhiteSpace(authToken))
            {
                Debug.LogWarning("[NuvioMellonSync] Mellon server URL and auth token are required");
                return manifests;
            }

            if (!Uri.TryCreate(mellonServerUrl.Trim(), UriKind.Absolute, out var serverUri) ||
                (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
            {
                Debug.LogWarning("[NuvioMellonSync] Mellon server URL must be an HTTP(S) URL");
                return manifests;
            }

            string baseUrl = serverUri.ToString().TrimEnd('/');
            using var req = UnityWebRequest.Get($"{baseUrl}/api/addons");
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Authorization", $"Bearer {authToken}");

            var tcs = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
            await tcs.Task;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[NuvioMellonSync] Failed to pull Mellon addons: {req.error}");
                return manifests;
            }

            MellonProfileResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<MellonProfileResponse>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NuvioMellonSync] Mellon addons response parse failed: {ex.Message}");
                return manifests;
            }
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
