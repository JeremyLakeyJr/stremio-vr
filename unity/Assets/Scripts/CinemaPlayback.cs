using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;
using StremioVR.Providers;

namespace StremioVR {
public sealed class CinemaPlayback : MonoBehaviour {
    DesktopBridge desktop; double desktopTime, desktopDuration; bool desktopPlaying, desktopPrepared;
    public bool IsPlaying => desktop ? desktopPlaying && !Suspended : Video.isPlaying;
    public Texture DesktopTexture => desktop && desktopPrepared && !Suspended ? desktop.Frame : null;
    public RoomClient Room { get; private set; }
    public VideoPlayer Video { get; private set; }
    public event Action<string> Status;
    public string SourceUrl { get; private set; }
    public float Volume { get; private set; } = 1;
    public ushort AudioTrack { get; private set; }
    public CinemaSubtitles Subtitles { get; private set; }
    public int ServerAudioTrackIndex { get; private set; } = 0;
    public string ActiveProviderId { get; private set; }
    public string ActiveItemId { get; private set; }
    public string AudioInfo => desktop ? "Desktop browser audio" : Video.audioTrackCount > 0 ? "Track " + (AudioTrack + 1) + " of " + Video.audioTrackCount : "No alternate audio tracks";
    public double Position => (desktop ? desktopTime : Video.time) + ((double?)playback?["mediaStart"] ?? 0);
    public double Duration => ((string)playback?["url"] ?? "").StartsWith("/media/") ? ((double?)playback?["duration"] ?? 0) : (desktop ? desktopDuration : Video.length);
    public bool Suspended { get; set; }
    Renderer screenRenderer; Vector3 screenSize; JObject playback; float nextSync, nextDiagnostic; bool preparing,soloSeek,wasPaused; double soloPosition;

    public void Initialize(RoomClient room, Renderer screen) {
        Room = room; Room.Message += OnMessage;
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
        desktop = DesktopBridge.Get(); desktop.Message += DesktopMessage;
#endif
        Video = gameObject.AddComponent<VideoPlayer>(); Video.playOnAwake = false; Video.isLooping = false;
        Video.skipOnDrop = true; Video.renderMode = VideoRenderMode.MaterialOverride;
        Video.errorReceived += (source, message) => { preparing = false; Status?.Invoke("Video playback error: " + message); };
        Video.prepareCompleted += source => {
            if(soloSeek){Video.time=Math.Min(soloPosition,Video.length>0?Video.length:soloPosition);soloSeek=false;}
            preparing = false; ApplyAudio(); Apply(); Status?.Invoke("Ready: " + (string)playback?["title"]);
            wasPaused = (bool?)playback?["paused"] ?? false;
            WatchProgressSync.Instance?.OnPlaybackStarted(ActiveProviderId, ActiveItemId, (string)playback?["title"], Position, Duration);
        };
        Subtitles = gameObject.AddComponent<CinemaSubtitles>();
        Subtitles.Initialize(this, screen ? screen.gameObject : null);
        SetScreen(screen);
    }

    void DesktopMessage(JObject data) {
        switch ((string)data["kind"]) {
            case "media-state":
                desktopTime = (double?)data["position"] ?? 0;
                desktopDuration = (double?)data["duration"] ?? 0;
                desktopPrepared = (bool?)data["prepared"] == true;
                desktopPlaying = (bool?)data["playing"] == true;
                break;
            case "media-ended":
                if (Room.IsHost) Control(true, Position);
                break;
        }
    }

    void ApplyDesktop() {
        if (Suspended || playback == null || string.IsNullOrEmpty((string)playback["url"])) return;
        bool paused = (bool)playback["paused"];
        double desired = Room.IsSolo ? (soloSeek ? soloPosition : desktopTime) : (double)playback["position"] + (paused ? 0 : Math.Max(0, Room.ServerTime - (double)playback["updatedAt"]) / 1000) - ((double?)playback["mediaStart"] ?? 0);
        desktop.Rtc("media-sync", new JObject {
            ["url"] = PlaybackUrl((string)playback["url"], Room.ApiEndpoint),
            ["position"] = Math.Max(0, desired),
            ["paused"] = paused,
            ["volume"] = Volume,
            ["solo"] = Room.IsSolo,
            ["seek"] = soloSeek
        });
        soloSeek = false;
    }

    public void SetScreen(Renderer screen) { screenRenderer = screen; screenSize = screen ? screen.transform.localScale : Vector3.one; Subtitles?.SetScreen(screen ? screen.gameObject : null); ApplyScreen(); }
    void ApplyScreen() { if (screenRenderer) { Video.targetMaterialRenderer = screenRenderer; Video.targetMaterialProperty = "_MainTex"; } }

    void OnMessage(JObject message) {
        if ((string)message["type"] == "disconnected") {
            WatchProgressSync.Instance?.OnPlaybackStopped(Position);
            playback = null;
            if(desktop){desktop.Rtc("media-stop");desktopPrepared=desktopPlaying=false;}
            Video.Stop(); preparing = false; return;
        }
        if (message["type"]?.ToString() != "state") return;
        if (!string.IsNullOrEmpty((string)message["mediaStatus"])) Status?.Invoke((string)message["mediaStatus"]);
        var next = message["playback"] as JObject; if (next == null) return;
        bool changed = (string)playback?["url"] != (string)next["url"] || (!Video.isPrepared && !preparing && (long?)playback?["revision"] != (long?)next["revision"]);
        if(Room.IsSolo && (long?)playback?["revision"]!=(long?)next["revision"]){soloSeek=true;soloPosition=(double)next["position"];}
        playback = next;
        if(desktop){if(changed){desktopPrepared=false;desktopTime=(double?)next["position"]??0;}ApplyDesktop();return;}
        if (changed && !string.IsNullOrEmpty((string)next["url"])) {
            Video.Stop(); AudioTrack=0; ConfigureAudio(); Video.url = PlaybackUrl((string)next["url"],Room.ApiEndpoint); preparing = true; Status?.Invoke("Preparing video."); Video.Prepare();
            if (next["subtitles"] != null && !string.IsNullOrEmpty((string)next["subtitles"])) {
                Subtitles?.LoadSubtitles((string)next["subtitles"], Room.ApiEndpoint);
            } else {
                Subtitles?.Clear();
            }
        } else if (string.IsNullOrEmpty((string)next["url"])) {
            WatchProgressSync.Instance?.OnPlaybackStopped(Position);
            Video.Stop(); Subtitles?.Clear();
        } else Apply();
    }

    public static string PlaybackUrl(string value,string endpoint){
        if(value.StartsWith("/media/"))return endpoint.TrimEnd('/')+value;
        if(string.IsNullOrEmpty(endpoint))throw new Exception("Solo playback needs a direct HTTP(S) video URL");
        return MediaLibrary.Http(value);
    }

    void Update() {
        if (desktop) {
            if (DesktopTexture && screenRenderer) {
                screenRenderer.sharedMaterial.mainTexture = DesktopTexture;
                screenRenderer.sharedMaterial.mainTextureScale = new Vector2(1, -1);
                screenRenderer.sharedMaterial.mainTextureOffset = new Vector2(0, 1);
            }
            if (Time.unscaledTime >= nextSync) {
                nextSync = Time.unscaledTime + 0.5f;
                ApplyDesktop();
            }
            return;
        }
        if (Suspended || playback == null || preparing || !Video.isPrepared) return;
        bool paused = (bool)playback["paused"];
        if (paused != wasPaused) {
            wasPaused = paused;
            if (paused) WatchProgressSync.Instance?.OnPlaybackPaused(Position);
            else WatchProgressSync.Instance?.OnPlaybackResumed(Position);
        }
        WatchProgressSync.Instance?.OnPlaybackPositionUpdated(Position);
        double desired = (double)playback["position"] + (paused ? 0 : Math.Max(0, Room.ServerTime - (double)playback["updatedAt"]) / 1000);
        desired = Math.Max(0, desired - ((double?)playback["mediaStart"] ?? 0));
        if (Video.length > 0 && !((string)playback["url"]).StartsWith("/media/")) desired = Math.Min(desired, Video.length);
        double delta = desired - Video.time;
        if (paused) { if (Video.isPlaying) Video.Pause(); if (Math.Abs(delta) > 0.3) Video.time = desired; return; }
        if (!Video.isPlaying) Video.Play();
        if (Math.Abs(delta) > 2.5) {
            Video.time = desired;
        } else if (Math.Abs(delta) > 0.6 && Time.unscaledTime > nextSync) {
            Video.time = desired;
            nextSync = Time.unscaledTime + 4.0f;
        }
        if (Video.canSetPlaybackSpeed) Video.playbackSpeed = 1;
        if (screenRenderer && Video.texture && (Video.texture.width != (int)screenSize.x || Video.texture.height != (int)screenSize.y)) {
            float aspect = (float)Video.texture.width / Mathf.Max(1, Video.texture.height);
            screenRenderer.transform.localScale = new Vector3(screenSize.y * aspect, screenSize.y, screenSize.z);
        }
        if (Time.unscaledTime > nextDiagnostic && !Video.isPlaying && !paused) {
            nextDiagnostic = Time.unscaledTime + 10;
            Status?.Invoke("Buffering video stream.");
        }
    }

    void Apply() {
        if (desktop) { ApplyDesktop(); return; }
        if (playback == null || !Video.isPrepared) return;
        if ((bool)playback["paused"] && Video.isPlaying) Video.Pause();
        if (!(bool)playback["paused"] && !Video.isPlaying) Video.Play();
    }

    void ConfigureAudio() {
        Video.audioOutputMode = VideoAudioOutputMode.Direct;
        ushort tracks = (ushort)Math.Min(Video.audioTrackCount, VideoPlayer.controlledAudioTrackMaxCount);
        for (ushort i = 0; i < tracks; i++) Video.EnableAudioTrack(i, i == AudioTrack);
    }

    void ApplyAudio() {
        ushort tracks = (ushort)Math.Min(Video.audioTrackCount, VideoPlayer.controlledAudioTrackMaxCount);
        for (ushort i = 0; i < tracks; i++) if (Video.GetAudioChannelCount(i) > 0) Video.SetDirectAudioVolume(i, i == AudioTrack ? Volume : 0);
    }

    public void SetVolume(float value) { Volume = Mathf.Clamp01(value); if (desktop) { ApplyDesktop(); return; } if (Video.isPrepared) ApplyAudio(); }

    public void NextAudioTrack() {
        if (desktop) { desktop.Rtc("media-track"); return; }
        if (!Video.isPrepared || Video.audioTrackCount < 2) { Status?.Invoke(AudioInfo); return; }
        ushort count = (ushort)Math.Min(Video.audioTrackCount, VideoPlayer.controlledAudioTrackMaxCount);
        if (Room.IsSolo) { soloPosition = Video.time; soloSeek = true; }
        AudioTrack = (ushort)((AudioTrack + 1) % count); Video.Stop(); ConfigureAudio(); preparing = true; Video.Prepare();
    }

    public void SelectAudioTrack(ushort trackIndex) {
        if (!Video.isPrepared) return;
        ushort count = (ushort)Math.Min(Video.audioTrackCount, VideoPlayer.controlledAudioTrackMaxCount);
        if (trackIndex < count) {
            AudioTrack = trackIndex;
            ConfigureAudio();
            ApplyAudio();
            Status?.Invoke("Selected local audio track " + (AudioTrack + 1));
        }
    }

    public void SelectServerAudioTrack(int trackIndex) {
        if (!Room.IsHost || playback == null) return;
        ServerAudioTrackIndex = trackIndex;
        string url = SourceUrl ?? (string)playback["url"];
        string title = (string)playback["title"] ?? "Stream";
        double pos = Position;
        Room.Send(new JObject {
            ["type"] = "playback",
            ["url"] = url,
            ["title"] = title,
            ["position"] = pos,
            ["audioTrackIndex"] = trackIndex
        });
        Status?.Invoke("Requested server audio track " + (trackIndex + 1));
    }

    public void NextServerAudioTrack() => SelectServerAudioTrack((ServerAudioTrackIndex + 1) % 8);

    public void Load(string url, string title, double position=0) {
        ActiveProviderId = "direct"; ActiveItemId = url;
        if (!Room.IsHost) { Status?.Invoke("Create a room or ask the host to choose media"); return; }
        try { SourceUrl=MediaLibrary.Http(url); Room.Send(new JObject { ["type"] = "playback", ["url"] = SourceUrl, ["title"] = title, ["position"] = Math.Max(0,position) }); } catch (Exception error) { Status?.Invoke(error.Message); }
    }

    public void Load(ResolvedStream stream, string title, double position=0) {
        ActiveProviderId = stream?.ProviderId; ActiveItemId = stream?.ItemId;
        if (stream?.SubtitleTracks != null && stream.SubtitleTracks.Count > 0) {
            Subtitles?.SetAvailableTracks(stream.SubtitleTracks);
        }
        if (!Room.IsHost) { Status?.Invoke("Create a room or ask the host to choose media"); return; }
        if (stream == null || string.IsNullOrEmpty(stream.StreamUrl)) { Status?.Invoke("Invalid stream selection"); return; }
        try {
            SourceUrl = MediaLibrary.Http(stream.StreamUrl);
            var payload = new JObject {
                ["type"] = "playback",
                ["url"] = SourceUrl,
                ["title"] = title,
                ["position"] = Math.Max(0, position),
                ["sourceCodec"] = stream.SourceCodec
            };
            if (stream.AudioTrackIndex.HasValue) {
                payload["audioTrackIndex"] = stream.AudioTrackIndex.Value;
            }
            if (!string.IsNullOrEmpty(stream.SubtitleUrl)) {
                payload["subtitles"] = stream.SubtitleUrl;
            }
            if (stream.Headers != null && stream.Headers.Count > 0) {
                var headersObj = new JObject();
                foreach (var kv in stream.Headers) headersObj[kv.Key] = kv.Value;
                payload["upstreamHeaders"] = headersObj;
            }
            Room.Send(payload);
        } catch (Exception error) { Status?.Invoke(error.Message); }
    }

    public void Toggle() { if (playback != null) Control(!(bool)playback["paused"], Position); }
    public void Seek(double delta) { if (playback != null) Control((bool)playback["paused"], Math.Max(0, Position + delta), true); }
    void Control(bool paused, double position, bool seek=false) {
        if (!Room.IsHost) { Status?.Invoke("Only the host controls playback"); return; }
        Room.Send(new JObject { ["type"] = "playback", ["paused"] = paused, ["position"] = position, ["seek"] = seek });
    }

    void OnDestroy() {
        WatchProgressSync.Instance?.OnPlaybackStopped(Position);
        if (Room != null) Room.Message -= OnMessage;
        if (desktop) desktop.Message -= DesktopMessage;
    }
}
}
