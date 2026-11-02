using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;

namespace StremioVR {
public sealed class CinemaPlayback : MonoBehaviour {
    public RoomClient Room;
    public bool Suspended;
    DesktopBridge desktop;double desktopTime,desktopDuration;bool desktopPlaying,desktopPrepared;
    public bool IsPlaying=>desktop?desktopPlaying&&!Suspended:Video.isPlaying;
    public Texture DesktopTexture=>desktop&&desktopPrepared&&!Suspended?desktop.Frame:null;
    public VideoPlayer Video;
    public event Action<string> Status;
    public ushort AudioTrack { get; private set; }
    public float Volume { get; private set; }=1; public string AudioInfo {get; private set;}="No video prepared";
    public double Position => (desktop?desktopTime:Video.time) + ((double?)playback?["mediaStart"] ?? 0);
    public double Duration => ((string)playback?["url"] ?? "").StartsWith("/media/") ? ((double?)playback?["duration"] ?? 0) : desktop?desktopDuration:Video.length;
    public string SourceUrl { get; private set; }
    Renderer screenRenderer; Vector3 screenSize; JObject playback; float nextSync, nextDiagnostic; bool preparing,soloSeek; double soloPosition;
    public void Initialize(RoomClient room, Renderer screen) {
        Room = room;
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
        desktop=DesktopBridge.Get();desktop.Message+=DesktopMessage;
#endif
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
        Video.loopPointReached += player => { if (Room.IsHost) Control(true, Position); };
        Room.Message += OnMessage;
    }
    void DesktopMessage(JObject data){switch((string)data["kind"]){case "media-state":desktopTime=(double?)data["position"]??0;desktopDuration=(double?)data["duration"]??0;desktopPrepared=(bool?)data["prepared"]==true;desktopPlaying=(bool?)data["playing"]==true;AudioInfo="Desktop browser audio";break;case "media-ended":if(Room.IsHost)Control(true,Position);break;}}
    void ApplyDesktop(){if(Suspended||playback==null||string.IsNullOrEmpty((string)playback["url"]))return;bool paused=(bool)playback["paused"];double desired=Room.IsSolo?(soloSeek?soloPosition:desktopTime):(double)playback["position"]+(paused?0:Math.Max(0,Room.ServerTime-(double)playback["updatedAt"])/1000)-((double?)playback["mediaStart"]??0);desktop.Rtc("media-sync",new JObject{["url"]=PlaybackUrl((string)playback["url"],Room.ApiEndpoint),["position"]=Math.Max(0,desired),["paused"]=paused,["volume"]=Volume,["solo"]=Room.IsSolo,["seek"]=soloSeek});soloSeek=false;}
    public void SetScreen(Renderer screen) { screenRenderer=screen; Video.targetMaterialRenderer=screen; screenSize=screen.transform.localScale; if(Video.isPrepared && Video.width>0 && Video.height>0) { float ratio=(float)Video.width/Video.height; float width=Mathf.Min(screenSize.x,screenSize.y*ratio); screen.transform.localScale=new Vector3(width,width/ratio,1); } }
    void OnMessage(JObject message) {
        if ((string)message["type"] == "disconnected") { playback = null; if(desktop){desktop.Rtc("media-stop");desktopPrepared=desktopPlaying=false;}Video.Stop(); preparing = false; return; }
        if ((string)message["type"] != "state") return;
        if(!string.IsNullOrEmpty((string)message["mediaStatus"])) Status?.Invoke((string)message["mediaStatus"]);
        var next = message["playback"] as JObject; if (next == null) return;
        bool changed = (string)playback?["url"] != (string)next["url"] || (!Video.isPrepared && !preparing && (long?)playback?["revision"] != (long?)next["revision"]);
        if(Room.IsSolo && (long?)playback?["revision"]!=(long?)next["revision"]){soloSeek=true;soloPosition=(double)next["position"];}
        playback = next;
        if(desktop){if(changed){desktopPrepared=false;desktopTime=(double?)next["position"]??0;}ApplyDesktop();return;}
        if (changed && !string.IsNullOrEmpty((string)next["url"])) {
            Video.Stop(); AudioTrack=0; ConfigureAudio(); Video.url = PlaybackUrl((string)next["url"],Room.ApiEndpoint); preparing = true; Status?.Invoke("Preparing video…"); Video.Prepare();
        } else Apply();
    }
    public static string PlaybackUrl(string value,string endpoint){
        if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&& (uri.Scheme=="http"||uri.Scheme=="https"))return MediaLibrary.Http(value);
        if(string.IsNullOrEmpty(endpoint))throw new Exception("Solo playback needs a direct HTTP(S) video URL");
        return new Uri(new Uri(endpoint),value).AbsoluteUri;
    }
    void Apply() {
        if(desktop){ApplyDesktop();return;}
        if (Suspended || playback == null || preparing || !Video.isPrepared) return;
        bool paused = (bool)playback["paused"];
        if(Room.IsSolo){if(soloSeek&&Video.canSetTime){Video.time=soloPosition;soloSeek=false;}if(paused)Video.Pause();else if(!Video.isPlaying)Video.Play();return;}
        double desired = (double)playback["position"] + (paused ? 0 : Math.Max(0, Room.ServerTime - (double)playback["updatedAt"]) / 1000);
        desired = Math.Max(0, desired - ((double?)playback["mediaStart"] ?? 0));
        if (Video.length > 0 && !((string)playback["url"]).StartsWith("/media/")) desired = Math.Min(desired, Video.length);
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
    void Update() { if(desktop){if(DesktopTexture&&screenRenderer){screenRenderer.sharedMaterial.mainTexture=DesktopTexture;screenRenderer.sharedMaterial.mainTextureScale=new Vector2(1,-1);screenRenderer.sharedMaterial.mainTextureOffset=new Vector2(0,1);}if(Time.unscaledTime>=nextSync){nextSync=Time.unscaledTime+.5f;ApplyDesktop();}return;}if(!Suspended && Video.isPrepared && Video.texture && screenRenderer) screenRenderer.sharedMaterial.mainTexture=Video.texture; if (Time.unscaledTime >= nextSync) { nextSync = Time.unscaledTime + 0.5f; Apply(); } }
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
    public void SetVolume(float value) { Volume=Mathf.Clamp01(value);if(desktop){ApplyDesktop();return;} if(Video.isPrepared) ApplyAudio(); }
    public void NextAudioTrack() {
        if(desktop){desktop.Rtc("media-track");return;}
        if(!Video.isPrepared || Video.audioTrackCount<2) { Status?.Invoke(AudioInfo);return; }
        ushort count=(ushort)Math.Min(Video.audioTrackCount,VideoPlayer.controlledAudioTrackMaxCount);
        if(Room.IsSolo){soloPosition=Video.time;soloSeek=true;}
        AudioTrack=(ushort)((AudioTrack+1)%count); Video.Stop(); ConfigureAudio(); preparing=true;Video.Prepare();
    }
    public void Load(string url, string title, double position=0) {
        if (!Room.IsHost) { Status?.Invoke("Create a room or ask the host to choose media"); return; }
        try { SourceUrl=MediaLibrary.Http(url); Room.Send(new JObject { ["type"] = "playback", ["url"] = SourceUrl, ["title"] = title, ["position"] = Math.Max(0,position) }); } catch (Exception error) { Status?.Invoke(error.Message); }
    }
    public void Toggle() { if (playback != null) Control(!(bool)playback["paused"], Position); }
    public void Seek(double delta) { if (playback != null) Control((bool)playback["paused"], Math.Max(0, Position + delta), true); }
    void Control(bool paused, double position, bool seek=false) {
        if (!Room.IsHost) { Status?.Invoke("Only the host controls playback"); return; }
        Room.Send(new JObject { ["type"] = "playback", ["paused"] = paused, ["position"] = position, ["seek"] = seek });
    }
    void OnDestroy() { if (Room != null) Room.Message -= OnMessage;if(desktop)desktop.Message-=DesktopMessage; }
}
}
