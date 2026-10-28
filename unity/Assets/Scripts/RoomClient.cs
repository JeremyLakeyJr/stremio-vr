using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StremioVR {
public sealed class RoomClient : MonoBehaviour {
    public event Action<JObject> Message;
    public event Action<string> Status;
    public string PlayerId { get; private set; }
    public string RoomId { get; private set; }
    public JObject State { get; private set; }
    public string ApiEndpoint {
        get { var uri = new Uri(endpoint); return new UriBuilder(uri) { Scheme = uri.Scheme == "wss" ? "https" : "http", Port = uri.Port, Path = "/api/core", Query = "", Fragment = "" }.Uri.AbsoluteUri; }
    }
    public bool Connected => socket?.State == WebSocketState.Open;
    public bool IsHost => Connected && State != null && (string)State["host"] == PlayerId;
    public double ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + clockOffset;
    volatile ClientWebSocket socket;
    volatile bool backgrounded;
    CancellationTokenSource lifetime;
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    readonly ConcurrentQueue<Action> main = new ConcurrentQueue<Action>();
    string token, endpoint; JObject localProfile; double clockOffset; float nextPing; volatile int generation; int pendingSends;
    public async void Connect(string url, string roomId, JObject profile) {
        Disconnect();
        try { url = NormalizeEndpoint(url); } catch (Exception error) { Status?.Invoke(error.Message); return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss")) { Status?.Invoke("Use ws:// or wss:// server URL ending in /ws"); return; }
        endpoint = url; RoomId = string.IsNullOrWhiteSpace(roomId) ? null : roomId.Trim(); localProfile = (JObject)profile.DeepClone();
        lifetime = new CancellationTokenSource();
        await Run(++generation, lifetime.Token);
    }
    public static string NormalizeEndpoint(string value) {
        if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri) || !string.IsNullOrEmpty(uri.UserInfo)) throw new Exception("Enter an HTTPS or WSS room-server address");
        string scheme=uri.Scheme=="https"?"wss":uri.Scheme=="http"?"ws":uri.Scheme;
        if(scheme!="ws" && scheme!="wss") throw new Exception("Use an HTTPS or WSS room-server address");
        var builder=new UriBuilder(uri) { Scheme=scheme, Fragment="" };
        if(uri.AbsolutePath=="/") builder.Path="/ws";
        return builder.Uri.AbsoluteUri;
    }
    async Task Run(int run, CancellationToken cancel) {
        int retry = 0;
        while (!cancel.IsCancellationRequested && run == generation) {
            var current = new ClientWebSocket(); socket = current;
            try {
                main.Enqueue(() => Status?.Invoke(retry == 0 ? "Connecting…" : "Reconnecting…"));
                await current.ConnectAsync(new Uri(endpoint), cancel).ConfigureAwait(false);
                await SendOn(current, new JObject { ["type"] = RoomId == null ? "create" : "join", ["roomId"] = RoomId, ["token"] = token, ["profile"] = localProfile }, cancel).ConfigureAwait(false);
                var buffer = new byte[8192];
                while (current.State == WebSocketState.Open && !cancel.IsCancellationRequested) {
                    using (var stream = new MemoryStream()) {
                        WebSocketReceiveResult result;
                        do {
                            result = await current.ReceiveAsync(new ArraySegment<byte>(buffer), cancel).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) break;
                            stream.Write(buffer, 0, result.Count);
                            if (stream.Length > 131072) throw new IOException("Oversized server message");
                        } while (!result.EndOfMessage);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        var data = JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                        // Retain reconnect credentials before receiving another frame.
                        if ((string)data["type"] == "joined") { RoomId = (string)data["roomId"]; token = (string)data["token"]; }
                        string messageType = (string)data["type"];
                        bool transient = messageType == "pose" || messageType == "audio" || messageType == "state";
                        if (!transient || (!backgrounded && main.Count < 512)) main.Enqueue(() => { if (run == generation) Handle(data); });
                        retry = 0;
                    }
                }
                if (current.CloseStatus == (WebSocketCloseStatus)4000) { main.Enqueue(() => { if (run == generation) { Disconnect(); Status?.Invoke("Session opened elsewhere"); } }); return; }
            } catch (OperationCanceledException) { return; }
              catch (Exception error) { main.Enqueue(() => { if (run == generation) Status?.Invoke(error.Message); }); }
            finally { current.Dispose(); }
            if (cancel.IsCancellationRequested) return;
            main.Enqueue(() => { if (run == generation) { State = null; Message?.Invoke(new JObject { ["type"] = "disconnected" }); } });
            try { await Task.Delay(Math.Min(10000, 1000 * (1 << Math.Min(retry++, 3))), cancel).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }
    void Handle(JObject data) {
        string type = (string)data["type"];
        if (type == "joined") { PlayerId = (string)data["playerId"]; Status?.Invoke("Joined room " + RoomId); Send(new JObject { ["type"] = "ping", ["sentAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }); }
        if (type == "state") { if (State == null) clockOffset = (double)data["serverTime"] - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); State = data; }
        if (type == "pong") { double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); clockOffset = (double)data["serverTime"] - ((double)data["sentAt"] + now) / 2; }
        if (type == "error") { Status?.Invoke((string)data["message"]); if (State == null || (string)data["message"] == "Room not found") Disconnect(); }
        Message?.Invoke(data);
    }
    public async void Send(JObject data) {
        var current = socket; var cancel = lifetime?.Token ?? CancellationToken.None;
        if (current?.State != WebSocketState.Open) return;
        if ((string)data["type"] == "profile") localProfile = (JObject)data["profile"].DeepClone();
        if (Interlocked.Increment(ref pendingSends) > 32) {
            Interlocked.Decrement(ref pendingSends);
            if ((string)data["type"] != "pose" && (string)data["type"] != "audio") Status?.Invoke("Connection is busy; try again");
            return;
        }
        try { await SendOn(current, data, cancel); }
        catch (Exception error) { main.Enqueue(() => Status?.Invoke("Send failed: " + error.Message)); }
        finally { Interlocked.Decrement(ref pendingSends); }
    }
    async Task SendOn(ClientWebSocket current, JObject data, CancellationToken cancel) {
        var bytes = Encoding.UTF8.GetBytes(data.ToString(Newtonsoft.Json.Formatting.None));
        await sendLock.WaitAsync(cancel).ConfigureAwait(false);
        try { await current.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancel).ConfigureAwait(false); } finally { sendLock.Release(); }
    }
    public async void Leave() {
        var current = socket; var cancel = lifetime?.Token ?? CancellationToken.None;
        try { if (current?.State == WebSocketState.Open) await SendOn(current, new JObject { ["type"] = "leave" }, cancel); }
        catch (Exception) { /* Closing still starts server-side cleanup. */ }
        finally { Disconnect(); Status?.Invoke("Left room"); }
    }
    public void Disconnect() {
        ++generation; lifetime?.Cancel(); socket?.Abort(); socket = null; lifetime?.Dispose(); lifetime = null;
        State = null; RoomId = null; PlayerId = null; token = null;
        Message?.Invoke(new JObject { ["type"] = "disconnected" });
    }
    void Update() {
        for (int i = 0; i < 100 && main.TryDequeue(out var action); i++) action();
        if (Connected && Time.unscaledTime > nextPing) { nextPing = Time.unscaledTime + 5; Send(new JObject { ["type"] = "ping", ["sentAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }); }
    }
    void OnApplicationPause(bool paused) { backgrounded = paused; }
    void OnDestroy() { Disconnect(); }
}
}
