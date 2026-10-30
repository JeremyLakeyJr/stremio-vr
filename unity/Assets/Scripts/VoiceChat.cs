using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class VoiceChat:MonoBehaviour {
 public RoomClient Room; public event Action<string> Status;
 public bool Enabled {get;private set;} public bool Deafened {get;private set;}
 AndroidJavaObject engine; bool starting; string ice="[]"; readonly HashSet<string> peers=new HashSet<string>();
 public void Initialize(RoomClient room){Room=room;room.Message+=OnMessage;}
 public void Toggle(){if(Enabled)StopVoice();else if(!starting)StartCoroutine(StartVoice());}
 IEnumerator StartVoice(){
  if(Room.IsSolo){Status?.Invoke("You are in solo mode. Connect a multiplayer room to use voice.");yield break;}
  if(!Room.Connected||Room.State==null){Status?.Invoke("Join a room first");yield break;}
  starting=true;
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
 public void StopVoice(){Enabled=false;engine?.Call("mute",true);if(Room?.Connected==true)Room.Send(new JObject{["type"]="voice",["enabled"]=false,["ready"]=engine!=null});Status?.Invoke("Microphone muted");}
 public void ToggleDeafen(){Deafened=!Deafened;engine?.Call("deafen",Deafened);Status?.Invoke(Deafened?"Room voice muted":"Room voice on");}
 void OnMessage(JObject message){
  string type=(string)message["type"];
  if(type=="joined")ice=(message["iceServers"]??new JArray()).ToString();
  if(type=="disconnected"||type=="left"){StopAllCoroutines();starting=false;Clear();return;}
  if(engine==null)return;
  if(type=="state"){
   var active=new HashSet<string>();foreach(var p in (JArray)message["players"]){string id=(string)p["id"];if(id!=Room.PlayerId&&(bool)p["connected"]&&(bool?)p["voiceReady"]==true){active.Add(id);if(peers.Add(id))engine.Call("connect",id,string.CompareOrdinal(Room.PlayerId,id)<0);}}
   foreach(string id in new List<string>(peers))if(!active.Contains(id)){engine.Call("remove",id);peers.Remove(id);}
  }
  if(type=="signal"&&peers.Contains((string)message["from"]))engine.Call("signal",(string)message["from"],message["data"].ToString());
 }
 public void RtcSignal(string json){if(engine==null||!Room.Connected)return;try{var message=JObject.Parse(json);message["type"]="signal";Room.Send(message);}catch{Status?.Invoke("Voice signaling failed");}}
 public void RtcStatus(string message){Debug.Log("[WebRTC] "+message);Status?.Invoke(message);}
 void Clear(){Enabled=false;peers.Clear();if(engine!=null){engine.Call("close");engine.Dispose();engine=null;}}
 void OnApplicationPause(bool paused){if(paused){StopAllCoroutines();starting=false;Clear();if(Room?.Connected==true)Room.Send(new JObject{["type"]="voice",["enabled"]=false,["ready"]=false});}}
 void OnDestroy(){if(Room)Room.Message-=OnMessage;Clear();}
}
}
