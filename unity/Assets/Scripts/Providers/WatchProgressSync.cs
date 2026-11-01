using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR.Providers
{
    /// <summary>
    /// Tracks and synchronizes playback progress across media backends (Jellyfin/Remux, Trakt, local cache).
    /// Ensures Continue Watching queues remain accurate whether watching on Meta Quest, PC, or phone.
    /// </summary>
    public class WatchProgressSync : MonoBehaviour
    {
        public static WatchProgressSync Instance { get; private set; }

        public string TraktClientId;
        public string TraktAccessToken;

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
                _ = MediaProviderRegistry.Instance.MediaStorm.ReportPlaybackAsync(itemId, positionSeconds, false);
            }

            if (IsTraktConfigured())
            {
                ReportTraktScrobble("start", positionSeconds, durationSeconds);
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
                _ = MediaProviderRegistry.Instance.MediaStorm.ReportPlaybackAsync(_activeItemId, positionSeconds, true);
            }

            if (IsTraktConfigured())
            {
                ReportTraktScrobble("pause", positionSeconds, _activeDurationSeconds);
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
                _ = MediaProviderRegistry.Instance.MediaStorm.ReportPlaybackAsync(_activeItemId, positionSeconds, false);
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
                _ = MediaProviderRegistry.Instance.MediaStorm.ReportPlaybackAsync(_activeItemId, positionSeconds, true);
            }

            if (IsTraktConfigured())
            {
                ReportTraktScrobble("stop", positionSeconds, _activeDurationSeconds);
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
                _ = MediaProviderRegistry.Instance.MediaStorm.ReportPlaybackAsync(_activeItemId, _lastReportedPosition, false);
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

        private bool IsTraktConfigured() =>
            !string.IsNullOrEmpty(TraktClientId) && !string.IsNullOrEmpty(TraktAccessToken);

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
            catch {}
        }

        private async void ReportTraktScrobble(string action, double positionSeconds, double durationSeconds)
        {
            if (!IsTraktConfigured() || durationSeconds <= 0) return;

            float progressPercent = Mathf.Clamp((float)(positionSeconds / durationSeconds) * 100f, 0f, 100f);
            string url = $"https://api.trakt.tv/scrobble/{action}";

            string body = JsonUtility.ToJson(new TraktScrobblePayload
            {
                progress = progressPercent,
                movie = new TraktMovieRef { title = _activeTitle }
            });

            try
            {
                using var req = new UnityWebRequest(url, "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("trakt-api-version", "2");
                req.SetRequestHeader("trakt-api-key", TraktClientId);
                req.SetRequestHeader("Authorization", $"Bearer {TraktAccessToken}");
                req.timeout = 10;

                var tcs = new TaskCompletionSource<bool>();
                var op = req.SendWebRequest();
                op.completed += _ => tcs.SetResult(true);
                await tcs.Task;
            }
            catch {}
        }

        [Serializable]
        private class JellyfinProgressPayload
        {
            public string ItemId;
            public long PositionTicks;
            public bool IsPaused;
        }

        [Serializable]
        private class TraktScrobblePayload
        {
            public float progress;
            public TraktMovieRef movie;
        }

        [Serializable]
        private class TraktMovieRef
        {
            public string title;
        }
    }
}
