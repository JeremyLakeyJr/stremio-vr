using System;
using NUnit.Framework;

namespace StremioVR.Tests {
public class MediaLibraryTests {
    [Test] public void SoloDirectPlaybackNeedsNoCoreEndpoint(){
        const string url="https://media.example/movie.mp4?token=a%2Fb&expires=42";
        Assert.AreEqual(url,MediaLibrary.DirectStream(Newtonsoft.Json.Linq.JObject.Parse("{\"url\":\""+url+"\"}")));
        Assert.AreEqual(url,CinemaPlayback.PlaybackUrl(url,null));
        Assert.Throws<Exception>(()=>CinemaPlayback.PlaybackUrl("/media/token/index.m3u8",null));
    }
    [Test] public void SoloDoesNotPretendToResolveTorrentsOrRequiredProxyHeaders(){
        Assert.Throws<Exception>(()=>MediaLibrary.DirectStream(Newtonsoft.Json.Linq.JObject.Parse("{\"infoHash\":\"0123456789abcdef\"}")));
        Assert.Throws<Exception>(()=>MediaLibrary.DirectStream(Newtonsoft.Json.Linq.JObject.Parse("{\"url\":\"https://media.example/video\",\"behaviorHints\":{\"proxyHeaders\":{}}}")));
    }

    [Test] public void DirectStreamPreservesSignedQuery() {
        const string url = "https://media.example/movie.mp4?token=a%2Fb&expires=42";
        Assert.AreEqual(url, MediaLibrary.Http(url));
    }
    [TestCase("file:///tmp/movie.mp4")]
    [TestCase("javascript:alert(1)")]
    [TestCase("https://user:secret@example.com/movie.mp4")]
    public void UnsafeDirectUrlIsRejected(string url) { Assert.Throws<Exception>(() => MediaLibrary.Http(url)); }
}
}
