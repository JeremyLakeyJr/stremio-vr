using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    public class MediaStormProvider : IMediaProvider
    {
        public string ProviderId { get; private set; }
        public string DisplayName { get; private set; }
        public ProviderType Type => ProviderType.MediaStorm;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_serverUrl);

        private string _serverUrl;
        private string _apiKey;
        private const int RequestTimeoutSeconds = 15;

        public MediaStormProvider(string providerId = "mediastorm", string displayName = "MediaStorm")
        {
            ProviderId = providerId;
            DisplayName = displayName;
        }

        public void RestoreSession(string serverUrl, string apiKey)
        {
            _serverUrl = NormalizeUrl(serverUrl);
            _apiKey = apiKey;
        }

        public async Task<bool> AuthenticateAsync(string serverUrl, string apiKey, string secret = null)
        {
            _serverUrl = NormalizeUrl(serverUrl);
            _apiKey = apiKey?.Trim();

            string url = $"{_serverUrl}/api/v1/health";
            using var req = UnityWebRequest.Get(url);
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("X-MediaStorm-Key", _apiKey);

            await SendRequestAsync(req);
            if (req.result != UnityWebRequest.Result.Success && req.responseCode != 200)
            {
                // Fallback check against root api
                using var fallbackReq = UnityWebRequest.Get($"{_serverUrl}/api/v1/catalogs");
                fallbackReq.timeout = RequestTimeoutSeconds;
                fallbackReq.SetRequestHeader("X-MediaStorm-Key", _apiKey);
                await SendRequestAsync(fallbackReq);
                if (fallbackReq.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[MediaStormProvider] Auth failed: {fallbackReq.error}");
                    return false;
                }
            }

            return IsAuthenticated;
        }

        public async Task<List<MediaCatalog>> GetCatalogsAsync()
        {
            var result = new List<MediaCatalog>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/api/v1/catalogs";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json))
            {
                // Return standard default MediaStorm catalogs
                result.Add(new MediaCatalog { ProviderId = ProviderId, CatalogId = "movies", Name = $"{DisplayName}: Movies", Type = "movie" });
                result.Add(new MediaCatalog { ProviderId = ProviderId, CatalogId = "series", Name = $"{DisplayName}: TV Series", Type = "series" });
                return result;
            }

            var envelope = JsonUtility.FromJson<CatalogsEnvelope>(json);
            if (envelope?.Catalogs == null) return result;

            foreach (var cat in envelope.Catalogs)
            {
                result.Add(new MediaCatalog
                {
                    ProviderId = ProviderId,
                    CatalogId = cat.Id,
                    Name = $"{DisplayName}: {cat.Name}",
                    Type = cat.Type ?? "movie"
                });
            }

            return result;
        }

        public async Task<List<MediaItem>> GetCatalogItemsAsync(string catalogId, int skip = 0)
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/api/v1/catalogs/{UnityWebRequest.EscapeURL(catalogId)}/items?skip={skip}&limit=48";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var envelope = JsonUtility.FromJson<MediaItemsEnvelope>(json);
            if (envelope?.Items == null) return result;

            foreach (var item in envelope.Items)
            {
                result.Add(new MediaItem
                {
                    ProviderId = ProviderId,
                    ItemId = item.Id,
                    Title = item.Title,
                    PosterUrl = item.PosterUrl,
                    Type = item.Type ?? "movie",
                    RuntimeTicks = item.RuntimeTicks,
                    ResumePositionTicks = item.ResumePositionTicks
                });
            }

            return result;
        }

        public async Task<List<MediaItem>> GetContinueWatchingAsync()
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/api/v1/user/continue-watching";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var envelope = JsonUtility.FromJson<MediaItemsEnvelope>(json);
            if (envelope?.Items == null) return result;

            foreach (var item in envelope.Items)
            {
                result.Add(new MediaItem
                {
                    ProviderId = ProviderId,
                    ItemId = item.Id,
                    Title = item.Title,
                    PosterUrl = item.PosterUrl,
                    Type = item.Type ?? "movie",
                    RuntimeTicks = item.RuntimeTicks,
                    ResumePositionTicks = item.ResumePositionTicks
                });
            }

            return result;
        }

        public async Task<List<MediaItem>> SearchAsync(string query)
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(query)) return result;

            string url = $"{_serverUrl}/api/v1/search?query={UnityWebRequest.EscapeURL(query.Trim())}&limit=32";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var envelope = JsonUtility.FromJson<MediaItemsEnvelope>(json);
            if (envelope?.Items == null) return result;

            foreach (var item in envelope.Items)
            {
                result.Add(new MediaItem
                {
                    ProviderId = ProviderId,
                    ItemId = item.Id,
                    Title = item.Title,
                    PosterUrl = item.PosterUrl,
                    Type = item.Type ?? "movie",
                    RuntimeTicks = item.RuntimeTicks,
                    ResumePositionTicks = item.ResumePositionTicks
                });
            }

            return result;
        }

        public async Task ReportPlaybackAsync(string itemId, double positionSeconds, bool isPaused = false)
        {
            if (!IsAuthenticated || string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(itemId)) return;

            long ticks = (long)(Math.Max(0, positionSeconds) * TimeSpan.TicksPerSecond);
            string url = $"{_serverUrl}/api/v1/user/progress";
            string body = JsonUtility.ToJson(new MediaStormProgressPayload
            {
                ItemId = itemId,
                PositionTicks = ticks,
                IsPaused = isPaused
            });

            using var req = new UnityWebRequest(url, "POST");
            req.timeout = RequestTimeoutSeconds;
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-MediaStorm-Key", _apiKey);

            await SendRequestAsync(req);
        }

        public async Task<List<ResolvedStream>> GetStreamsAsync(string itemId)
        {
            var streams = new List<ResolvedStream>();
            if (!IsAuthenticated) return streams;

            string url = $"{_serverUrl}/api/v1/items/{UnityWebRequest.EscapeURL(itemId)}/streams";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return streams;

            var envelope = JsonUtility.FromJson<StreamsEnvelope>(json);
            if (envelope?.Streams == null) return streams;

            foreach (var stream in envelope.Streams)
            {
                string tag = !string.IsNullOrEmpty(stream.Debrid) ? $"[{DisplayName}: {stream.Debrid}]" : $"[{DisplayName}]";
                string qual = !string.IsNullOrEmpty(stream.Quality) ? $" {stream.Quality}" : "";
                string sz = !string.IsNullOrEmpty(stream.Size) ? $" ({stream.Size})" : "";
                string displayTitle = $"{tag}{qual} {stream.Title}{sz}".Trim();

                streams.Add(new ResolvedStream
                {
                    ProviderId = ProviderId,
                    ItemId = itemId,
                    Title = displayTitle,
                    StreamUrl = stream.Url,
                    SourceCodec = stream.Codec?.ToLowerInvariant() ?? "h264",
                    AudioTrackIndex = stream.AudioTrackIndex >= 0 ? stream.AudioTrackIndex : (int?)null,
                    SubtitleUrl = stream.SubtitleUrl,
                    RequiresServerProxy = true,
                    Headers = new Dictionary<string, string>
                    {
                        { "X-MediaStorm-Key", _apiKey }
                    }
                });
            }

            return streams;
        }

        private async Task<string> GetJsonAsync(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("X-MediaStorm-Key", _apiKey);
            await SendRequestAsync(req);
            return req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null;
        }

        private static Task SendRequestAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = req.SendWebRequest();
            op.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }

        private static string NormalizeUrl(string url) => url?.Trim().TrimEnd('/') ?? "";

        [Serializable] private class CatalogsEnvelope { public CatalogDto[] Catalogs; }
        [Serializable] private class CatalogDto { public string Id; public string Name; public string Type; }
        [Serializable] private class MediaItemsEnvelope { public MediaItemDto[] Items; }
        [Serializable]
        private class MediaItemDto
        {
            public string Id;
            public string Title;
            public string PosterUrl;
            public string Type;
            public long RuntimeTicks;
            public long ResumePositionTicks;
        }
        [Serializable] private class StreamsEnvelope { public StreamDto[] Streams; }
        [Serializable]
        private class StreamDto
        {
            public string Title;
            public string Url;
            public string Codec;
            public string Quality;
            public string Debrid;
            public string Size;
            public string SubtitleUrl;
            public int AudioTrackIndex = -1;
        }
        [Serializable]
        private class MediaStormProgressPayload
        {
            public string ItemId;
            public long PositionTicks;
            public bool IsPaused;
        }
    }
}
