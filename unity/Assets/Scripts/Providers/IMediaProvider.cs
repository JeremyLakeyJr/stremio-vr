using System.Collections.Generic;
using System.Threading.Tasks;

namespace StremioVR.Providers
{
    public enum ProviderType
    {
        Stremio,
        Jellyfin, // Covers both Jellyfin and lostb1t/remux
        Silo,
        MediaStorm,
        Nuvio // Mellon sync + Stremio addons + local JS plugins
    }

    public class MediaCatalog
    {
        public string ProviderId;
        public string CatalogId;
        public string Name;
        public string Type; // "movie" or "series"
    }

    public class MediaItem
    {
        public string ProviderId;
        public string ItemId;
        public string Title;
        public string PosterUrl;
        public string Type; // "movie", "series", "episode"
        public long ResumePositionTicks; // For Continue Watching
        public long RuntimeTicks;
    }

    public class ResolvedStream
    {
        public string Title; // e.g., "1080p HEVC Direct" or "AIOStreams RD+"
        public string StreamUrl; // Upstream URL sent to server
        public string SourceCodec; // e.g., "h264" or "hevc"
        public int? AudioTrackIndex; // Optional audio stream selection for server transcoder
        public string SubtitleUrl; // Optional subtitle stream URL
        public Dictionary<string, string> Headers; // Upstream auth headers (X-Emby-Token, etc.)
        public bool RequiresServerProxy;
    }

    public interface IMediaProvider
    {
        string ProviderId { get; }
        string DisplayName { get; }
        ProviderType Type { get; }
        bool IsAuthenticated { get; }

        Task<bool> AuthenticateAsync(string serverUrl, string usernameOrToken, string secret);
        Task<List<MediaCatalog>> GetCatalogsAsync();
        Task<List<MediaItem>> GetCatalogItemsAsync(string catalogId, int skip = 0);
        Task<List<MediaItem>> GetContinueWatchingAsync();
        Task<List<ResolvedStream>> GetStreamsAsync(string itemId);
    }
}
