using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    public class SiloProvider : IMediaProvider
    {
        public string ProviderId { get; private set; }
        public string DisplayName { get; private set; }
        public ProviderType Type => ProviderType.Silo;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_token) && !string.IsNullOrEmpty(_serverUrl);

        private string _serverUrl;
        private string _token;

        public SiloProvider(string providerId = "silo", string displayName = "Silo")
        {
            ProviderId = providerId;
            DisplayName = displayName;
        }

        public void RestoreSession(string serverUrl, string token)
        {
            _serverUrl = NormalizeUrl(serverUrl);
            _token = token;
        }

        public async Task<bool> AuthenticateAsync(string serverUrl, string username, string password)
        {
            _serverUrl = NormalizeUrl(serverUrl);
            string url = $"{_serverUrl}/api/auth/login";
            string bodyJson = JsonUtility.ToJson(new LoginRequest { Username = username, Password = password });

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            await SendRequestAsync(req);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SiloProvider] Auth failed: {req.error}");
                return false;
            }

            var resp = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);
            if (resp == null || string.IsNullOrEmpty(resp.Token))
                return false;

            _token = resp.Token;
            return IsAuthenticated;
        }

        public async Task<List<MediaCatalog>> GetCatalogsAsync()
        {
            var result = new List<MediaCatalog>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/api/libraries";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var env = JsonUtility.FromJson<LibrariesEnvelope>(json);
            if (env?.Libraries == null) return result;

            foreach (var lib in env.Libraries)
            {
                result.Add(new MediaCatalog
                {
                    ProviderId = ProviderId,
                    CatalogId = lib.Id,
                    Name = $"{DisplayName}: {lib.Name}",
                    Type = lib.Type ?? "movie"
                });
            }

            return result;
        }

        public async Task<List<MediaItem>> GetCatalogItemsAsync(string catalogId, int skip = 0)
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/api/libraries/{UnityWebRequest.EscapeURL(catalogId)}/items?skip={skip}&limit=48";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var env = JsonUtility.FromJson<SiloItemsEnvelope>(json);
            if (env?.Items == null) return result;

            foreach (var item in env.Items)
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

            string url = $"{_serverUrl}/api/continue-watching";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var env = JsonUtility.FromJson<SiloItemsEnvelope>(json);
            if (env?.Items == null) return result;

            foreach (var item in env.Items)
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

        public async Task<List<ResolvedStream>> GetStreamsAsync(string itemId)
        {
            var streams = new List<ResolvedStream>();
            if (!IsAuthenticated) return streams;

            string url = $"{_serverUrl}/api/items/{UnityWebRequest.EscapeURL(itemId)}/streams";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return streams;

            var env = JsonUtility.FromJson<SiloStreamsEnvelope>(json);
            if (env?.Streams == null) return streams;

            foreach (var stream in env.Streams)
            {
                streams.Add(new ResolvedStream
                {
                    Title = $"[{DisplayName}] {stream.Title}",
                    StreamUrl = stream.Url,
                    SourceCodec = stream.Codec?.ToLowerInvariant() ?? "h264",
                    RequiresServerProxy = true,
                    Headers = new Dictionary<string, string>
                    {
                        { "Authorization", $"Bearer {_token}" }
                    }
                });
            }

            return streams;
        }

        private async Task<string> GetJsonAsync(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", $"Bearer {_token}");
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

        [Serializable] private class LoginRequest { public string Username; public string Password; }
        [Serializable] private class LoginResponse { public string Token; }
        [Serializable] private class LibrariesEnvelope { public LibraryDto[] Libraries; }
        [Serializable] private class LibraryDto { public string Id; public string Name; public string Type; }
        [Serializable] private class SiloItemsEnvelope { public SiloItemDto[] Items; }
        [Serializable]
        private class SiloItemDto
        {
            public string Id;
            public string Title;
            public string PosterUrl;
            public string Type;
            public long RuntimeTicks;
            public long ResumePositionTicks;
        }
        [Serializable] private class SiloStreamsEnvelope { public SiloStreamDto[] Streams; }
        [Serializable] private class SiloStreamDto { public string Title; public string Url; public string Codec; }
    }
}
