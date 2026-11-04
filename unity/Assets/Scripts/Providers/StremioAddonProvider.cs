using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    /// <summary>
    /// Connects to any standard Stremio Add-on manifest URL (Cinemeta, Torrentio, CyberFlix, etc.)
    /// conforming to the official Stremio Addon Protocol.
    /// </summary>
    public class StremioAddonProvider : IMediaProvider
    {
        public string ProviderId { get; private set; }
        public string DisplayName { get; private set; }
        public ProviderType Type => ProviderType.Stremio;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_manifestUrl) && _manifest != null;

        private string _manifestUrl;
        private string _baseUrl;
        private ManifestResponse _manifest;
        private const int ManifestTimeoutSeconds = 15;
        private const int CatalogTimeoutSeconds = 15;
        private const int StreamTimeoutSeconds = 20;

        public StremioAddonProvider(string providerId = "stremio-addon", string displayName = "Stremio Addon")
        {
            ProviderId = providerId;
            DisplayName = displayName;
        }

        public async Task<bool> AuthenticateAsync(string manifestUrl, string usernameOrToken = null, string secret = null)
        {
            _manifest = null;
            _manifestUrl = null;
            _baseUrl = null;
            if (string.IsNullOrWhiteSpace(manifestUrl)) return false;

            string normalized = manifestUrl.Trim();
            if (normalized.StartsWith("stremio://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized.Substring("stremio://".Length);
            }
            if (!normalized.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd('/') + "/manifest.json";
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var manifestUri) ||
                (manifestUri.Scheme != Uri.UriSchemeHttp && manifestUri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(manifestUri.Host))
            {
                Debug.LogWarning("[StremioAddonProvider] Manifest URL must be an HTTP(S) URL");
                return false;
            }

            _manifestUrl = normalized;
            _baseUrl = normalized.Substring(0, normalized.Length - "/manifest.json".Length);

            using var req = UnityWebRequest.Get(_manifestUrl);
            req.timeout = ManifestTimeoutSeconds;
            await SendRequestAsync(req);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[StremioAddonProvider] Manifest load failed: {req.error}");
                return false;
            }

            try
            {
                _manifest = JsonUtility.FromJson<ManifestResponse>(req.downloadHandler.text);
                if (_manifest != null && !string.IsNullOrEmpty(_manifest.name))
                {
                    DisplayName = _manifest.name;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StremioAddonProvider] Failed to parse manifest: {ex.Message}");
            }

            return false;
        }

        public Task<List<MediaCatalog>> GetCatalogsAsync()
        {
            var result = new List<MediaCatalog>();
            if (!IsAuthenticated || _manifest?.catalogs == null) return Task.FromResult(result);

            foreach (var cat in _manifest.catalogs)
            {
                result.Add(new MediaCatalog
                {
                    ProviderId = ProviderId,
                    CatalogId = cat.id,
                    Name = $"{DisplayName}: {cat.name ?? cat.id}",
                    Type = cat.type ?? "movie"
                });
            }

            return Task.FromResult(result);
        }

        public async Task<List<MediaItem>> GetCatalogItemsAsync(string catalogId, int skip = 0)
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(catalogId) || skip < 0) return result;

            var catalogMeta = FindCatalog(catalogId);
            string type = catalogMeta?.type ?? "movie";
            string path = skip > 0
                ? $"/catalog/{UnityWebRequest.EscapeURL(type)}/{UnityWebRequest.EscapeURL(catalogId)}/skip={skip}.json"
                : $"/catalog/{UnityWebRequest.EscapeURL(type)}/{UnityWebRequest.EscapeURL(catalogId)}.json";

            string url = _baseUrl + path;
            using var req = UnityWebRequest.Get(url);
            req.timeout = CatalogTimeoutSeconds;
            await SendRequestAsync(req);

            if (req.result != UnityWebRequest.Result.Success) return result;

            try
            {
                var envelope = JsonUtility.FromJson<MetasEnvelope>(req.downloadHandler.text);
                if (envelope?.metas != null)
                {
                    foreach (var m in envelope.metas)
                    {
                        result.Add(new MediaItem
                        {
                            ProviderId = ProviderId,
                            ItemId = m.id,
                            Title = m.name,
                            PosterUrl = m.poster,
                            Type = m.type ?? type
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StremioAddonProvider] Catalog response parse failed: {ex.Message}");
            }

            return result;
        }

        public Task<List<MediaItem>> GetContinueWatchingAsync()
        {
            // Standard Stremio add-ons are stateless catalogs; continue watching is managed by the client or account
            return Task.FromResult(new List<MediaItem>());
        }

        public async Task<List<ResolvedStream>> GetStreamsAsync(string itemId)
        {
            var result = new List<ResolvedStream>();
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(itemId)) return result;

            string type = itemId.StartsWith("tt") ? "movie" : "series";
            string url = $"{_baseUrl}/stream/{UnityWebRequest.EscapeURL(type)}/{UnityWebRequest.EscapeURL(itemId)}.json";

            using var req = UnityWebRequest.Get(url);
            req.timeout = StreamTimeoutSeconds;
            await SendRequestAsync(req);

            if (req.result != UnityWebRequest.Result.Success) return result;

            try
            {
                var envelope = JsonUtility.FromJson<StreamsEnvelope>(req.downloadHandler.text);
                if (envelope?.streams != null)
                {
                    foreach (var s in envelope.streams)
                    {
                        if (string.IsNullOrEmpty(s.url)) continue;

                        result.Add(new ResolvedStream
                        {
                            ProviderId = ProviderId,
                            ItemId = itemId,
                            Title = !string.IsNullOrEmpty(s.name) && !string.IsNullOrEmpty(s.title)
                                ? $"{s.name} - {s.title}"
                                : (s.title ?? s.name ?? "Stream"),
                            StreamUrl = s.url,
                            SourceCodec = null,
                            RequiresServerProxy = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StremioAddonProvider] Stream response parse failed: {ex.Message}");
            }

            return result;
        }

        private CatalogMeta FindCatalog(string catalogId)
        {
            if (_manifest?.catalogs == null) return null;
            foreach (var c in _manifest.catalogs)
            {
                if (c.id == catalogId) return c;
            }
            return null;
        }

        private Task SendRequestAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = request.SendWebRequest();
            op.completed += _ => tcs.SetResult(true);
            return tcs.Task;
        }

        [Serializable]
        private class ManifestResponse
        {
            public string id;
            public string name;
            public string description;
            public CatalogMeta[] catalogs;
        }

        [Serializable]
        private class CatalogMeta
        {
            public string id;
            public string type;
            public string name;
        }

        [Serializable]
        private class MetasEnvelope
        {
            public MetaEntry[] metas;
        }

        [Serializable]
        private class MetaEntry
        {
            public string id;
            public string type;
            public string name;
            public string poster;
        }

        [Serializable]
        private class StreamsEnvelope
        {
            public StreamEntry[] streams;
        }

        [Serializable]
        private class StreamEntry
        {
            public string name;
            public string title;
            public string url;
        }
    }
}
