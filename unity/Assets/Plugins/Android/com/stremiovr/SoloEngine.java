package com.stremiovr;

import android.app.Activity;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.webkit.WebResourceRequest;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;

/** Local game/state rules only. No socket, HTTP server, remote page, or network access. */
public final class SoloEngine {
    private final Activity activity; private final String receiver,initial;
    private WebView view; private volatile boolean closed; private boolean ready;
    private final java.util.ArrayList<String> pending=new java.util.ArrayList<>();
    public SoloEngine(Activity activity,String receiver,String initial){this.activity=activity;this.receiver=receiver;this.initial=initial;activity.runOnUiThread(this::create);}
    private void create(){
        if(closed)return;
        try(InputStream stream=activity.getAssets().open("solo.js");ByteArrayOutputStream output=new ByteArrayOutputStream()){
            byte[] bytes=new byte[8192];int n;while((n=stream.read(bytes))!=-1)output.write(bytes,0,n);
            String source=output.toString("UTF-8").replace("</script","<\\/script");
            view=new WebView(activity);view.getSettings().setJavaScriptEnabled(true);view.getSettings().setBlockNetworkLoads(true);view.getSettings().setAllowFileAccess(false);view.getSettings().setAllowContentAccess(false);view.getSettings().setDomStorageEnabled(false);
            view.addJavascriptInterface(new Object(){@JavascriptInterface public void publish(String json){if(!closed&&json.length()<1048576)UnityPlayer.UnitySendMessage(receiver,"SoloMessage",json);}},"MovieNightBridge");
            view.setWebViewClient(new WebViewClient(){
                @Override public boolean shouldOverrideUrlLoading(WebView v,WebResourceRequest r){return true;}
                @Override public void onPageFinished(WebView v,String url){if(closed||ready)return;ready=true;
                    view.evaluateJavascript("globalThis.solo=new MovieNightSolo(JSON.parse("+JSONObject.quote(initial)+"),value=>MovieNightBridge.publish(JSON.stringify(value)));solo.start();",null);
                    for(String command:pending)dispatch(command);pending.clear();
                }
            });
            view.loadDataWithBaseURL("https://solo.movie-night.invalid/","<!doctype html><meta charset='utf-8'><script>"+source+"</script>","text/html","UTF-8",null);
        }catch(Exception error){if(!closed)UnityPlayer.UnitySendMessage(receiver,"SoloFailure","Unable to start solo rules: "+error.getClass().getSimpleName());}
    }
    private void dispatch(String json){if(view!=null&&!closed)view.evaluateJavascript("solo.command(JSON.parse("+JSONObject.quote(json)+"))",null);}
    public void send(String json){activity.runOnUiThread(()->{if(closed)return;if(ready)dispatch(json);else if(pending.size()<64)pending.add(json);});}
    public void close(){closed=true;activity.runOnUiThread(()->{pending.clear();if(view!=null){view.removeJavascriptInterface("MovieNightBridge");view.stopLoading();view.destroy();view=null;}});}
}
