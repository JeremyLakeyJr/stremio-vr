using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace StremioVR {
[DefaultExecutionOrder(-100)]
public sealed class AvatarPresence : MonoBehaviour {
    public RoomClient Room; public Transform Head, Left, Right, Rig;
    readonly Dictionary<string, Avatar> avatars = new Dictionary<string, Avatar>();
    float nextPose; float originHeight;
    public void Initialize(RoomClient room, Transform rig, Transform head, Transform left, Transform right) {
        Room = room; Rig = rig; Head = head; Left = left; Right = right; room.Message += OnMessage;
    }
    public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color) {
        var go = GameObject.CreatePrimitive(type); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>()); else Object.DestroyImmediate(go.GetComponent<Collider>());
        var material = new Material(Shader.Find("Unlit/Color")); material.color = color; go.GetComponent<Renderer>().material = material;
        return go;
    }
    void Update() {
        Track(XRNode.Head, Head, new Vector3(0, 1.65f, 0)); Track(XRNode.LeftHand, Left, new Vector3(-0.3f, 1.2f, 0.3f)); Track(XRNode.RightHand, Right, new Vector3(0.3f, 1.2f, 0.3f));
        foreach (var avatar in avatars.Values) avatar.Update();
        if (Room.IsSolo || !Room.Connected || Room.State == null || Time.unscaledTime < nextPose) return;
        nextPose = Time.unscaledTime + 0.1f;
        Room.Send(new JObject { ["type"] = "pose", ["pose"] = new JObject { ["head"] = Pose(Head), ["left"] = Pose(Left), ["right"] = Pose(Right) } });
    }
    void Start() {
        var subsystems = new List<XRInputSubsystem>(); SubsystemManager.GetSubsystems(subsystems);
        foreach (var subsystem in subsystems) if (!subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)) originHeight = 1.65f;
    }
    void Track(XRNode node, Transform target, Vector3 fallback) {
        var device = InputDevices.GetDeviceAtXRNode(node);
        target.localPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out var position) ? position + Vector3.up * originHeight : fallback;
        target.localRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation) ? rotation : Quaternion.identity;
    }
    static JObject Pose(Transform t) => new JObject { ["position"] = new JArray(t.position.x, t.position.y, t.position.z), ["rotation"] = new JArray(t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z) };
    void OnMessage(JObject message) {
        string type = (string)message["type"];
        if (type == "disconnected") { foreach (var avatar in avatars.Values) avatar.Dispose(); avatars.Clear(); return; }
        if (type == "state") {
            var ids = new HashSet<string>();
            foreach (var player in (JArray)message["players"]) {
                string id = (string)player["id"]; int index = (int)player["seat"]; Vector3 seat = new Vector3((index % 4 - 1.5f) * 1.3f, 0, -(index / 4) * 1.6f);
                if (id == Room.PlayerId) continue;
                if (!(bool)player["connected"]) continue;
                ids.Add(id); string appearance = player["profile"].ToString();
                if (avatars.TryGetValue(id, out var existing) && existing.Appearance != appearance) { existing.Dispose(); avatars.Remove(id); }
                if (!avatars.ContainsKey(id)) avatars[id] = new Avatar(transform, (JObject)player["profile"], seat);
                avatars[id].Seated((int?)player["seatedSeat"] >= 0);
                if (player["pose"] is JObject savedPose) avatars[id].Pose(savedPose);
            }
            foreach (var id in new List<string>(avatars.Keys)) if (!ids.Contains(id)) { avatars[id].Dispose(); avatars.Remove(id); }
        }
        if (type == "pose" && avatars.TryGetValue((string)message["playerId"], out var target)) { target.Pose((JObject)message["pose"]); target.Seated((bool?)message["seated"] ?? false); }
    }
    void OnDestroy() { if (Room != null) Room.Message -= OnMessage; foreach (var avatar in avatars.Values) avatar.Dispose(); }
    sealed class Avatar {
        public string Appearance; readonly GameObject root; readonly Transform head, left, right; readonly R6Avatar model;
        readonly Dictionary<Transform, Vector3> positions = new Dictionary<Transform, Vector3>(); readonly Dictionary<Transform, Quaternion> rotations = new Dictionary<Transform, Quaternion>();
        public Avatar(Transform parent, JObject profile, Vector3 seat) {
            Appearance = profile.ToString(); root = new GameObject((string)profile["name"]); root.transform.SetParent(parent);
            model = R6Avatar.Create(root.transform, profile);
            head = new GameObject("Tracked head").transform; head.SetParent(root.transform); head.position = seat + Vector3.up * 1.65f;
            left = new GameObject("Tracked left hand").transform; left.SetParent(root.transform); left.position = seat + new Vector3(-0.45f, 1.15f, 0.25f);
            right = new GameObject("Tracked right hand").transform; right.SetParent(root.transform); right.position = seat + new Vector3(0.45f, 1.15f, 0.25f);
            model.SetPose(head.position, head.rotation, left.position, right.position);
        }
        public void Seated(bool value) { model.SetSeated(value); }
        public void Pose(JObject pose) { Set(head, pose["head"]); Set(left, pose["left"]); Set(right, pose["right"]); }
        void Set(Transform target, JToken data) { if (data == null) return; var p = data["position"]; var r = data["rotation"]; positions[target] = new Vector3((float)p[0], (float)p[1], (float)p[2]); rotations[target] = Quaternion.Euler((float)r[0], (float)r[1], (float)r[2]); }
        public void Update() { float t = 1 - Mathf.Exp(-15 * Time.deltaTime); foreach (var pair in positions) { pair.Key.position = Vector3.Lerp(pair.Key.position, pair.Value, t); pair.Key.rotation = Quaternion.Slerp(pair.Key.rotation, rotations[pair.Key], t); } model.SetPose(head.position, head.rotation, left.position, right.position); }
        public void Dispose() { Object.Destroy(root); }
    }
}
}
