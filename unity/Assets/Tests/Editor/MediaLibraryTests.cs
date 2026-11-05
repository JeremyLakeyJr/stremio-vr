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
    [Test] public void RoomPlaybackResolvesGatewayWithoutDuplicatingPath(){
        Assert.AreEqual("https://room.example/media/token/index.m3u8",CinemaPlayback.PlaybackUrl("/media/token/index.m3u8","https://room.example/core"));
    }
    [Test] public void ProviderSecretsAreSeparatedWithoutMutatingInMemorySettings(){
        var settings=Newtonsoft.Json.Linq.JObject.Parse("{\"jellyfinPass\":\"secret\",\"siloUser\":\"token\",\"moveSpeed\":2}");
        var saved=ProviderSettings.PublicSettings(settings);
        Assert.IsNull(saved["jellyfinPass"]);Assert.IsNull(saved["siloUser"]);
        Assert.AreEqual(2,(int)saved["moveSpeed"]);
        Assert.AreEqual("secret",(string)ProviderSettings.Secrets(settings)["jellyfinPass"]);
        Assert.AreEqual("secret",(string)settings["jellyfinPass"]);
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
