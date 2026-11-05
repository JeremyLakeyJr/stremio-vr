using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class DesktopBridge:MonoBehaviour {
 static DesktopBridge instance;
 public static DesktopBridge Get(){if(!instance){var go=new GameObject("Desktop runtime");DontDestroyOnLoad(go);instance=go.AddComponent<DesktopBridge>();}return instance;}
 public event Action<JObject> Message;public bool Ready {get;private set;}public bool Failed {get;private set;}public bool VaultReady=>vault.ContainsKey("stremio")&&vault.ContainsKey("browser-profile-master");
 public Texture2D Frame {get;private set;}public JArray Tabs {get;private set;}=new JArray();
 readonly Dictionary<string,string> vault=new Dictionary<string,string>();bool encrypted;
 readonly ConcurrentQueue<JObject> incoming=new ConcurrentQueue<JObject>();readonly Queue<JObject> pending=new Queue<JObject>();readonly SemaphoreSlim sends=new SemaphoreSlim(1,1);
 readonly CancellationTokenSource lifetime=new CancellationTokenSource();ClientWebSocket socket;Process process;FramePacket latest;StreamWriter runtimeLog;int logChars;readonly object logLock=new object();
 void Awake(){StartRuntime();}
 async void StartRuntime(){try{
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();byte[] random=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(random);string token=BitConverter.ToString(random).Replace("-","").ToLowerInvariant();
  bool windows=Application.platform==RuntimePlatform.WindowsPlayer;string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../DesktopRuntime")),executable=Path.Combine(directory,windows?"electron.exe":"electron");
  if(!File.Exists(executable))throw new Exception("DesktopRuntime is missing. Extract the complete Movie Night archive before launching.");
  var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=directory,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.EnvironmentVariables["MOVIE_NIGHT_PORT"]=port.ToString();start.EnvironmentVariables["MOVIE_NIGHT_TOKEN"]=token;start.EnvironmentVariables["MOVIE_NIGHT_DATA"]=Application.persistentDataPath;start.EnvironmentVariables["MOVIE_NIGHT_SOLO"]=Path.Combine(Application.streamingAssetsPath,"solo.js");Directory.CreateDirectory(Application.persistentDataPath);runtimeLog=new StreamWriter(Path.Combine(Application.persistentDataPath,"desktop-runtime.log"),false);runtimeLog.AutoFlush=true;process=new Process{StartInfo=start};process.OutputDataReceived+=Log;process.ErrorDataReceived+=Log;process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
  for(int attempt=0;attempt<100;attempt++){lifetime.Token.ThrowIfCancellationRequested();socket=new ClientWebSocket();try{await socket.ConnectAsync(new Uri("ws://127.0.0.1:"+port+"/?token="+token),lifetime.Token);break;}catch{socket.Dispose();socket=null;if(process.HasExited)throw new Exception("Desktop runtime could not start. Check the runtime log and Linux sandbox support.");await Task.Delay(200,lifetime.Token);}}
  if(socket==null)throw new Exception("Desktop runtime connection timed out");await Receive(lifetime.Token);
 }catch(OperationCanceledException){}catch(Exception error){if(this){Failed=true;Ready=false;incoming.Enqueue(new JObject{["kind"]="status",["message"]=error.Message});}}}
 void Log(object sender,DataReceivedEventArgs args){if(args.Data==null)return;lock(logLock){try{if(logChars<1024*1024){runtimeLog?.WriteLine(args.Data);logChars+=args.Data.Length;}}catch{}}}
 sealed class FramePacket {public byte[] Bytes;public int Length;public void Release(){ArrayPool<byte>.Shared.Return(Bytes);}}
 async Task Receive(CancellationToken token){const int limit=3840*2160*4+8;while(!token.IsCancellationRequested){byte[] bytes=ArrayPool<byte>.Shared.Rent(limit);try{int length=0;WebSocketReceiveResult result;do{result=await socket.ReceiveAsync(new ArraySegment<byte>(bytes,length,limit-length),token).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)throw new IOException("Desktop runtime stopped. Restart Movie Night.");length+=result.Count;if(length>=limit&&!result.EndOfMessage)throw new IOException("Oversized desktop frame");}while(!result.EndOfMessage);if(result.MessageType==WebSocketMessageType.Binary){var packet=new FramePacket{Bytes=bytes,Length=length};bytes=null;Interlocked.Exchange(ref latest,packet)?.Release();}else if(length<=131072&&incoming.Count<128)incoming.Enqueue(JObject.Parse(Encoding.UTF8.GetString(bytes,0,length)));}finally{if(bytes!=null)ArrayPool<byte>.Shared.Return(bytes);}}}

 public void Send(JObject data){if(!Ready){if(pending.Count<128)pending.Enqueue(data);return;}SendReady(data);}
 async void SendReady(JObject data){try{var bytes=Encoding.UTF8.GetBytes(data.ToString(Newtonsoft.Json.Formatting.None));await sends.WaitAsync(lifetime.Token);try{if(socket?.State==WebSocketState.Open)await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,lifetime.Token);}finally{sends.Release();}}catch(OperationCanceledException){}catch(Exception error){if(this)incoming.Enqueue(new JObject{["kind"]="status",["message"]=error.Message});}}
 public void Rtc(string method,JObject data=null){data=data??new JObject();data["op"]="rtc";data["method"]=method;Send(data);}
 public void ClearFrame(){Interlocked.Exchange(ref latest,null)?.Release();if(Frame)Destroy(Frame);Frame=null;}
 public void Browser(string method,params object[] args){Send(new JObject{["op"]="browser",["method"]=method,["args"]=JArray.FromObject(args)});}
 public string LoadSecret(string name){return vault.TryGetValue(name,out var value)?value:null;}
 public void SaveSecret(string name,string value){vault[name]=value;if(!encrypted)throw new Exception("OS keyring unavailable; sign-in is kept for this session only");Send(new JObject{["op"]="vault",["method"]="save",["name"]=name,["value"]=value});}
 public void RemoveSecret(string name){vault[name]=null;Send(new JObject{["op"]="vault",["method"]="remove",["name"]=name});}
 public string BrowserProfileKey(string authority){if(!VaultReady)throw new Exception("Desktop browser is still starting. Try again shortly.");string secret=LoadSecret("browser-profile-master");if(string.IsNullOrEmpty(secret)){var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);secret=Convert.ToBase64String(bytes);try{SaveSecret("browser-profile-master",secret);}catch{Message?.Invoke(new JObject{["kind"]="status",["message"]="No OS keyring: browser room sign-in will last for this app session."});}}using(var hmac=new HMACSHA256(Convert.FromBase64String(secret)))return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(authority))).Replace("-","").ToLowerInvariant();}
 unsafe void Update(){
  for(int i=0;i<64&&incoming.TryDequeue(out var data);i++){
   string kind=(string)data["kind"];if(kind=="ready"){Ready=true;foreach(string name in new[]{"stremio","browser-profile-master"})Send(new JObject{["op"]="vault",["method"]="load",["name"]=name});while(pending.Count>0)Send(pending.Dequeue());}
   if(kind=="vault"){vault[(string)data["name"]]=(string)data["value"];encrypted=(bool?)data["available"]==true;}
   if(kind=="browser-tabs")Tabs=(JArray)data["tabs"];Message?.Invoke(data);
  }
  var packet=Interlocked.Exchange(ref latest,null);if(packet==null)return;
  try{var frame=packet.Bytes;int length=packet.Length;if(length<8)return;int width=BitConverter.ToInt32(frame,0),height=BitConverter.ToInt32(frame,4);if(width<2||height<2||width>3840||height>2160||length!=8+width*height*4)return;
   if(!Frame||Frame.width!=width||Frame.height!=height){if(Frame)Destroy(Frame);Frame=new Texture2D(width,height,TextureFormat.BGRA32,false,false);Frame.wrapMode=TextureWrapMode.Clamp;Frame.filterMode=FilterMode.Bilinear;UnityEngine.Debug.Log("[Desktop] Browser frame "+width+"x"+height);}
   fixed(byte* address=frame)Frame.LoadRawTextureData((IntPtr)(address+8),length-8);Frame.Apply(false,false);
  }finally{packet.Release();}
 }
 void OnDestroy(){try{if(socket?.State==WebSocketState.Open){var quit=Encoding.UTF8.GetBytes("{\"op\":\"quit\"}");socket.SendAsync(new ArraySegment<byte>(quit),WebSocketMessageType.Text,true,CancellationToken.None).Wait(500);process?.WaitForExit(1000);}}catch{}lifetime.Cancel();Interlocked.Exchange(ref latest,null)?.Release();socket?.Abort();socket?.Dispose();try{if(process!=null&&!process.HasExited)process.Kill();}catch{}process?.Dispose();lock(logLock){runtimeLog?.Dispose();runtimeLog=null;}if(Frame)Destroy(Frame);lifetime.Dispose();if(instance==this)instance=null;}
}
}
