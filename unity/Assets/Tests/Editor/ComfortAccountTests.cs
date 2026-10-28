using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace StremioVR.Tests {
public class ComfortAccountTests {
 [Test] public void PointerCorrectionTiltsDownFortyFiveDegrees() { var direction=RigMotion.RayDirection(Quaternion.identity,45); Assert.Less(direction.y,-0.7f); Assert.Greater(direction.z,0.7f); Assert.AreEqual(1,direction.magnitude,0.001f); }
 [Test] public void ReclinedViewLevelsHeadWithoutMovingIt() {
  var root=new GameObject("Rig"); var tracking=new GameObject("Tracking").transform; tracking.SetParent(root.transform); var head=new GameObject("Head").transform; head.SetParent(tracking); head.localPosition=new Vector3(0,0.9f,0.2f); head.localRotation=Quaternion.Euler(-70,25,10);
  var motion=root.AddComponent<RigMotion>(); motion.Rig=root.transform; motion.Tracking=tracking; motion.Head=head; var before=head.position; motion.CalibrateReclined();
  Assert.Less(Vector3.Distance(before,head.position),0.001f); Assert.Less(Mathf.Abs(head.forward.y),0.001f); motion.AdjustView(5); Assert.Less(Vector3.Distance(before,head.position),0.001f); UnityEngine.Object.DestroyImmediate(root);
 }
 [Test] public void CinemaHasThirtyTwoChairsAndLandscapeScreen() { var world=Resources.Load<GameObject>("Worlds/Cinema").GetComponent<CinemaWorldData>(); Assert.AreEqual(32,world.Seats.Length); Assert.Greater(world.ScreenSize.x,world.ScreenSize.y); Assert.Greater(world.ScreenPosition.z,0); }
 [Test] public void AccountImportKeepsConfiguredAddonPath() {
  var addon=MediaLibrary.AccountAddon(JObject.Parse("{\"transportUrl\":\"https://example.com/config/manifest.json\",\"manifest\":{\"id\":\"test\",\"resources\":[\"stream\"]}}")); Assert.AreEqual("https://example.com/config",(string)addon["endpoint"]);
 }
 [Test] public void ConfiguredAddonQuerySurvivesResourceRouting() {
  const string manifest="https://example.com/config/manifest.json?key=a%2Fb%2F";
  string endpoint=MediaLibrary.AddonEndpoint(manifest);
  Assert.AreEqual("https://example.com/config/catalog/movie/top.json?key=a%2Fb%2F",MediaLibrary.ResourceUrl(endpoint,"catalog/movie/top.json"));
  Assert.AreEqual(manifest,MediaLibrary.ResourceUrl(endpoint,"manifest.json"));
 }
 [TestCase("javascript:alert(1)")][TestCase("https://stremio.com.evil.example/login")][TestCase("http://link.stremio.com/login")]
 public void SignInOnlyOpensOfficialHttpsLinks(string value) { Assert.IsFalse(StremioAccount.OfficialLink(value)); }
 [TestCase("https://cinema.example", "wss://cinema.example/ws")]
 [TestCase("http://127.0.0.1:3000", "ws://127.0.0.1:3000/ws")]
 public void HostingUrlsBecomeWebSocketEndpoints(string input,string expected) { Assert.AreEqual(expected,RoomClient.NormalizeEndpoint(input)); }
 [Test] public void WatchHistoryFiltersUnwatchedAndSortsNewestFirst() {
  var items=JArray.Parse("[{\"_id\":\"old\",\"state\":{\"lastWatched\":\"2025-01-01T00:00:00Z\"}},{\"_id\":\"unwatched\",\"state\":{}},{\"_id\":\"new\",\"state\":{\"lastWatched\":\"2026-01-01T00:00:00Z\",\"timeOffset\":120000}}]");
  var history=StremioAccount.WatchedItems(items); Assert.AreEqual(2,history.Count); Assert.AreEqual("new",(string)history[0]["_id"]);
 }
 [Test] public void OfficialStremioDeviceLinkIsAccepted() { Assert.IsTrue(StremioAccount.OfficialLink("https://link.stremio.com/?code=test")); }
}
}
