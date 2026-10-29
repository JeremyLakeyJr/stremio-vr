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
    public ushort AudioTrack { get; private set; }
    public float Volume { get; private set; }=1; public string AudioInfo {get; private set;}="No video prepared";
    Renderer screenRenderer; Vector3 screenSize; JObject playback; float nextSync, nextDiagnostic; bool preparing;
    public void Initialize(RoomClient room, Renderer screen) {
        Room = room;
        Video = gameObject.AddComponent<VideoPlayer>();
        screenRenderer=screen;
        Video.playOnAwake = false; Video.source = VideoSource.Url;
        Video.renderMode = VideoRenderMode.APIOnly; Video.targetMaterialRenderer = screen; Video.targetMaterialProperty = "_MainTex";
        Video.audioOutputMode = VideoAudioOutputMode.Direct; ConfigureAudio();
        screenSize=screen.transform.localScale;
        Video.prepareCompleted += player => {
            if(player.width>0 && player.height>0) { float ratio=(float)player.width/player.height; float width=Mathf.Min(screenSize.x,screenSize.y*ratio); Video.targetMaterialRenderer.transform.localScale=new Vector3(width,width/ratio,screenSize.z); }
            preparing = false; ApplyAudio(); Apply(); Status?.Invoke("Ready: " + (string)playback?["title"]); };
        Video.errorReceived += (player, error) => { preparing = false; Status?.Invoke("Video failed: " + error + ". Try another source or supported codec."); };
        Video.loopPointReached += player => { if (Room.IsHost) Control(true, player.time); };
        Room.Message += OnMessage;
    }
    public void SetScreen(Renderer screen) { screenRenderer=screen; Video.targetMaterialRenderer=screen; screenSize=screen.transform.localScale; if(Video.isPrepared && Video.width>0 && Video.height>0) { float ratio=(float)Video.width/Video.height; float width=Mathf.Min(screenSize.x,screenSize.y*ratio); screen.transform.localScale=new Vector3(width,width/ratio,1); } }
    void OnMessage(JObject message) {
        if ((string)message["type"] == "disconnected") { playback = null; Video.Stop(); preparing = false; return; }
        if ((string)message["type"] != "state") return;
        var next = message["playback"] as JObject; if (next == null) return;
        bool changed = (string)playback?["url"] != (string)next["url"] || (!Video.isPrepared && !preparing && (long?)playback?["revision"] != (long?)next["revision"]);
        playback = next;
        if (changed && !string.IsNullOrEmpty((string)next["url"])) {
            Video.Stop(); AudioTrack=0; ConfigureAudio(); Video.url = (string)next["url"]; preparing = true; Status?.Invoke("Preparing video…"); Video.Prepare();
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
        // Android decoders can mute or distort audio during rate changes. Correct large drift by seeking.
        if (Video.canSetPlaybackSpeed) Video.playbackSpeed = 1;
        if (paused) Video.Pause(); else if (!Video.isPlaying) Video.Play();
    }
    void Update() { if(!Suspended && Video.isPrepared && Video.texture && screenRenderer) screenRenderer.sharedMaterial.mainTexture=Video.texture; if (Time.unscaledTime >= nextSync) { nextSync = Time.unscaledTime + 0.5f; Apply(); } }
    void ConfigureAudio() {
        Video.controlledAudioTrackCount=(ushort)(AudioTrack+1);
        for(ushort i=0;i<Video.controlledAudioTrackCount;i++) Video.EnableAudioTrack(i,i==AudioTrack);
    }
    void ApplyAudio() {
        if(Video.audioTrackCount==0) { AudioInfo="No decodable audio track reported. Try an AAC source or a transcoding service."; Status?.Invoke(AudioInfo); return; }
        if(AudioTrack>=Video.audioTrackCount) AudioTrack=0;
        Video.SetDirectAudioMute(AudioTrack,Volume<=0.001f); if(Video.canSetDirectAudioVolume) Video.SetDirectAudioVolume(AudioTrack,Volume);
        AudioInfo="Track "+(AudioTrack+1)+" / "+Video.audioTrackCount+" · "+Video.GetAudioLanguageCode(AudioTrack)+" · "+Video.GetAudioChannelCount(AudioTrack)+" channels";
        Debug.Log("[MovieAudio] "+AudioInfo+" direct output, speed=1");
    }
    public void SetVolume(float value) { Volume=Mathf.Clamp01(value); if(Video.isPrepared) ApplyAudio(); }
    public void NextAudioTrack() {
        if(!Video.isPrepared || Video.audioTrackCount<2) { Status?.Invoke(AudioInfo);return; }
        ushort count=(ushort)Math.Min(Video.audioTrackCount,VideoPlayer.controlledAudioTrackMaxCount);
        AudioTrack=(ushort)((AudioTrack+1)%count); Video.Stop(); ConfigureAudio(); preparing=true;Video.Prepare();
    }
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
