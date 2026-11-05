using System;
using System.Threading.Tasks;
using UnityEngine;

namespace StremioVR.Providers
{
    /// <summary>
    /// Tracks and synchronizes playback progress across media backends (Jellyfin/Remux, MediaStorm, Silo, local cache).
    /// Ensures Continue Watching queues remain accurate whether watching on Meta Quest, PC, or phone.
    /// Scrobbling to external services (Trakt, Simkl) is handled server-side by upstream media server plugins.
    /// </summary>
    public class WatchProgressSync : MonoBehaviour
    {
        public static WatchProgressSync Instance { get; private set; }

        private string _activeProviderId;
        private string _activeItemId;
        private string _activeTitle;
        private double _activeDurationSeconds;
        private double _lastReportedPosition;
        private float _nextPeriodicReportTime;
        private bool _isPlaying;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (!_isPlaying || string.IsNullOrEmpty(_activeItemId)) return;

            // Report progress every 15 seconds during active playback
            if (Time.unscaledTime >= _nextPeriodicReportTime)
            {
                _nextPeriodicReportTime = Time.unscaledTime + 15f;
                ReportPeriodicProgress();
            }
        }

        public void OnPlaybackStarted(string providerId, string itemId, string title, double positionSeconds, double durationSeconds)
        {
            _activeProviderId = providerId;
            _activeItemId = itemId;
            _activeTitle = title;
            _activeDurationSeconds = durationSeconds;
            _lastReportedPosition = positionSeconds;
            _isPlaying = true;
            _nextPeriodicReportTime = Time.unscaledTime + 15f;

            SaveLocalResumePosition(providerId, itemId, positionSeconds);

            if (providerId == "jellyfin" && MediaProviderRegistry.Instance?.Jellyfin != null)
            {
                ReportJellyfinPlayback("Playing", positionSeconds);
            }
            else if (providerId == "mediastorm" && MediaProviderRegistry.Instance?.MediaStorm != null)
            {
                ReportProviderPlayback("mediastorm", itemId, positionSeconds, false);
            }
            else if (providerId == "silo" && MediaProviderRegistry.Instance?.Silo != null)
            {
                ReportProviderPlayback("silo", itemId, positionSeconds, false);
            }
        }

        public void OnPlaybackPositionUpdated(double positionSeconds)
        {
            _lastReportedPosition = positionSeconds;
        }

        public void OnPlaybackPaused(double positionSeconds)
        {
            _lastReportedPosition = positionSeconds;
            _isPlaying = false;

            SaveLocalResumePosition(_activeProviderId, _activeItemId, positionSeconds);

            if (_activeProviderId == "jellyfin")
            {
                ReportJellyfinPlayback("Progress", positionSeconds, isPaused: true);
            }
            else if (_activeProviderId == "mediastorm" && MediaProviderRegistry.Instance?.MediaStorm != null)
            {
                ReportProviderPlayback("mediastorm", _activeItemId, positionSeconds, true);
            }
            else if (_activeProviderId == "silo" && MediaProviderRegistry.Instance?.Silo != null)
            {
                ReportProviderPlayback("silo", _activeItemId, positionSeconds, true);
            }
        }

        public void OnPlaybackResumed(double positionSeconds)
        {
            _lastReportedPosition = positionSeconds;
            _isPlaying = true;
            _nextPeriodicReportTime = Time.unscaledTime + 15f;

            if (_activeProviderId == "jellyfin")
            {
                ReportJellyfinPlayback("Progress", positionSeconds, isPaused: false);
            }
            else if (_activeProviderId == "mediastorm" && MediaProviderRegistry.Instance?.MediaStorm != null)
            {
                ReportProviderPlayback("mediastorm", _activeItemId, positionSeconds, false);
            }
            else if (_activeProviderId == "silo" && MediaProviderRegistry.Instance?.Silo != null)
            {
                ReportProviderPlayback("silo", _activeItemId, positionSeconds, false);
            }
        }

        public void OnPlaybackStopped(double positionSeconds)
        {
            _isPlaying = false;
            SaveLocalResumePosition(_activeProviderId, _activeItemId, positionSeconds);

            if (_activeProviderId == "jellyfin")
            {
                ReportJellyfinPlayback("Stopped", positionSeconds);
            }
            else if (_activeProviderId == "mediastorm" && MediaProviderRegistry.Instance?.MediaStorm != null)
            {
                ReportProviderPlayback("mediastorm", _activeItemId, positionSeconds, true);
            }
            else if (_activeProviderId == "silo" && MediaProviderRegistry.Instance?.Silo != null)
            {
                ReportProviderPlayback("silo", _activeItemId, positionSeconds, true);
            }

            _activeItemId = null;
        }

        private void ReportPeriodicProgress()
        {
            SaveLocalResumePosition(_activeProviderId, _activeItemId, _lastReportedPosition);

            if (_activeProviderId == "jellyfin")
            {
                ReportJellyfinPlayback("Progress", _lastReportedPosition, isPaused: false);
            }
            else if (_activeProviderId == "mediastorm" && MediaProviderRegistry.Instance?.MediaStorm != null)
            {
                ReportProviderPlayback("mediastorm", _activeItemId, _lastReportedPosition, false);
            }
            else if (_activeProviderId == "silo" && MediaProviderRegistry.Instance?.Silo != null)
            {
                ReportProviderPlayback("silo", _activeItemId, _lastReportedPosition, false);
            }
        }

        private void SaveLocalResumePosition(string providerId, string itemId, double seconds)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            string key = $"resume_{providerId}_{itemId}";
            long ticks = (long)(seconds * TimeSpan.TicksPerSecond);
            PlayerPrefs.SetString(key, ticks.ToString());
            PlayerPrefs.Save();
        }

        private async void ReportProviderPlayback(string providerId, string itemId, double positionSeconds, bool isPaused)
        {
            try
            {
                var registry = MediaProviderRegistry.Instance;
                if (registry == null) return;

                if (providerId == "mediastorm" && registry.MediaStorm != null)
                {
                    await registry.MediaStorm.ReportPlaybackAsync(itemId, positionSeconds, isPaused);
                }
                else if (providerId == "silo" && registry.Silo != null)
                {
                    await registry.Silo.ReportPlaybackAsync(itemId, positionSeconds, isPaused);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WatchProgressSync] {providerId} progress report failed: {ex.Message}");
            }
        }

        public double GetLocalResumePositionSeconds(string providerId, string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            string key = $"resume_{providerId}_{itemId}";
            string val = PlayerPrefs.GetString(key, "0");
            if (long.TryParse(val, out long ticks))
            {
                return (double)ticks / TimeSpan.TicksPerSecond;
            }
            return 0;
        }

        private async void ReportJellyfinPlayback(string state, double positionSeconds, bool isPaused = false)
        {
            var jellyfin = MediaProviderRegistry.Instance?.Jellyfin;
            if (jellyfin == null || !jellyfin.IsAuthenticated) return;

            long ticks = (long)(Math.Max(0, positionSeconds) * TimeSpan.TicksPerSecond);
            string endpoint = $"/Sessions/Playing/{state}";
            if (state == "Playing") endpoint = "/Sessions/Playing";

            string body = JsonUtility.ToJson(new JellyfinProgressPayload
            {
                ItemId = _activeItemId,
                PositionTicks = ticks,
                IsPaused = isPaused
            });

            // Dispatched asynchronously in background
            try
            {
                await jellyfin.ReportPlaybackAsync(endpoint, body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WatchProgressSync] Jellyfin progress report failed: {ex.Message}");
            }
        }

        [Serializable]
        private class JellyfinProgressPayload
        {
            public string ItemId;
            public long PositionTicks;
            public bool IsPaused;
        }
    }
}
