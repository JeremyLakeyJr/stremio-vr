using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;

namespace StremioVR {
public sealed class CinemaPlayback : MonoBehaviour {
    public RoomClient Room;
    public bool Suspended;
    public VideoPlayer Video;
    public event Action<string> Status;
    Vector3 screenSize; JObject playback; float nextSync, nextDiagnostic; bool preparing;
    public void Initialize(RoomClient room, Renderer screen) {
        Room = room;
        Video = gameObject.AddComponent<VideoPlayer>();
        var audio = gameObject.AddComponent<AudioSource>(); audio.spatialBlend = 0;
        Video.playOnAwake = false; Video.source = VideoSource.Url;
        Video.renderMode = VideoRenderMode.MaterialOverride; Video.targetMaterialRenderer = screen; Video.targetMaterialProperty = "_MainTex";
        Video.audioOutputMode = VideoAudioOutputMode.AudioSource; Video.EnableAudioTrack(0, true); Video.SetTargetAudioSource(0, audio);
        screenSize=screen.transform.localScale;
        Video.prepareCompleted += player => {
            if(player.width>0 && player.height>0) { float ratio=(float)player.width/player.height; float width=Mathf.Min(screenSize.x,screenSize.y*ratio); Video.targetMaterialRenderer.transform.localScale=new Vector3(width,width/ratio,screenSize.z); }
            preparing = false; Apply(); Status?.Invoke("Ready: " + (string)playback?["title"]); };
        Video.errorReceived += (player, error) => { preparing = false; Status?.Invoke("Video failed: " + error + ". Try another source or supported codec."); };
        Video.loopPointReached += player => { if (Room.IsHost) Control(true, player.time); };
        Room.Message += OnMessage;
    }
    public void SetScreen(Renderer screen) { Video.targetMaterialRenderer=screen; screenSize=screen.transform.localScale; if(Video.isPrepared && Video.width>0 && Video.height>0) { float ratio=(float)Video.width/Video.height; float width=Mathf.Min(screenSize.x,screenSize.y*ratio); screen.transform.localScale=new Vector3(width,width/ratio,1); } }
    void OnMessage(JObject message) {
        if ((string)message["type"] == "disconnected") { playback = null; Video.Stop(); preparing = false; return; }
        if ((string)message["type"] != "state") return;
        var next = message["playback"] as JObject; if (next == null) return;
        bool changed = (string)playback?["url"] != (string)next["url"] || (!Video.isPrepared && !preparing && (long?)playback?["revision"] != (long?)next["revision"]);
        playback = next;
        if (changed && !string.IsNullOrEmpty((string)next["url"])) {
            Video.Stop(); Video.url = (string)next["url"]; preparing = true; Status?.Invoke("Preparing video…"); Video.Prepare();
        } else Apply();
    }
    void Apply() {
        if (Suspended || playback == null || preparing || !Video.isPrepared) return;
        bool paused = (bool)playback["paused"];
        double desired = (double)playback["position"] + (paused ? 0 : Math.Max(0, Room.ServerTime - (double)playback["updatedAt"]) / 1000);
        if (Video.length > 0) desired = Math.Min(desired, Video.length);
        double drift = desired - Video.time;
        if (Debug.isDebugBuild && Time.unscaledTime >= nextDiagnostic) {
            nextDiagnostic = Time.unscaledTime + 5;
            Debug.Log($"[CinemaSync] target={desired:F2} actual={Video.time:F2} drift={drift:F2} paused={paused} playing={Video.isPlaying}");
        }
        if (Video.canSetTime && Math.Abs(drift) > (paused ? 0.1 : 0.75)) Video.time = desired;
        if (Video.canSetPlaybackSpeed) Video.playbackSpeed = paused || Math.Abs(drift) < 0.15 ? 1 : (float)Math.Max(0.97, Math.Min(1.03, 1 + drift * 0.03));
        if (paused) Video.Pause(); else if (!Video.isPlaying) Video.Play();
    }
    void Update() { if (Time.unscaledTime >= nextSync) { nextSync = Time.unscaledTime + 0.5f; Apply(); } }
    public void Load(string url, string title, double position=0) {
        if (!Room.IsHost) { Status?.Invoke("Create a room or ask the host to choose media"); return; }
        try { Room.Send(new JObject { ["type"] = "playback", ["url"] = MediaLibrary.Http(url), ["title"] = title, ["position"] = Math.Max(0,position) }); } catch (Exception error) { Status?.Invoke(error.Message); }
    }
    public void Toggle() { if (playback != null) Control(!(bool)playback["paused"], Video.time); }
    public void Seek(double delta) { if (playback != null) Control((bool)playback["paused"], Math.Max(0, Video.time + delta)); }
    void Control(bool paused, double position) {
        if (!Room.IsHost) { Status?.Invoke("Only the host controls playback"); return; }
        Room.Send(new JObject { ["type"] = "playback", ["paused"] = paused, ["position"] = position });
    }
    void OnDestroy() { if (Room != null) Room.Message -= OnMessage; }
}
}
