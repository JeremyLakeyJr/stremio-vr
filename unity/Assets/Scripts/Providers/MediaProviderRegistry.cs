using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace StremioVR.Providers
{
    /// <summary>
    /// Central manager for all media backends in Movie Night VR.
    /// Aggregates catalogs, continue watching items, and stream resolution across
    /// Stremio, Jellyfin, Remux, MediaStorm, Silo, and Nuvio.
    /// </summary>
    public sealed class MediaProviderRegistry : MonoBehaviour
    {
        public static MediaProviderRegistry Instance { get; private set; }

        public event Action ProvidersChanged;
        public event Action<IMediaProvider> ActiveProviderChanged;

        public JellyfinProvider Jellyfin { get; private set; }
        public MediaStormProvider MediaStorm { get; private set; }
        public SiloProvider Silo { get; private set; }
        public NuvioMellonSync NuvioSync { get; private set; }

        private readonly Dictionary<string, IMediaProvider> _providers = new Dictionary<string, IMediaProvider>();
        private IMediaProvider _activeProvider;

        public IMediaProvider ActiveProvider
        {
            get => _activeProvider;
            set
            {
                if (_activeProvider != value)
                {
                    _activeProvider = value;
                    ActiveProviderChanged?.Invoke(_activeProvider);
                }
            }
        }

        public IEnumerable<IMediaProvider> Providers => _providers.Values;

        public IEnumerable<IMediaProvider> AuthenticatedProviders =>
            _providers.Values.Where(p => p.IsAuthenticated);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeProviders();
        }

        private void InitializeProviders()
        {
            Jellyfin = new JellyfinProvider("jellyfin", "Jellyfin / Remux");
            MediaStorm = new MediaStormProvider("mediastorm", "MediaStorm");
            Silo = new SiloProvider("silo", "Silo");
            NuvioSync = new NuvioMellonSync();

            RegisterProvider(Jellyfin);
            RegisterProvider(MediaStorm);
            RegisterProvider(Silo);

            // Default active provider
            _activeProvider = Jellyfin;
        }

        public void RegisterProvider(IMediaProvider provider)
        {
            if (provider == null) return;
            _providers[provider.ProviderId] = provider;
            ProvidersChanged?.Invoke();
        }

        public async Task<bool> AddStremioAddonAsync(string manifestUrl, string displayName = null)
        {
            string providerId = "addon_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var addon = new StremioAddonProvider(providerId, displayName ?? "Stremio Addon");
            bool ok = await addon.AuthenticateAsync(manifestUrl);
            if (ok)
            {
                RegisterProvider(addon);
            }
            return ok;
        }

        public async Task<int> SyncMellonAddonsAsync(string mellonServerUrl, string authToken)
        {
            if (NuvioSync == null || string.IsNullOrWhiteSpace(mellonServerUrl)) return 0;
            var manifests = await NuvioSync.FetchProfileAddonManifestsAsync(mellonServerUrl, authToken);
            int added = 0;
            foreach (var manifestUrl in manifests)
            {
                if (await AddStremioAddonAsync(manifestUrl))
                {
                    added++;
                }
            }
            return added;
        }

        public IMediaProvider GetProvider(string providerId)
        {
            return _providers.TryGetValue(providerId, out var provider) ? provider : null;
        }

        /// <summary>
        /// Fetches catalogs from all authenticated providers or a specific provider.
        /// </summary>
        public async Task<List<MediaCatalog>> GetAllCatalogsAsync(string specificProviderId = null)
        {
            var catalogs = new List<MediaCatalog>();

            if (!string.IsNullOrEmpty(specificProviderId))
            {
                var prov = GetProvider(specificProviderId);
                if (prov != null && prov.IsAuthenticated)
                {
                    catalogs.AddRange(await prov.GetCatalogsAsync());
                }
                return catalogs;
            }

            foreach (var prov in AuthenticatedProviders)
            {
                try
                {
                    var items = await prov.GetCatalogsAsync();
                    if (items != null) catalogs.AddRange(items);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MediaProviderRegistry] Failed to get catalogs for {prov.DisplayName}: {e.Message}");
                }
            }

            return catalogs;
        }

        /// <summary>
        /// Merges Continue Watching / In-Progress queues from all authenticated backends,
        /// sorted by resume timestamp.
        /// </summary>
        public async Task<List<MediaItem>> GetUnifiedContinueWatchingAsync()
        {
            var aggregated = new List<MediaItem>();

            foreach (var prov in AuthenticatedProviders)
            {
                try
                {
                    var items = await prov.GetContinueWatchingAsync();
                    if (items != null)
                    {
                        aggregated.AddRange(items);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MediaProviderRegistry] Failed to fetch resume items for {prov.DisplayName}: {e.Message}");
                }
            }

            // Sort items with the most recent resume ticks first
            return aggregated.OrderByDescending(item => item.ResumePositionTicks).ToList();
        }

        /// <summary>
        /// Searches across all authenticated search-capable providers (MediaStorm, etc.).
        /// </summary>
        public async Task<List<MediaItem>> SearchAllProvidersAsync(string query)
        {
            var results = new List<MediaItem>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            if (MediaStorm != null && MediaStorm.IsAuthenticated)
            {
                try
                {
                    var msItems = await MediaStorm.SearchAsync(query);
                    if (msItems != null) results.AddRange(msItems);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MediaProviderRegistry] MediaStorm search failed: {e.Message}");
                }
            }

            return results;
        }
    }
}
