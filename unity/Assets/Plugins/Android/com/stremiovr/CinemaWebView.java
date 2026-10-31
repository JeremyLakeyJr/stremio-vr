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
    private final java.util.ArrayList<WebView> tabs=new java.util.ArrayList<>();
    private int activeTab;
    private volatile String tabState="[]";
    public String tabs(){return tabState;}
    private void publishTabs(){org.json.JSONArray rows=new org.json.JSONArray();for(int i=0;i<tabs.size();i++){WebView tab=tabs.get(i);JSONObject row=new JSONObject();try{row.put("index",i);row.put("active",tab==web);row.put("title",tab.getTitle()==null?"New tab":tab.getTitle());row.put("url",tab.getUrl()==null?"":tab.getUrl());}catch(Exception ignored){}rows.put(row);}tabState=rows.toString();}
    private FrameLayout layout;
    private Presentation presentation;
    private VirtualDisplay display;
    private ImageReader reader;
    private HandlerThread capture;
    private volatile byte[] frame;
    private volatile boolean closed;
    private long lastFrame;
    private boolean restored;
    private int sessionEpoch;
    private final Handler sessionHandler=new Handler(android.os.Looper.getMainLooper());
    private boolean official(){return web!=null && web.getUrl()!=null && "https".equals(android.net.Uri.parse(web.getUrl()).getScheme()) && "web.stremio.com".equals(android.net.Uri.parse(web.getUrl()).getHost());}
    private void remember(Runnable done){
        if(!official()){if(done!=null)done.run();return;}
        final int epoch=sessionEpoch;web.evaluateJavascript("location.origin==='https://web.stremio.com'?localStorage.getItem('profile'):null",value->{try{
            if(epoch!=sessionEpoch)return;Object parsed=new org.json.JSONTokener(value).nextValue();if(parsed==JSONObject.NULL)return;
            String profile=parsed instanceof String?(String)new org.json.JSONTokener(value).nextValue():"";
            if(profile.length()<262144 && !profile.isEmpty()){
                JSONObject auth=new JSONObject(profile).optJSONObject("auth");
                if(auth!=null&&!auth.optString("key").isEmpty())SecureSession.saveNamed(activity,"web-profile",profile);
                else SecureSession.clearNamed(activity,"web-profile");
            }else if(profile.isEmpty())SecureSession.clearNamed(activity,"web-profile");
            CookieManager.getInstance().flush();
        }catch(Exception ignored){}finally{if(done!=null)done.run();}});
    }
    private final Runnable rememberLoop=new Runnable(){public void run(){if(closed)return;remember(null);sessionHandler.postDelayed(this,5000);}};
    private void restore(){
        if(restored||!official())return;restored=true;
        try{WebView restoring=web;String saved=SecureSession.loadNamed(activity,"web-profile");if(!saved.isEmpty())restoring.evaluateJavascript("(()=>{if(location.origin!=='https://web.stremio.com')return false;const existing=localStorage.getItem('profile');if(existing!==null)return false;localStorage.setItem('profile',"+JSONObject.quote(saved)+");return true})()",value->{if(!closed&&tabs.contains(restoring)&&"true".equals(value))restoring.reload();});}catch(Exception ignored){}
        sessionHandler.removeCallbacks(rememberLoop);sessionHandler.postDelayed(rememberLoop,5000);
    }
    public void forgetSession(){activity.runOnUiThread(()->{
        sessionEpoch++;SecureSession.clearNamed(activity,"web-profile");WebStorage.getInstance().deleteOrigin("https://web.stremio.com");
        for(WebView tab:tabs)tab.evaluateJavascript("(()=>{if(location.origin==='https://web.stremio.com'){localStorage.removeItem('profile');location.reload();}})()",null);
        CookieManager.getInstance().removeAllCookies(null);CookieManager.getInstance().flush();status("Browser sign-in cleared.");
    });}

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
                    if(image==null || closed || SystemClock.uptimeMillis()-lastFrame<33) return;
                    lastFrame=SystemClock.uptimeMillis(); Image.Plane plane=image.getPlanes()[0];
                    ByteBuffer pixels=plane.getBuffer(); int stride=plane.getRowStride(), step=plane.getPixelStride();
                    if(step!=4) return;
                    byte[] next=new byte[width*height*4];
                    for(int y=0;y<height;y++) { pixels.position(y*stride); pixels.get(next,y*width*4,width*4); }
                    frame=next;
                } catch(Exception e) { /* A closing display can invalidate its last image. */ }
            },new Handler(capture.getLooper()));
            DisplayManager manager=(DisplayManager)activity.getSystemService(Context.DISPLAY_SERVICE);
            display=manager.createVirtualDisplay("Movie Night Browser",width,height,160,reader.getSurface(),DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY|DisplayManager.VIRTUAL_DISPLAY_FLAG_PRESENTATION);
            if(display==null) throw new IllegalStateException("Virtual display unavailable");
            presentation=new Presentation(activity,display.getDisplay());
            presentation.getWindow().addFlags(WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
            layout=new FrameLayout(presentation.getContext());
            presentation.setContentView(layout); presentation.show();
            addTab("https://web.stremio.com/");

        } catch(Exception e) { status("Web screen unavailable: "+e.getClass().getSimpleName()); release(); }
    }
    private String safeUrl(String value){
        String url=value==null?"":value.trim();if(url.isEmpty())return "https://web.stremio.com/";
        if(!url.contains("://"))url="https://"+url;
        android.net.Uri uri=android.net.Uri.parse(url);String scheme=uri.getScheme();
        return ("https".equals(scheme)||"http".equals(scheme))&&uri.getHost()!=null&&uri.getUserInfo()==null?url:null;
    }
    private void activate(int index){
        if(closed||index<0||index>=tabs.size())return;
        if(web!=null){remember(null);web.evaluateJavascript("document.querySelectorAll('video,audio').forEach(v=>v.pause())",null);web.onPause();}
        layout.removeAllViews();activeTab=index;web=tabs.get(index);web.onResume();layout.addView(web,new FrameLayout.LayoutParams(-1,-1));restored=false;restore();publishTabs();
    }
    private void addTab(String value){
        if(closed||layout==null)return;if(tabs.size()>=6){status("Six tabs maximum. Close a tab first.");return;}
        String url=safeUrl(value);if(url==null){status("Enter an HTTP or HTTPS address.");return;}
        WebView previous=web;web=new WebView(presentation.getContext());
            WebSettings settings=web.getSettings(); settings.setJavaScriptEnabled(true); settings.setDomStorageEnabled(true); settings.setMediaPlaybackRequiresUserGesture(false); settings.setAllowFileAccess(false); settings.setAllowContentAccess(false);
            CookieManager.getInstance().setAcceptCookie(true);
            settings.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);
            web.setWebViewClient(new WebViewClient() {
                @Override public boolean shouldOverrideUrlLoading(WebView view,WebResourceRequest request) { String scheme=request.getUrl().getScheme(); return !("https".equals(scheme)||"http".equals(scheme)); }
                @Override public void onPageFinished(WebView view,String url) { if(view==web)restore(); publishTabs(); }
                @Override public void onReceivedError(WebView view,WebResourceRequest req,WebResourceError error) { if(req.isForMainFrame()) status("Web page failed: "+error.getDescription()); }
            });
            web.setWebChromeClient(new WebChromeClient() {
                View fullscreen;
                @Override public void onReceivedTitle(WebView view,String title){publishTabs();}
                @Override public void onShowCustomView(View view,CustomViewCallback callback) { if(fullscreen!=null) {callback.onCustomViewHidden();return;} fullscreen=view; layout.addView(view,new FrameLayout.LayoutParams(-1,-1)); }
                @Override public void onHideCustomView() { if(fullscreen!=null) {layout.removeView(fullscreen);fullscreen=null;} }
            });

        WebView added=web;web=previous;tabs.add(added);activate(tabs.size()-1);added.loadUrl(url);publishTabs();
    }
    public void newTab(String url){activity.runOnUiThread(()->addTab(url));}
    public void selectTab(int index){activity.runOnUiThread(()->activate(index));}
    public void closeTab(int index){activity.runOnUiThread(()->{if(closed||index<0||index>=tabs.size())return;WebView removed=tabs.get(index);if(removed==web){remember(null);layout.removeAllViews();web=null;}tabs.remove(index);removed.stopLoading();removed.destroy();if(tabs.isEmpty())addTab("https://web.stremio.com/");else activate(web!=null?tabs.indexOf(web):Math.min(index,tabs.size()-1));});}
    public void navigate(String value){activity.runOnUiThread(()->{String url=safeUrl(value);if(url==null){status("Enter an HTTP or HTTPS address.");return;}if(web!=null&&!closed){remember(null);restored=false;web.loadUrl(url);}});}
    public void forward(){activity.runOnUiThread(()->{if(web!=null&&!closed&&web.canGoForward())web.goForward();});}
    public synchronized byte[] takeFrame() { byte[] result=frame; frame=null; return result; }
    public void pointer(float x,float y,int action) { activity.runOnUiThread(()-> { if(web==null||closed)return; long t=SystemClock.uptimeMillis(); MotionEvent event=MotionEvent.obtain(t,t,action,x*width,y*height,0); layout.dispatchTouchEvent(event);event.recycle(); }); }
    public void scroll(float amount) { activity.runOnUiThread(()-> {if(web!=null&&!closed) web.evaluateJavascript("window.scrollBy(0,"+(int)amount+");document.elementFromPoint("+(width/2)+","+(height/2)+")?.scrollBy?.(0,"+(int)amount+")",null);}); }
    public void text(String value) { activity.runOnUiThread(()-> { if(web==null||closed)return; String quoted=JSONObject.quote(value); web.evaluateJavascript("(()=>{const e=document.activeElement;if(!e||!['INPUT','TEXTAREA'].includes(e.tagName))return;const p=e.tagName==='INPUT'?HTMLInputElement.prototype:HTMLTextAreaElement.prototype;Object.getOwnPropertyDescriptor(p,'value').set.call(e,"+quoted+");e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()",null); }); }
    public void back() { activity.runOnUiThread(()->{if(web!=null&&!closed&&web.canGoBack())web.goBack();}); }
    public void reload() { activity.runOnUiThread(()->{if(web!=null&&!closed)web.reload();}); }
    public void shareVideo() { activity.runOnUiThread(()-> { if(web==null||closed)return; web.evaluateJavascript("(()=>{const v=document.querySelector('video');return v?JSON.stringify({url:v.currentSrc,title:document.title,position:v.currentTime,paused:v.paused}):null})()",value->UnityPlayer.UnitySendMessage(receiver,"BrowserVideo",value)); }); }
    public void close() { closed=true; activity.runOnUiThread(()->{sessionHandler.removeCallbacks(rememberLoop);remember(this::release);sessionHandler.postDelayed(this::release,1000);}); }
    private void release() { for(WebView tab:tabs){tab.stopLoading();tab.destroy();}tabs.clear();web=null; if(presentation!=null){presentation.dismiss();presentation=null;} if(display!=null){display.release();display=null;} if(reader!=null){reader.close();reader=null;} if(capture!=null){capture.quitSafely();capture=null;} frame=null; }
}
