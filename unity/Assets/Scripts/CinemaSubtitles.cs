using StremioVR.Providers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace StremioVR
{
    public class SubtitleCue
    {
        public double StartSeconds;
        public double EndSeconds;
        public string Text;
    }

    public class CinemaSubtitles : MonoBehaviour
    {
        public bool Enabled { get; set; } = true;
        public double OffsetSeconds { get; set; } = 0;
        public string CurrentSubtitleUrl { get; private set; }
        public int CueCount => _cues.Count;
        public List<SubtitleTrackInfo> AvailableTracks { get; private set; } = new List<SubtitleTrackInfo>();
        public int ActiveTrackIndex { get; private set; } = -1;

        private CinemaPlayback _playback;
        private GameObject _screen;
        private TextMesh _textMesh;
        private GameObject _textObj;
        private readonly List<SubtitleCue> _cues = new List<SubtitleCue>();
        private int _lastCueIndex = -1;

        public void Initialize(CinemaPlayback playback, GameObject screen)
        {
            _playback = playback;
            _screen = screen;
            CreateSubtitleRenderer();
        }

        public void SetScreen(GameObject screen)
        {
            _screen = screen;
            if (_textObj != null && _screen != null)
            {
                _textObj.transform.SetParent(_screen.transform, false);
                _textObj.transform.localPosition = new Vector3(0, -0.42f, -0.05f);
            }
        }

        private void CreateSubtitleRenderer()
        {
            if (_textObj != null) Destroy(_textObj);
            if (_screen == null) return;

            _textObj = new GameObject("ScreenSubtitles", typeof(TextMesh));
            _textObj.transform.SetParent(_screen.transform, false);
            _textObj.transform.localPosition = new Vector3(0, -0.42f, -0.05f);
            _textObj.transform.localRotation = Quaternion.identity;
            _textObj.transform.localScale = new Vector3(0.015f, 0.015f, 0.015f);

            _textMesh = _textObj.GetComponent<TextMesh>();
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.anchor = TextAnchor.LowerCenter;
            _textMesh.fontSize = 48;
            _textMesh.color = Color.yellow;
            _textMesh.text = "";
        }

        public async void LoadSubtitles(string subUrl, string apiEndpoint = "")
        {
            if (string.IsNullOrEmpty(subUrl))
            {
                Clear();
                return;
            }

            CurrentSubtitleUrl = subUrl;
            _cues.Clear();

            string fetchUrl = subUrl;
            if (!string.IsNullOrEmpty(apiEndpoint) && !subUrl.StartsWith("http://localhost") && !subUrl.StartsWith("http://127.0.0.1"))
            {
                string encoded = UnityWebRequest.EscapeURL(subUrl);
                fetchUrl = $"{apiEndpoint.TrimEnd('/')}/api/subtitles?url={encoded}&offset={OffsetSeconds}";
            }

            try
            {
                using var req = UnityWebRequest.Get(fetchUrl);
                req.timeout = 10;
                var tcs = new TaskCompletionSource<bool>();
                req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
                await tcs.Task;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    ParseWebVTT(req.downloadHandler.text);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CinemaSubtitles] Failed to fetch subtitles: {ex.Message}");
            }
        }

        public void AdjustOffset(double deltaSeconds)
        {
            OffsetSeconds += deltaSeconds;
        }

        public void ResetOffset()
        {
            OffsetSeconds = 0;
        }

        public void Toggle()
        {
            Enabled = !Enabled;
            if (!Enabled && _textMesh != null)
            {
                _textMesh.text = "";
            }
        }

        public void SetAvailableTracks(List<SubtitleTrackInfo> tracks)
        {
            AvailableTracks = tracks ?? new List<SubtitleTrackInfo>();
            ActiveTrackIndex = -1;
            if (AvailableTracks.Count > 0 && !string.IsNullOrEmpty(CurrentSubtitleUrl))
            {
                for (int i = 0; i < AvailableTracks.Count; i++)
                {
                    if (AvailableTracks[i].Url == CurrentSubtitleUrl) { ActiveTrackIndex = i; break; }
                }
            }
        }

        public void SelectTrack(int index, string apiEndpoint = "")
        {
            if (index < 0 || index >= AvailableTracks.Count)
            {
                ActiveTrackIndex = -1;
                Clear();
                return;
            }
            ActiveTrackIndex = index;
            Enabled = true;
            LoadSubtitles(AvailableTracks[index].Url, apiEndpoint);
        }

        public void Clear()
        {
            _cues.Clear();
            CurrentSubtitleUrl = null;
            if (_textMesh != null) _textMesh.text = "";
        }

        private void Update()
        {
            if (!Enabled || _textMesh == null || _playback == null || _cues.Count == 0)
            {
                if (_textMesh != null && _textMesh.text != "") _textMesh.text = "";
                return;
            }

            double currentTime = _playback.Position + OffsetSeconds;

            if (_lastCueIndex >= 0 && _lastCueIndex < _cues.Count)
            {
                var current = _cues[_lastCueIndex];
                if (currentTime >= current.StartSeconds && currentTime <= current.EndSeconds)
                {
                    return;
                }
            }

            int found = -1;
            for (int i = 0; i < _cues.Count; i++)
            {
                if (currentTime >= _cues[i].StartSeconds && currentTime <= _cues[i].EndSeconds)
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                _lastCueIndex = found;
                _textMesh.text = _cues[found].Text;
            }
            else
            {
                _lastCueIndex = -1;
                _textMesh.text = "";
            }
        }

        private void ParseWebVTT(string vttContent)
        {
            _cues.Clear();
            if (string.IsNullOrEmpty(vttContent)) return;

            string[] lines = vttContent.Replace("\r\n", "\n").Split('\n');
            var cueRegex = new Regex(@"(\d{2}:\d{2}:\d{2}[\.,]\d{3}|\d{2}:\d{2}[\.,]\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}[\.,]\d{3}|\d{2}:\d{2}[\.,]\d{3})");

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                var match = cueRegex.Match(line);
                if (match.Success)
                {
                    double start = ParseTimestamp(match.Groups[1].Value);
                    double end = ParseTimestamp(match.Groups[2].Value);

                    string text = "";
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string textLine = lines[j].Trim();
                        if (string.IsNullOrEmpty(textLine)) { i = j; break; }
                        text += (text.Length > 0 ? "\n" : "") + StripTags(textLine);
                    }

                    if (end > start && !string.IsNullOrEmpty(text))
                    {
                        _cues.Add(new SubtitleCue { StartSeconds = start, EndSeconds = end, Text = text });
                    }
                }
            }
        }

        private static double ParseTimestamp(string ts)
        {
            ts = ts.Replace(',', '.');
            string[] parts = ts.Split(':');
            if (parts.Length == 3)
            {
                return double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) * 3600 + double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * 60 + double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            }
            if (parts.Length == 2)
            {
                return double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) * 60 + double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            return 0;
        }

        private static string StripTags(string input)
        {
            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        private void OnDestroy()
        {
            if (_textObj != null) Destroy(_textObj);
        }
    }
}
