package com.stremiovr;
import android.app.Activity;
import com.unity3d.player.UnityPlayer;
import java.util.*;
import java.util.concurrent.*;
import java.nio.ByteBuffer;
import org.json.*;
import org.webrtc.*;
import org.webrtc.audio.JavaAudioDeviceModule;

/** Receive-only hardware-decoded browser stream. Pooled I420 frames avoid JNI pixel arrays. */
public final class RtcBrowser implements PeerConnection.Observer,VideoSink {
 private final ExecutorService worker=Executors.newSingleThreadExecutor();
 private final String receiver;private PeerConnectionFactory factory;private JavaAudioDeviceModule device;private EglBase egl;private PeerConnection pc;private AudioTrack audio;private VideoTrack video;
 private final List<IceCandidate> candidates=new ArrayList<>();private boolean remote;private volatile boolean closed;private double volume=1;
 private ByteBuffer pending,front,spare;private int pendingWidth,pendingHeight,width,height;
 public RtcBrowser(Activity activity,String receiver,String ice){this.receiver=receiver;enqueue(()->{try{
  PeerConnectionFactory.initialize(PeerConnectionFactory.InitializationOptions.builder(activity).createInitializationOptions());egl=EglBase.create();
  device=JavaAudioDeviceModule.builder(activity).setUseStereoOutput(true).createAudioDeviceModule();device.setMicrophoneMute(true);
  factory=PeerConnectionFactory.builder().setAudioDeviceModule(device).setVideoDecoderFactory(new DefaultVideoDecoderFactory(egl.getEglBaseContext())).createPeerConnectionFactory();
  List<PeerConnection.IceServer> servers=new ArrayList<>();JSONArray list=new JSONArray(ice);for(int i=0;i<list.length();i++){JSONObject item=list.getJSONObject(i);Object value=item.get("urls");List<String> urls=new ArrayList<>();if(value instanceof JSONArray){JSONArray a=(JSONArray)value;for(int j=0;j<a.length();j++)urls.add(a.getString(j));}else urls.add(value.toString());servers.add(PeerConnection.IceServer.builder(urls).setUsername(item.optString("username")).setPassword(item.optString("credential")).createIceServer());}
  PeerConnection.RTCConfiguration config=new PeerConnection.RTCConfiguration(servers);config.sdpSemantics=PeerConnection.SdpSemantics.UNIFIED_PLAN;pc=factory.createPeerConnection(config,this);if(pc==null)throw new IllegalStateException();
 }catch(Exception e){status("Browser decoder could not start");}});}
 private void enqueue(Runnable r){try{worker.execute(()->{if(!closed)r.run();});}catch(RejectedExecutionException ignored){}}
 private void status(String value){UnityPlayer.UnitySendMessage(receiver,"BrowserStatus",value);}
 private void send(JSONObject value){UnityPlayer.UnitySendMessage(receiver,"ServerBrowserSignal",value.toString());}
 private class Observer implements SdpObserver {public void onCreateSuccess(SessionDescription d){}public void onSetSuccess(){}public void onCreateFailure(String e){status("Browser stream negotiation failed");}public void onSetFailure(String e){status("Browser stream description rejected");}}
 public void signal(String json){enqueue(()->{try{
  if(pc==null)return;JSONObject d=new JSONObject(json);
  if(d.has("candidate")){IceCandidate c=new IceCandidate(d.optString("sdpMid","0"),d.optInt("sdpMLineIndex"),d.getString("candidate"));if(remote)pc.addIceCandidate(c);else if(candidates.size()<128)candidates.add(c);return;}
  if(!d.optString("type").equals("offer"))return;
  pc.setRemoteDescription(new Observer(){public void onSetSuccess(){enqueue(()->{remote=true;for(IceCandidate c:candidates)pc.addIceCandidate(c);candidates.clear();pc.createAnswer(new Observer(){public void onCreateSuccess(SessionDescription answer){enqueue(()->pc.setLocalDescription(new Observer(){public void onSetSuccess(){try{send(new JSONObject().put("type","answer").put("sdp",answer.description));}catch(Exception ignored){}}},answer));}},new MediaConstraints());});}},new SessionDescription(SessionDescription.Type.OFFER,d.getString("sdp")));
 }catch(Exception e){status("Browser stream negotiation failed");}});}
 public void volume(float value){enqueue(()->{volume=value;if(audio!=null)audio.setVolume(volume);});}
 public void onTrack(RtpTransceiver t){enqueue(()->{MediaStreamTrack track=t.getReceiver().track();if(track instanceof VideoTrack){if(video!=null)video.removeSink(this);video=(VideoTrack)track;video.addSink(this);}if(track instanceof AudioTrack){audio=(AudioTrack)track;audio.setEnabled(true);audio.setVolume(volume);}});}
 public synchronized void onFrame(VideoFrame frame){
  if(closed)return;VideoFrame.I420Buffer buffer=frame.getBuffer().toI420();if(buffer==null)return;
  try{int w=buffer.getWidth(),h=buffer.getHeight(),size=w*h+2*((w+1)/2)*((h+1)/2);if(pending==null){pending=spare;spare=null;}if(pending==null||pending.capacity()!=size)pending=ByteBuffer.allocateDirect(size);pending.clear();YuvHelper.I420Copy(buffer.getDataY(),buffer.getStrideY(),buffer.getDataU(),buffer.getStrideU(),buffer.getDataV(),buffer.getStrideV(),pending,w,h);pendingWidth=w;pendingHeight=h;}finally{buffer.release();}
 }
 public synchronized ByteBuffer takeFrame(){if(pending==null)return null;spare=front;front=pending;pending=null;width=pendingWidth;height=pendingHeight;return front;}
 public synchronized int width(){return width;}public synchronized int height(){return height;}
 public void onIceCandidate(IceCandidate c){try{send(new JSONObject().put("candidate",c.sdp).put("sdpMid",c.sdpMid).put("sdpMLineIndex",c.sdpMLineIndex));}catch(Exception ignored){}}
 public void onConnectionChange(PeerConnection.PeerConnectionState s){if(s==PeerConnection.PeerConnectionState.CONNECTED)status("Shared browser video and audio connected");if(s==PeerConnection.PeerConnectionState.FAILED)status("Browser connection failed; this network may need TURN. Close and reopen the browser to retry.");}
 public void onSignalingChange(PeerConnection.SignalingState s){}public void onIceConnectionChange(PeerConnection.IceConnectionState s){}public void onIceConnectionReceivingChange(boolean b){}public void onIceGatheringChange(PeerConnection.IceGatheringState s){}public void onIceCandidatesRemoved(IceCandidate[] c){}public void onAddStream(MediaStream s){}public void onRemoveStream(MediaStream s){}public void onDataChannel(DataChannel c){}public void onRenegotiationNeeded(){}public void onAddTrack(RtpReceiver r,MediaStream[] s){}
 public void close(){enqueue(()->{closed=true;if(video!=null)video.removeSink(this);if(pc!=null)pc.dispose();if(factory!=null)factory.dispose();if(device!=null)device.release();if(egl!=null)egl.release();synchronized(this){pending=null;front=null;spare=null;}});worker.shutdown();}
}
