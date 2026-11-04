using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    public class JellyfinProvider : IMediaProvider
    {
        public string ProviderId { get; private set; }
        public string DisplayName { get; private set; }
        public ProviderType Type => ProviderType.Jellyfin;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_userId);
        public string AccessToken => _accessToken;
        public string UserId => _userId;

        private string _serverUrl;
        private string _accessToken;
        private string _userId;
        private readonly string _deviceId;

        public JellyfinProvider(string providerId = "jellyfin", string displayName = "Jellyfin / Remux")
        {
            ProviderId = providerId;
            DisplayName = displayName;
            _deviceId = SystemInfo.deviceUniqueIdentifier;
        }

        public void RestoreSession(string serverUrl, string userId, string accessToken)
        {
            _serverUrl = NormalizeUrl(serverUrl);
            _userId = userId;
            _accessToken = accessToken;
        }

        public async Task<bool> AuthenticateAsync(string serverUrl, string username, string password)
        {
            _serverUrl = NormalizeUrl(serverUrl);
            string url = $"{_serverUrl}/Users/AuthenticateByName";
            string bodyJson = JsonUtility.ToJson(new AuthRequest { Username = username, Pw = password });

            using var req = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(bodyJson);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Emby-Authorization", BuildEmbyAuthHeader(null));

            await SendRequestAsync(req);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[JellyfinProvider] Auth failed: {req.error}");
                return false;
            }

            var authResp = JsonUtility.FromJson<AuthResponse>(req.downloadHandler.text);
            if (authResp == null || string.IsNullOrEmpty(authResp.AccessToken))
                return false;

            _accessToken = authResp.AccessToken;
            _userId = authResp.User?.Id;
            return IsAuthenticated;
        }

        public async Task ReportPlaybackAsync(string endpoint, string jsonBody)
        {
            if (!IsAuthenticated || string.IsNullOrEmpty(_serverUrl)) return;
            await GetJsonAsync($"{_serverUrl}{endpoint}", "POST", jsonBody);
        }

        public async Task<List<MediaCatalog>> GetCatalogsAsync()
        {
            var result = new List<MediaCatalog>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/Users/{_userId}/Views";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var views = JsonUtility.FromJson<ItemsEnvelope>(json);
            if (views?.Items == null) return result;

            foreach (var item in views.Items)
            {
                string type = item.CollectionType == "tvshows" ? "series" : "movie";
                result.Add(new MediaCatalog
                {
                    ProviderId = ProviderId,
                    CatalogId = item.Id,
                    Name = $"{DisplayName}: {item.Name}",
                    Type = type
                });
            }

            return result;
        }

        public async Task<List<MediaItem>> GetCatalogItemsAsync(string catalogId, int skip = 0)
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/Users/{_userId}/Items" +
                         $"?ParentId={UnityWebRequest.EscapeURL(catalogId)}" +
                         $"&IncludeItemTypes=Movie,Series" +
                         $"&Recursive=true&StartIndex={skip}&Limit=48" +
                         $"&SortBy=SortName&SortOrder=Ascending";

            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var envelope = JsonUtility.FromJson<ItemsEnvelope>(json);
            if (envelope?.Items == null) return result;

            foreach (var dto in envelope.Items)
            {
                result.Add(MapToMediaItem(dto));
            }

            return result;
        }

        public async Task<List<MediaItem>> GetContinueWatchingAsync()
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated) return result;

            string url = $"{_serverUrl}/Users/{_userId}/Items/Resume?Limit=20&MediaTypes=Video";
            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var envelope = JsonUtility.FromJson<ItemsEnvelope>(json);
            if (envelope?.Items == null) return result;

            foreach (var dto in envelope.Items)
            {
                result.Add(MapToMediaItem(dto));
            }

            return result;
        }

        public async Task<List<MediaItem>> SearchAsync(string query)
        {
            var result = new List<MediaItem>();
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(query)) return result;

            string url = $"{_serverUrl}/Users/{_userId}/Items" +
                         $"?searchTerm={UnityWebRequest.EscapeURL(query)}" +
                         $"&IncludeItemTypes=Movie,Series,Episode" +
                         $"&Recursive=true&Limit=40";

            string json = await GetJsonAsync(url);
            if (string.IsNullOrEmpty(json)) return result;

            var envelope = JsonUtility.FromJson<ItemsEnvelope>(json);
            if (envelope?.Items == null) return result;

            foreach (var dto in envelope.Items)
            {
                result.Add(MapToMediaItem(dto));
            }

            return result;
        }

        public async Task<List<ResolvedStream>> GetStreamsAsync(string itemId)
        {
            var streams = new List<ResolvedStream>();
            if (!IsAuthenticated) return streams;

            // PlaybackInfo returns all MediaSources (including Remux Stremio/WebDAV/Debrid sources)
            string url = $"{_serverUrl}/Items/{UnityWebRequest.EscapeURL(itemId)}/PlaybackInfo?UserId={_userId}";
            string json = await GetJsonAsync(url, method: "POST", jsonBody: "{}");
            if (string.IsNullOrEmpty(json)) return streams;

            var playbackInfo = JsonUtility.FromJson<PlaybackInfoResponse>(json);
            if (playbackInfo?.MediaSources == null) return streams;

            foreach (var source in playbackInfo.MediaSources)
            {
                string videoCodec = ExtractVideoCodec(source);
                string streamUrl = !string.IsNullOrEmpty(source.Path) && source.IsRemote
                    ? source.Path
                    : $"{_serverUrl}/Videos/{itemId}/stream?static=true&MediaSourceId={source.Id}";

                var subTracks = new List<SubtitleTrackInfo>();
                if (source.MediaStreams != null)
                {
                    foreach (var ms in source.MediaStreams)
                    {
                        if (ms.Type == "Subtitle")
                        {
                            string lang = !string.IsNullOrEmpty(ms.DisplayTitle) ? ms.DisplayTitle : (!string.IsNullOrEmpty(ms.Language) ? ms.Language : "Subtitle " + ms.Index);
                            string subUrl = $"{_serverUrl}/Videos/{itemId}/{source.Id}/Subtitles/{ms.Index}/Stream.vtt?api_key={_accessToken}";
                            subTracks.Add(new SubtitleTrackInfo { Language = lang, Url = subUrl });
                        }
                    }
                }

                streams.Add(new ResolvedStream
                {
                    ProviderId = ProviderId,
                    ItemId = itemId,
                    Title = $"[{DisplayName}] {source.Name} ({videoCodec?.ToUpperInvariant() ?? "DIRECT"})",
                    StreamUrl = streamUrl,
                    SourceCodec = videoCodec, // Passed to server/hwaccel.js for -c:v copy check
                    SubtitleTracks = subTracks.Count > 0 ? subTracks : null,
                    SubtitleUrl = subTracks.Count > 0 ? subTracks[0].Url : null,
                    RequiresServerProxy = true,
                    Headers = new Dictionary<string, string>
                    {
                        { "X-Emby-Token", _accessToken },
                        { "X-Emby-Authorization", BuildEmbyAuthHeader(_accessToken) }
                    }
                });
            }

            return streams;
        }

        private MediaItem MapToMediaItem(JellyfinItemDto dto)
        {
            return new MediaItem
            {
                ProviderId = ProviderId,
                ItemId = dto.Id,
                Title = dto.Name,
                Type = dto.Type == "Series" ? "series" : (dto.Type == "Episode" ? "episode" : "movie"),
                PosterUrl = $"{_serverUrl}/Items/{dto.Id}/Images/Primary?maxHeight=480&quality=85",
                RuntimeTicks = dto.RunTimeTicks,
                ResumePositionTicks = dto.UserData != null ? dto.UserData.PlaybackPositionTicks : 0
            };
        }

        private static string ExtractVideoCodec(MediaSourceDto source)
        {
            if (source.MediaStreams == null) return null;
            foreach (var stream in source.MediaStreams)
            {
                if (stream.Type == "Video" && !string.IsNullOrEmpty(stream.Codec))
                    return stream.Codec.ToLowerInvariant();
            }
            return null;
        }

        private string BuildEmbyAuthHeader(string token)
        {
            string header = $"MediaBrowser Client=\"MovieNightVR\", Device=\"MetaQuest\", DeviceId=\"{_deviceId}\", Version=\"1.0.0\"";
            if (!string.IsNullOrEmpty(token))
                header += $", Token=\"{token}\"";
            return header;
        }

        private async Task<string> GetJsonAsync(string url, string method = "GET", string jsonBody = null)
        {
            using var req = new UnityWebRequest(url, method);
            if (jsonBody != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("X-Emby-Token", _accessToken);
            req.SetRequestHeader("X-Emby-Authorization", BuildEmbyAuthHeader(_accessToken));

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

        [Serializable] private class AuthRequest { public string Username; public string Pw; }
        [Serializable] private class AuthResponse { public string AccessToken; public UserDto User; }
        [Serializable] private class UserDto { public string Id; public string Name; }
        [Serializable] private class ItemsEnvelope { public JellyfinItemDto[] Items; }
        [Serializable]
        private class JellyfinItemDto
        {
            public string Id;
            public string Name;
            public string Type;
            public string CollectionType;
            public long RunTimeTicks;
            public UserDataDto UserData;
        }
        [Serializable] private class UserDataDto { public long PlaybackPositionTicks; }
        [Serializable] private class PlaybackInfoResponse { public MediaSourceDto[] MediaSources; }
        [Serializable]
        private class MediaSourceDto
        {
            public string Id;
            public string Name;
            public string Path;
            public bool IsRemote;
            public MediaStreamDto[] MediaStreams;
        }
        [Serializable] private class MediaStreamDto { public string Type; public string Codec; public int Index; public string DisplayTitle; public string Language; }
    }
}
