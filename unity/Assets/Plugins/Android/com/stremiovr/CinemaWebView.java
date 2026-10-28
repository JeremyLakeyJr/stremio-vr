package com.stremiovr;

import android.app.Activity;
import android.app.Presentation;
import android.content.Context;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.webkit.*;
import android.widget.FrameLayout;
import com.unity3d.player.UnityPlayer;
import java.nio.ByteBuffer;
import org.json.JSONObject;

/** A hardware-accelerated private virtual display, never an overlay over the XR eyes. */
public final class CinemaWebView {
    private final Activity activity;
    private final String receiver;
    private final int width, height;
    private WebView web;
    private FrameLayout layout;
    private Presentation presentation;
    private VirtualDisplay display;
    private ImageReader reader;
    private HandlerThread capture;
    private volatile byte[] frame;
    private volatile boolean closed;
    private long lastFrame;
    public CinemaWebView(Activity activity, String receiver, int width, int height) {
        this.activity=activity; this.receiver=receiver; this.width=width; this.height=height;
        activity.runOnUiThread(this::create);
    }
    private void status(String text) { UnityPlayer.UnitySendMessage(receiver,"BrowserStatus",text); }
    private void create() {
        if(closed) return;
        try {
            capture=new HandlerThread("MovieNightWebCapture"); capture.start();
            reader=ImageReader.newInstance(width,height,PixelFormat.RGBA_8888,3);
            reader.setOnImageAvailableListener(r -> {
                try(Image image=r.acquireLatestImage()) {
                    if(image==null || closed || SystemClock.uptimeMillis()-lastFrame<83) return;
                    lastFrame=SystemClock.uptimeMillis(); Image.Plane plane=image.getPlanes()[0];
                    ByteBuffer pixels=plane.getBuffer(); int stride=plane.getRowStride(), step=plane.getPixelStride();
                    if(step!=4) return;
                    byte[] next=new byte[width*height*4];
                    for(int y=0;y<height;y++) { pixels.position(y*stride); pixels.get(next,y*width*4,width*4); }
                    frame=next;
                } catch(Exception e) { /* A closing display can invalidate its last image. */ }
            },new Handler(capture.getLooper()));
            DisplayManager manager=(DisplayManager)activity.getSystemService(Context.DISPLAY_SERVICE);
            display=manager.createVirtualDisplay("Movie Night Stremio",width,height,160,reader.getSurface(),DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY|DisplayManager.VIRTUAL_DISPLAY_FLAG_PRESENTATION);
            if(display==null) throw new IllegalStateException("Virtual display unavailable");
            presentation=new Presentation(activity,display.getDisplay());
            presentation.getWindow().addFlags(WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
            layout=new FrameLayout(presentation.getContext());
            web=new WebView(presentation.getContext());
            WebSettings settings=web.getSettings(); settings.setJavaScriptEnabled(true); settings.setDomStorageEnabled(true); settings.setMediaPlaybackRequiresUserGesture(false); settings.setAllowFileAccess(false); settings.setAllowContentAccess(false);
            settings.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);
            web.setWebViewClient(new WebViewClient() {
                @Override public boolean shouldOverrideUrlLoading(WebView view,WebResourceRequest request) { String scheme=request.getUrl().getScheme(); return !("https".equals(scheme)||"http".equals(scheme)); }
                @Override public void onPageFinished(WebView view,String url) { status("Stremio Web ready. Point at the screen and pull the trigger."); }
                @Override public void onReceivedError(WebView view,WebResourceRequest req,WebResourceError error) { if(req.isForMainFrame()) status("Web page failed: "+error.getDescription()); }
            });
            web.setWebChromeClient(new WebChromeClient() {
                View fullscreen;
                @Override public void onShowCustomView(View view,CustomViewCallback callback) { if(fullscreen!=null) {callback.onCustomViewHidden();return;} fullscreen=view; layout.addView(view,new FrameLayout.LayoutParams(-1,-1)); }
                @Override public void onHideCustomView() { if(fullscreen!=null) {layout.removeView(fullscreen);fullscreen=null;} }
            });
            layout.addView(web,new FrameLayout.LayoutParams(-1,-1)); presentation.setContentView(layout); presentation.show();
            web.loadUrl("https://web.stremio.com/"); status("Loading Stremio Web…");
        } catch(Exception e) { status("Web screen unavailable: "+e.getClass().getSimpleName()); release(); }
    }
    public synchronized byte[] takeFrame() { byte[] result=frame; frame=null; return result; }
    public void pointer(float x,float y,int action) { activity.runOnUiThread(()-> { if(web==null||closed)return; long t=SystemClock.uptimeMillis(); MotionEvent event=MotionEvent.obtain(t,t,action,x*width,y*height,0); layout.dispatchTouchEvent(event);event.recycle(); }); }
    public void scroll(float amount) { activity.runOnUiThread(()-> {if(web!=null&&!closed) web.evaluateJavascript("window.scrollBy(0,"+(int)amount+");document.elementFromPoint("+(width/2)+","+(height/2)+")?.scrollBy?.(0,"+(int)amount+")",null);}); }
    public void text(String value) { activity.runOnUiThread(()-> { if(web==null||closed)return; String quoted=JSONObject.quote(value); web.evaluateJavascript("(()=>{const e=document.activeElement;if(!e||!['INPUT','TEXTAREA'].includes(e.tagName))return;const p=e.tagName==='INPUT'?HTMLInputElement.prototype:HTMLTextAreaElement.prototype;Object.getOwnPropertyDescriptor(p,'value').set.call(e,"+quoted+");e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()",null); }); }
    public void back() { activity.runOnUiThread(()->{if(web!=null&&!closed&&web.canGoBack())web.goBack();}); }
    public void reload() { activity.runOnUiThread(()->{if(web!=null&&!closed)web.reload();}); }
    public void shareVideo() { activity.runOnUiThread(()-> { if(web==null||closed)return; web.evaluateJavascript("(()=>{const v=document.querySelector('video');return v?JSON.stringify({url:v.currentSrc,title:document.title,position:v.currentTime,paused:v.paused}):null})()",value->UnityPlayer.UnitySendMessage(receiver,"BrowserVideo",value)); }); }
    public void close() { closed=true; activity.runOnUiThread(this::release); }
    private void release() { if(web!=null){web.stopLoading();web.destroy();web=null;} if(presentation!=null){presentation.dismiss();presentation=null;} if(display!=null){display.release();display=null;} if(reader!=null){reader.close();reader=null;} if(capture!=null){capture.quitSafely();capture=null;} frame=null; }
}
