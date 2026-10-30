package com.stremiovr;
import android.app.Activity;
import com.unity3d.player.UnityPlayer;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;
import org.webrtc.*;
import org.webrtc.audio.JavaAudioDeviceModule;

/** DTLS-SRTP/Opus audio; room WebSocket carries signaling only. */
public final class RtcVoice {
 private final ExecutorService worker=Executors.newSingleThreadExecutor();
 private final String receiver; private final Activity activity;
 private PeerConnectionFactory factory; private JavaAudioDeviceModule device; private AudioSource source; private AudioTrack local;
 private final Map<String,Peer> peers=new HashMap<>(); private List<PeerConnection.IceServer> servers=new ArrayList<>();
 private boolean muted=true,deafened=false,closed=false;
 public RtcVoice(Activity activity,String receiver){this.activity=activity;this.receiver=receiver;enqueue(()->{
  PeerConnectionFactory.initialize(PeerConnectionFactory.InitializationOptions.builder(activity).createInitializationOptions());
  device=JavaAudioDeviceModule.builder(activity).setUseHardwareAcousticEchoCanceler(true).setUseHardwareNoiseSuppressor(true).createAudioDeviceModule();
  factory=PeerConnectionFactory.builder().setAudioDeviceModule(device).createPeerConnectionFactory();
  MediaConstraints constraints=new MediaConstraints();constraints.optional.add(new MediaConstraints.KeyValuePair("googEchoCancellation","true"));constraints.optional.add(new MediaConstraints.KeyValuePair("googNoiseSuppression","true"));
  source=factory.createAudioSource(constraints);local=factory.createAudioTrack("voice",source);local.setEnabled(false);device.setMicrophoneMute(true);
 });}
 private void enqueue(Runnable action){try{worker.execute(action);}catch(RejectedExecutionException ignored){}}
 private void status(String message){UnityPlayer.UnitySendMessage(receiver,"RtcStatus",message);}
 private void send(String id,JSONObject data){try{JSONObject message=new JSONObject();message.put("target",id);message.put("data",data);UnityPlayer.UnitySendMessage(receiver,"RtcSignal",message.toString());}catch(Exception ignored){}}
 public void configure(String json){enqueue(()->{try{List<PeerConnection.IceServer> next=new ArrayList<>();JSONArray list=new JSONArray(json);for(int i=0;i<list.length();i++){JSONObject item=list.getJSONObject(i);Object value=item.get("urls");List<String> urls=new ArrayList<>();if(value instanceof JSONArray){JSONArray array=(JSONArray)value;for(int n=0;n<array.length();n++)urls.add(array.getString(n));}else urls.add(value.toString());next.add(PeerConnection.IceServer.builder(urls).setUsername(item.optString("username")).setPassword(item.optString("credential")).createIceServer());}servers=next;}catch(Exception e){status("Invalid voice ICE configuration");}});}
 public void mute(boolean value){enqueue(()->{muted=value;if(device!=null)device.setMicrophoneMute(value);if(local!=null)local.setEnabled(!value);});}
 public void deafen(boolean value){enqueue(()->{deafened=value;for(Peer p:peers.values())if(p.remote!=null)p.remote.setEnabled(!value);});}
 public void connect(String id,boolean offer){enqueue(()->{if(closed||peers.containsKey(id))return;Peer p=new Peer(id);peers.put(id,p);if(offer)p.pc.createOffer(p.description(true),new MediaConstraints());});}
 public void remove(String id){enqueue(()->{Peer p=peers.remove(id);if(p!=null)p.pc.dispose();});}
 public void signal(String id,String json){enqueue(()->{try{
  Peer p=peers.get(id);if(p==null||closed)return;JSONObject data=new JSONObject(json);
  if(data.has("candidate")){IceCandidate candidate=new IceCandidate(data.optString("sdpMid","0"),data.optInt("sdpMLineIndex",0),data.getString("candidate"));if(p.hasRemote)p.pc.addIceCandidate(candidate);else if(p.pending.size()<128)p.pending.add(candidate);return;}
  String type=data.getString("type");if(!type.equals("offer")&&!type.equals("answer"))return;
  p.pc.setRemoteDescription(new Observer(){@Override public void onSetSuccess(){enqueue(()->{if(!peers.containsKey(id))return;p.hasRemote=true;for(IceCandidate candidate:p.pending)p.pc.addIceCandidate(candidate);p.pending.clear();if(type.equals("offer"))p.pc.createAnswer(p.description(false),new MediaConstraints());});}},new SessionDescription(SessionDescription.Type.fromCanonicalForm(type),data.getString("sdp")));
 }catch(Exception e){status("Voice negotiation failed");}});}
 private class Observer implements SdpObserver {
  public void onCreateSuccess(SessionDescription description){} public void onSetSuccess(){}
  public void onCreateFailure(String error){status("Voice negotiation failed");}public void onSetFailure(String error){status("Voice description rejected");}
 }
 private class Peer implements PeerConnection.Observer {
  final String id;final PeerConnection pc;final List<IceCandidate> pending=new ArrayList<>();boolean hasRemote;AudioTrack remote;
  Peer(String id){this.id=id;PeerConnection.RTCConfiguration config=new PeerConnection.RTCConfiguration(servers);config.sdpSemantics=PeerConnection.SdpSemantics.UNIFIED_PLAN;pc=factory.createPeerConnection(config,this);if(pc==null)throw new IllegalStateException("WebRTC peer unavailable");pc.addTrack(local,Collections.singletonList("voice"));}
  SdpObserver description(boolean offer){return new Observer(){@Override public void onCreateSuccess(SessionDescription description){enqueue(()->{if(!peers.containsKey(id))return;pc.setLocalDescription(new Observer(){@Override public void onSetSuccess(){try{JSONObject data=new JSONObject();data.put("type",description.type.canonicalForm());data.put("sdp",description.description);send(id,data);}catch(Exception ignored){}}},description);});}};}
  public void onIceCandidate(IceCandidate candidate){try{JSONObject data=new JSONObject();data.put("candidate",candidate.sdp);data.put("sdpMid",candidate.sdpMid);data.put("sdpMLineIndex",candidate.sdpMLineIndex);send(id,data);}catch(Exception ignored){}}
  public void onTrack(RtpTransceiver transceiver){enqueue(()->{MediaStreamTrack track=transceiver.getReceiver().track();if(track instanceof AudioTrack){remote=(AudioTrack)track;remote.setEnabled(!deafened);}});}
  public void onConnectionChange(PeerConnection.PeerConnectionState state){if(state==PeerConnection.PeerConnectionState.CONNECTED)status("WebRTC voice connected");if(state==PeerConnection.PeerConnectionState.FAILED)status("Voice connection failed. This network may require a TURN server; reconnect the room to retry.");}
  public void onSignalingChange(PeerConnection.SignalingState state){}public void onIceConnectionChange(PeerConnection.IceConnectionState state){}public void onIceConnectionReceivingChange(boolean receiving){}public void onIceGatheringChange(PeerConnection.IceGatheringState state){}public void onIceCandidatesRemoved(IceCandidate[] candidates){}public void onAddStream(MediaStream stream){}public void onRemoveStream(MediaStream stream){}public void onDataChannel(DataChannel channel){}public void onRenegotiationNeeded(){}public void onAddTrack(RtpReceiver receiver,MediaStream[] streams){}
 }
 public void close(){enqueue(()->{closed=true;for(Peer p:peers.values())p.pc.dispose();peers.clear();if(local!=null)local.dispose();if(source!=null)source.dispose();if(factory!=null)factory.dispose();if(device!=null)device.release();});worker.shutdown();}
}
