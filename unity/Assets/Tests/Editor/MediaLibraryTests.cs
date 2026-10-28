using System;
using NUnit.Framework;

namespace StremioVR.Tests {
public class MediaLibraryTests {
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
