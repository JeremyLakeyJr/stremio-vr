using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class VoiceChat:MonoBehaviour {
 public RoomClient Room; public event Action<string> Status;
 public bool Enabled {get;private set;} public bool Deafened {get;private set;}
 DesktopBridge desktop;bool pcReady;
 AndroidJavaObject engine; bool starting; string ice="[]"; readonly HashSet<string> peers=new HashSet<string>();
 public void Initialize(RoomClient room){Room=room;room.Message+=OnMessage;
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
 desktop=DesktopBridge.Get();desktop.Message+=DesktopMessage;
#endif
 }
 void DesktopMessage(JObject data){switch((string)data["kind"]){case "voice-ready":if(!Room.Connected||Room.IsSolo){Clear();return;}starting=false;pcReady=true;Enabled=true;desktop.Rtc("voice-deafen",new JObject{["value"]=Deafened});Room.Send(new JObject{["type"]="voice",["enabled"]=true,["ready"]=true});Status?.Invoke("WebRTC microphone on");if(Room.State!=null)OnMessage(Room.State);break;case "voice-error":starting=false;Clear();Status?.Invoke((string)data["message"]);break;case "voice-signal":if(pcReady)Room.Send(new JObject{["type"]="signal",["to"]=data["to"],["data"]=data["data"]});break;}}
 void Engine(string method,params object[] args){if(!desktop){engine?.Call(method,args);return;}var data=new JObject();string operation="voice-"+method;switch(method){case "connect":data["id"]=(string)args[0];data["offer"]=(bool)args[1];break;case "signal":data["id"]=(string)args[0];data["data"]=JObject.Parse((string)args[1]);break;case "remove":data["id"]=(string)args[0];break;case "mute":case "deafen":data["value"]=(bool)args[0];break;}desktop.Rtc(operation,data);}

 public void Toggle(){if(Enabled)StopVoice();else if(!starting)StartCoroutine(StartVoice());}
 IEnumerator StartVoice(){
  if(Room.IsSolo){Status?.Invoke("You are in solo mode. Connect a multiplayer room to use voice.");yield break;}
  if(!Room.Connected||Room.State==null){Status?.Invoke("Join a room first");yield break;}
  starting=true;
  if(desktop){if(pcReady){Engine("mute",false);Enabled=true;starting=false;Room.Send(new JObject{["type"]="voice",["enabled"]=true,["ready"]=true});}else desktop.Rtc("voice-start",new JObject{["ice"]=JArray.Parse(ice)});yield break;}
#if UNITY_ANDROID && !UNITY_EDITOR
  if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)){
   UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);float until=Time.realtimeSinceStartup+20;
   while(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)&&Time.realtimeSinceStartup<until)yield return null;
   if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)){starting=false;Status?.Invoke("Microphone permission is required to enable room voice");yield break;}
  }
  if(!Room.Connected){starting=false;yield break;}
  try{
   if(engine==null){using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))engine=new AndroidJavaObject("com.stremiovr.RtcVoice",activity,gameObject.name);engine.Call("configure",ice);engine.Call("deafen",Deafened);}
   engine.Call("mute",false);Enabled=true;Room.Send(new JObject{["type"]="voice",["enabled"]=true,["ready"]=true});Status?.Invoke("WebRTC microphone on");
  }catch(Exception){Status?.Invoke("Could not initialize WebRTC voice");Clear();}
#else
  Status?.Invoke("WebRTC voice is available in the Quest APK");
#endif
  starting=false;yield break;
 }
 public void StopVoice(){Enabled=false;Engine("mute",true);if(Room?.Connected==true)Room.Send(new JObject{["type"]="voice",["enabled"]=false,["ready"]=engine!=null||pcReady});Status?.Invoke("Microphone muted");}
 public void ToggleDeafen(){Deafened=!Deafened;Engine("deafen",Deafened);Status?.Invoke(Deafened?"Room voice muted":"Room voice on");}
 void OnMessage(JObject message){
  string type=(string)message["type"];
  if(type=="joined")ice=(message["iceServers"]??new JArray()).ToString();
  if(type=="disconnected"||type=="left"){StopAllCoroutines();starting=false;Clear();return;}
  if(engine==null&&!pcReady)return;
  if(type=="state"){
   var active=new HashSet<string>();foreach(var p in (JArray)message["players"]){string id=(string)p["id"];if(id!=Room.PlayerId&&(bool)p["connected"]&&(bool?)p["voiceReady"]==true){active.Add(id);if(peers.Add(id))Engine("connect",id,string.CompareOrdinal(Room.PlayerId,id)<0);}}
   foreach(string id in new List<string>(peers))if(!active.Contains(id)){Engine("remove",id);peers.Remove(id);}
  }
  if(type=="signal"&&peers.Contains((string)message["from"]))Engine("signal",(string)message["from"],message["data"].ToString());
 }
 public void RtcSignal(string json){if(engine==null||!Room.Connected)return;try{var message=JObject.Parse(json);message["type"]="signal";Room.Send(message);}catch{Status?.Invoke("Voice signaling failed");}}
 public void RtcStatus(string message){Debug.Log("[WebRTC] "+message);Status?.Invoke(message);}
 void Clear(){Enabled=false;pcReady=false;if(desktop)desktop.Rtc("voice-close");peers.Clear();if(engine!=null){engine.Call("close");engine.Dispose();engine=null;}}
 void OnApplicationPause(bool paused){if(paused){StopAllCoroutines();starting=false;Clear();if(Room?.Connected==true)Room.Send(new JObject{["type"]="voice",["enabled"]=false,["ready"]=false});}}
 void OnDestroy(){if(Room)Room.Message-=OnMessage;if(desktop)desktop.Message-=DesktopMessage;Clear();}
}
}
