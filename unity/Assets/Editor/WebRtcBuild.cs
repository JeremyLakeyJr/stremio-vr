using System.IO;
using UnityEditor.Android;
namespace StremioVR.Editor {
public sealed class WebRtcBuild:IPostGenerateGradleAndroidProject {
 public int callbackOrder=>10;
 public void OnPostGenerateGradleAndroidProject(string path) {
  string file=Path.Combine(path,"build.gradle"),text=File.ReadAllText(file);
  const string dependency="implementation 'io.github.webrtc-sdk:android:144.7559.15'";
  if(!text.Contains(dependency))File.WriteAllText(file,text.Replace("dependencies {","dependencies {\n    "+dependency));
 }
}
}
