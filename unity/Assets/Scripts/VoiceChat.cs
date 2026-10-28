using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StremioVR {
// Small-room MVP: 16 kHz mono PCM via room-scoped WebSocket, 100 ms per frame.
public sealed class VoiceChat : MonoBehaviour {
    public RoomClient Room;
    public event Action<string> Status;
    public bool Enabled { get; private set; }
    public bool Deafened { get; private set; }
    AudioClip microphone; string device; int read; float nextFrame; bool starting;
    readonly Dictionary<string, Speaker> speakers = new Dictionary<string, Speaker>();
    public void Initialize(RoomClient room) { Room = room; room.Message += OnMessage; }
    public void Toggle() { if (Enabled) StopVoice(); else if (!starting) StartCoroutine(StartVoice()); }
    IEnumerator StartVoice() {
        if (!Room.Connected || Room.State == null) { Status?.Invoke("Join a room first"); yield break; }
        starting = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            float deadline = Time.realtimeSinceStartup + 20;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) { starting = false; Status?.Invoke("Microphone permission denied"); yield break; }
        }
#endif
        if (Microphone.devices.Length == 0) { starting = false; Status?.Invoke("No microphone available"); yield break; }
        device = Microphone.devices[0]; microphone = Microphone.Start(device, true, 1, 16000);
        float timeout = Time.realtimeSinceStartup + 5;
        while (Microphone.GetPosition(device) <= 0 && Time.realtimeSinceStartup < timeout) yield return null;
        starting = false;
        if (microphone == null || Microphone.GetPosition(device) <= 0 || !Room.Connected) { StopVoice(); Status?.Invoke("Microphone unavailable"); yield break; }
        if (microphone.frequency != 16000 || microphone.channels != 1) { StopVoice(); Status?.Invoke("Microphone must support 16 kHz mono"); yield break; }
        read = Microphone.GetPosition(device); Enabled = true;
        Room.Send(new JObject { ["type"] = "voice", ["enabled"] = true }); Status?.Invoke("Microphone on — use headphones to prevent echo");
    }
    public void StopVoice() {
        Enabled = false; if (device != null) Microphone.End(device); if (microphone != null) Destroy(microphone); microphone = null;
        if (Room?.Connected == true) Room.Send(new JObject { ["type"] = "voice", ["enabled"] = false });
        Status?.Invoke("Microphone muted");
    }
    public void ToggleDeafen() { Deafened = !Deafened; foreach (var speaker in speakers.Values) speaker.Source.mute = Deafened; Status?.Invoke(Deafened ? "Room audio muted" : "Room audio on"); }
    void Update() {
        if (!Enabled || microphone == null || Time.unscaledTime < nextFrame) return;
        if (!Room.Connected) { StopVoice(); return; }
        nextFrame = Time.unscaledTime + 0.1f;
        int end = Microphone.GetPosition(device), available = (end - read + microphone.samples) % microphone.samples;
        if (available < 1600) return;
        // Drop backlog after a stalled frame, keeping interactive voice bounded.
        if (available > 4800) read = (end - 1600 + microphone.samples) % microphone.samples;
        float[] samples = new float[1600]; microphone.GetData(samples, read); read = (read + 1600) % microphone.samples;
        byte[] bytes = new byte[3200];
        for (int i = 0; i < samples.Length; i++) { short sample = (short)(Mathf.Clamp(samples[i], -1, 1) * 32767); bytes[i * 2] = (byte)(sample & 255); bytes[i * 2 + 1] = (byte)((sample >> 8) & 255); }
        Room.Send(new JObject { ["type"] = "audio", ["pcm"] = Convert.ToBase64String(bytes) });
    }
    void OnMessage(JObject message) {
        string type = (string)message["type"];
        if (type == "disconnected") { StopAllCoroutines(); starting = false; StopVoice(); Clear(); return; }
        if (type == "state") {
            var active = new HashSet<string>();
            foreach (var p in (JArray)message["players"]) if ((bool)p["connected"] && (bool)p["voice"]) active.Add((string)p["id"]);
            foreach (var id in new List<string>(speakers.Keys)) if (!active.Contains(id)) { speakers[id].Dispose(); speakers.Remove(id); }
        }
        if (type != "audio" || Deafened) return;
        string from = (string)message["playerId"];
        if (!speakers.TryGetValue(from, out var speaker)) { speaker = new Speaker(transform); speakers.Add(from, speaker); }
        try { speaker.Push(Convert.FromBase64String((string)message["pcm"])); } catch (FormatException) { }
    }
    void Clear() { foreach (var speaker in speakers.Values) speaker.Dispose(); speakers.Clear(); }
    void OnApplicationPause(bool paused) { if (paused) { StopAllCoroutines(); starting = false; StopVoice(); } }
    void OnDestroy() { if (Room != null) Room.Message -= OnMessage; StopVoice(); Clear(); }
    sealed class Speaker {
        public readonly AudioSource Source;
        readonly Queue<float> samples = new Queue<float>(); readonly object gate = new object(); readonly AudioClip clip;
        public Speaker(Transform parent) {
            var go = new GameObject("Room voice"); go.transform.SetParent(parent); Source = go.AddComponent<AudioSource>();
            clip = AudioClip.Create("Voice", 16000, 1, 16000, true, data => { lock (gate) { for (int i = 0; i < data.Length; i++) data[i] = samples.Count > 0 ? samples.Dequeue() : 0; } });
            Source.clip = clip; Source.loop = true; Source.Play();
        }
        public void Push(byte[] bytes) { if (bytes.Length != 3200) return; lock (gate) { if (samples.Count > 8000) samples.Clear(); for (int i = 0; i < bytes.Length; i += 2) samples.Enqueue((short)(bytes[i] | bytes[i + 1] << 8) / 32768f); } }
        public void Dispose() { Source.Stop(); UnityEngine.Object.Destroy(Source.gameObject); UnityEngine.Object.Destroy(clip); }
    }
}
}
