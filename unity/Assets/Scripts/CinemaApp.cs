using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using StremioVR.Providers;

namespace StremioVR {
public sealed class CinemaApp : MonoBehaviour {
    int layoutPageRevision=-1; RoomPlacement placement; TabletopGames games; LocalBody localBody; WebCinema browser; RoomClient room; CinemaPlayback playback; VoiceChat voice; MediaLibrary library; StremioAccount account; RigMotion motion; WorldManager world; Transform tracking; VideoAmbient ambient; PanelMotion panelMotion; float scrollTarget; bool historyContinue, historyLocal; JObject watching; string watchingUrl; float nextHistory;
    Transform rig, head, left, right, panel; Camera cameraView; Canvas canvas; Text status, members;
    AvatarPresence presence;
    RectTransform content; Font font; JObject settings, profile; float row; int pageGeneration; float actionWidth = 920; Text pageTitle;
    InputField serverInput, roomInput, streamInput; readonly List<RaycastResult> hits = new List<RaycastResult>();
    bool triggerWasDown; string shownRoom; GameObject hovered; LineRenderer pointer;
    float rightALongPressTimer; bool rightALongPressFired; bool menuWasDown;
    string providerFilter = "all"; int serverHubTab = 0;

    public static readonly string[] MenuButtonOptions = new string[] {
        "Left Controller Menu Button",
        "Left Controller Y Button",
        "Left Controller X Button",
        "Right Controller A Button",
        "Right Controller B Button",
        "Right Controller A (Hold 0.4s)",
        "Left Stick Click"
    };

    string SettingsPath => Path.Combine(Application.persistentDataPath, "profile.json");

    void Awake() {
        Application.targetFrameRate = 72; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        try { settings = JObject.Parse(File.ReadAllText(SettingsPath)); } catch { settings = new JObject(); }
        profile = settings["profile"] as JObject ?? new JObject { ["name"] = "Guest", ["skin"] = "#b98059", ["color"] = "#7958ff", ["head"] = "round", ["body"] = "regular", ["hair"] = "none", ["outfit"] = "casual", ["accessory"] = "none" };
        room = gameObject.AddComponent<RoomClient>(); library = gameObject.AddComponent<MediaLibrary>();
        var provReg = gameObject.AddComponent<MediaProviderRegistry>();
        gameObject.AddComponent<WatchProgressSync>();
        string jUrl = (string)settings[SettingsKeys.JellyfinUrl];
        string jUid = (string)settings[SettingsKeys.JellyfinUserId];
        string jTok = (string)settings[SettingsKeys.JellyfinToken];
        if (!string.IsNullOrEmpty(jUrl) && !string.IsNullOrEmpty(jTok) && !string.IsNullOrEmpty(jUid)) {
            provReg.Jellyfin?.RestoreSession(jUrl, jUid, jTok);
        }
        string msUrl = (string)settings[SettingsKeys.MediaStormUrl];
        string msKey = (string)settings[SettingsKeys.MediaStormKey];
        if (!string.IsNullOrEmpty(msUrl) && !string.IsNullOrEmpty(msKey)) {
            provReg.MediaStorm?.RestoreSession(msUrl, msKey);
        }
        string sUrl = (string)settings[SettingsKeys.SiloUrl];
        string sTok = (string)settings[SettingsKeys.SiloToken];
        if (!string.IsNullOrEmpty(sUrl) && !string.IsNullOrEmpty(sTok)) {
            provReg.Silo?.RestoreSession(sUrl, sTok);
        }
        account = gameObject.AddComponent<StremioAccount>(); account.Status += ShowStatus; account.AddonsImported += ImportAccountAddons;
        account.HistoryLoaded += () => { if (pageTitle != null && pageTitle.text == "Watch history") HistoryPage(); };
        account.Changed += () => {
            if (pageTitle != null) {
                if (pageTitle.text == "Stremio account") AccountPage();
                else if (pageTitle.text == "Servers & Streaming") ServersHubPage();
                else if (pageTitle.text == "Discover") Library();
            }
        };
        voice = gameObject.AddComponent<VoiceChat>(); voice.Initialize(room);
        room.Status += ShowStatus; library.Status += ShowStatus; voice.Status += ShowStatus;
        rig = new GameObject("XR Rig").transform;
        tracking = new GameObject("Tracking space").transform; tracking.SetParent(rig, false);
        head = new GameObject("Head").transform; head.SetParent(tracking); head.localPosition = new Vector3(0, 1.65f, 0);
        cameraView = head.gameObject.AddComponent<Camera>(); cameraView.clearFlags = CameraClearFlags.SolidColor; cameraView.nearClipPlane = 0.05f; cameraView.farClipPlane = 100; cameraView.cullingMask &= ~((1 << 30) | (1 << 29)); cameraView.backgroundColor = new Color(0.02f, 0.025f, 0.05f); head.gameObject.tag = "MainCamera"; head.gameObject.AddComponent<AudioListener>();
        cameraView.allowHDR=false;
        left = new GameObject("Left controller").transform; left.SetParent(tracking);
        right = new GameObject("Right controller").transform; right.SetParent(tracking);
        presence = gameObject.AddComponent<AvatarPresence>(); presence.Initialize(room, rig, head, left, right);
        localBody=gameObject.AddComponent<LocalBody>(); localBody.Head=head; localBody.Left=left; localBody.Right=right; localBody.Room=room; localBody.Apply(profile);
        world = gameObject.AddComponent<WorldManager>(); world.Initialize(room);
        motion = gameObject.AddComponent<RigMotion>(); motion.Rig=rig; motion.Tracking=tracking; motion.Head=head; motion.Room=room; motion.World=world;
        motion.LeftHand = localBody.Left;
        motion.MoveSpeed=(float?)settings[SettingsKeys.MoveSpeed]??1.5f;
        motion.TurnSpeed=(float?)settings[SettingsKeys.TurnSpeed]??60;
        motion.RayPitch=(float?)settings[SettingsKeys.RayPitch]??45;
        motion.SnapTurn = (bool?)settings[SettingsKeys.SnapTurn] ?? false;
        motion.SnapAngle = (float?)settings[SettingsKeys.SnapAngle] ?? 45f;
        motion.HandOriented = (bool?)settings[SettingsKeys.HandOriented] ?? false;
        motion.SprintEnabled = (bool?)settings[SettingsKeys.SprintEnabled] ?? true;
        motion.TeleportMode = (bool?)settings[SettingsKeys.TeleportMode] ?? false;
        motion.MenuVisible=()=>placement!=null && placement.Active; motion.Status+=ShowStatus; motion.Initialize();
        localBody.AvatarHeightChanged += h => motion.SetAvatarHeight(h);
        ApplyBrowserRes((string)settings[SettingsKeys.BrowserRes] ?? "1080p");
        playback = gameObject.AddComponent<CinemaPlayback>(); playback.Initialize(room, world.Screen); playback.Status += ShowStatus;
        ambient=gameObject.AddComponent<VideoAmbient>(); ambient.World=world; ambient.Video=playback.Video; ambient.Screen=world.Screen.transform; ambient.Enabled=(bool?)settings[SettingsKeys.Ambient]??true; ambient.Strength=(float?)settings[SettingsKeys.AmbientStrength]??0.65f;
        world.Changed+=()=>{ playback.SetScreen(world.Screen); ambient.Screen=world.Screen.transform; motion.ResetWorld(); Recenter(); };
        int savedEnv = Mathf.Clamp((int?)settings[SettingsKeys.EnvMode] ?? 0, 0, 2);
        world.SetEnvironmentMode((WorldManager.EnvironmentMode)savedEnv);
        int savedStereo = Mathf.Clamp((int?)settings[SettingsKeys.StereoMode] ?? 0, 0, 2);
        bool savedSwap = (bool?)settings[SettingsKeys.SwapEyes] ?? false;
        world.SetStereoMode(savedStereo, savedSwap);
        float savedBr = (float?)settings[SettingsKeys.ScreenBrightness] ?? 1.0f;
        float savedCo = (float?)settings[SettingsKeys.ScreenContrast] ?? 1.0f;
        float savedSa = (float?)settings[SettingsKeys.ScreenSaturation] ?? 1.0f;
        world.SetPictureAdjustments(savedBr, savedCo, savedSa);
        int savedHdr = Mathf.Clamp((int?)settings[SettingsKeys.HdrMode] ?? 0, 0, 3);
        world.SetHdrMode(savedHdr);
        bool savedBed = (bool?)settings[SettingsKeys.BedMode] ?? false;
        world.SetBedMode(savedBed);
        float savedCurv = (float?)settings[SettingsKeys.ScreenCurvature] ?? 0f;
        if (savedCurv > 0) world.SetCurvature(savedCurv);
        browser=gameObject.AddComponent<WebCinema>(); browser.Room=room; browser.Playback=playback; browser.World=world; browser.Ambient=ambient; browser.Status+=ShowStatus; browser.Initialize();
        games=gameObject.AddComponent<TabletopGames>();games.Initialize(room,world);games.Status+=ShowStatus;
        placement=gameObject.AddComponent<RoomPlacement>();placement.Room=room;placement.Head=head;placement.Status+=ShowStatus;
        placement.Finished+=()=>{panelMotion.Show(true);Recenter();LayoutPage();};
        serverHubTab = Mathf.Clamp((int?)settings[SettingsKeys.LastServerTab] ?? 0, 0, 5);
        BuildCanvas();
        if (settings[SettingsKeys.HasOnboarded] == null && !account.SignedIn && (settings["addons"] as JArray == null || (settings["addons"] as JArray).Count == 0)) {
            OnboardingWizard();
        } else {
            Library();
        }
        room.Message += message => {
            if ((string)message["type"] == "joined") { library.CoreEndpoint = room.ApiEndpoint; if(!room.IsSolo)library.ValidateInstalled(); }
            if ((string)message["type"] == "state") {
                var names = new List<string>(); foreach (var p in (JArray)message["players"]) names.Add((string)p["profile"]["name"] + ((string)p["id"] == (string)message["host"] ? " (host)" : "") + ((bool)p["connected"] ? "" : " (reconnecting)"));
                if (shownRoom != room.RoomId) { shownRoom = room.RoomId; Recenter(); }
                if(pageTitle.text=="Arrange room" && !placement.Active && layoutPageRevision!=((int?)message["layout"]?["revision"]??-1))LayoutPage();
                members.text = room.IsSolo?"Solo mode • Your apartment • Saved on this headset":"Room " + room.RoomId + "  •  " + string.Join(", ", names);
            }
            if ((string)message["type"] == "disconnected") {members.text = "Not connected";library.CoreEndpoint=null;}
        };
        var accountEndpoints=new HashSet<string>(); foreach(var endpoint in settings["accountAddons"] as JArray??new JArray()) accountEndpoints.Add((string)endpoint);
        foreach(var cached in settings["accountCache"] as JArray??new JArray()) if(cached is JObject addon) library.Addons.Add((JObject)addon.DeepClone());
        foreach (var addon in settings["addons"] as JArray ?? new JArray()) {
            try { if(accountEndpoints.Contains(MediaLibrary.AddonEndpoint((string)addon))) continue; } catch { }
            library.Install((string)addon, () => { if(pageTitle.text=="Discover") Library(); });
        }
        if(library.Addons.Count>0 && pageTitle.text=="Discover") Library();
    }

    void Start() {
        room.StartSolo(profile); account.Resume();
#if UNITY_ANDROID && DEVELOPMENT_BUILD
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var intent = activity.Call<AndroidJavaObject>("getIntent")) {
            string endpoint = intent.Call<string>("getStringExtra", "stremio.server");
            string code = intent.Call<string>("getStringExtra", "stremio.room");
            if (!string.IsNullOrEmpty(endpoint)) room.Connect(endpoint, code, profile);
            string initialPage=intent.Call<string>("getStringExtra", "stremio.page");
            if(initialPage=="avatar") Customize();
            if(initialPage=="history") HistoryPage();
            if(initialPage=="settings") Comfort();
            if(initialPage=="web") StartCoroutine(DevelopmentWebScreen());
            string addon = intent.Call<string>("getStringExtra", "stremio.addon");
            if (!string.IsNullOrEmpty(addon)) StartCoroutine(DevelopmentAddon(addon));
        }
#endif
    }

#if UNITY_ANDROID && DEVELOPMENT_BUILD
    IEnumerator DevelopmentWebScreen() {
        float deadline=Time.realtimeSinceStartup+20; while(!room.IsHost && Time.realtimeSinceStartup<deadline) yield return null;
        if(!room.IsHost) yield break; ChangeWorld("home"); browser.Toggle(); panelMotion.Show(false);
    }
    IEnumerator DevelopmentAddon(string url) {
        float deadline = Time.realtimeSinceStartup + 20;
        while (string.IsNullOrEmpty(library.CoreEndpoint) && Time.realtimeSinceStartup < deadline) yield return null;
        if (string.IsNullOrEmpty(library.CoreEndpoint)) yield break;
        library.Install(url, () => { SaveAddons(); Debug.Log("[StremioCore] Development add-on installed"); });
    }
#endif

    void BuildCanvas() {
        if (FindFirstObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem));
        var go = new GameObject("Cinema controls", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)); panel = go.transform;
        canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cameraView;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 1000); panel.localScale = Vector3.one * 0.0012f; panelMotion=go.AddComponent<PanelMotion>();
        Recenter();
        var background = go.AddComponent<Image>(); background.color = new Color(0.047f, 0.043f, 0.067f, 0.98f);
        Plate(panel, 0, 0, 1280, 80, new Color(0.06f,0.05f,0.1f));
        Plate(panel, 0, 80, 245, 920, new Color(0.075f,0.065f,0.13f));
        Label("Movie Night", panel, 25, 18, 235, 44, 26);
        pageTitle = Label("My room", panel, 280, 18, 750, 44, 30);
        Button("Hide", panel, 1150, 14, 105, () => panelMotion.Show(false));
        members = Label("Not connected", panel, 280, 95, 955, 65, 19);
        status = Label("Solo starts automatically. Connect a room only when you want company.", panel, 280, 825, 955, 65, 20);
        status.color = new Color(0.25f, 0.9f, 0.72f);
        Label("BROWSE & WATCH", panel, 20, 105, 215, 40, 17);
        Button("My room", panel, 15, 150, 215, Home);
        Button("Discover", panel, 15, 210, 215, Library);
        Button("Watch history", panel, 15, 270, 215, HistoryPage);
        Button("Stremio account", panel, 15, 330, 215, AccountPage);
        Button("Servers & streaming", panel, 15, 390, 215, ServersHubPage);
        Button("Avatar", panel, 15, 450, 215, Customize);
        Button("Settings / worlds", panel, 15, 510, 215, Comfort);

        Button("Toggle mic", panel, 15, 575, 215, () => voice.Toggle());
        Button("Toggle audio", panel, 15, 635, 215, () => voice.ToggleDeafen());
        Button("Recenter", panel, 15, 695, 215, Recenter);
        Label("Left stick: move / sprint\nRight stick: turn\nHold grip + stick: scroll", panel, 20, 765, 215, 95, 17);
        Plate(panel, 260, 910, 1000, 72, new Color(0.06f,0.05f,0.1f));
        Button("My room", panel, 275, 920, 220, Home); Button("Discover", panel, 510, 920, 220, Library); Button("Games", panel, 745, 920, 220, GamesPage); Button("Mic", panel, 980, 920, 260, () => voice.Toggle());
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect)); scrollGo.transform.SetParent(panel, false);
        Place(scrollGo.GetComponent<RectTransform>(), 280, 185, 950, 620); scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.05f); scrollGo.GetComponent<Mask>().showMaskGraphic = false;
        content = new GameObject("Content", typeof(RectTransform), typeof(PageFade)).GetComponent<RectTransform>(); content.SetParent(scrollGo.transform, false); Place(content, 0, 0, 930, 620);
        var scroll = scrollGo.GetComponent<ScrollRect>(); scroll.viewport = scrollGo.GetComponent<RectTransform>(); scroll.content = content; scroll.horizontal = false;
        pointer = new GameObject("Controller ray").AddComponent<LineRenderer>(); pointer.positionCount = 2; pointer.startWidth = 0.003f; pointer.endWidth = 0.001f; pointer.material = new Material(Shader.Find("Unlit/Color")); pointer.material.color = new Color(0.1f, 0.9f, 0.65f);
        var kb = go.AddComponent<VRKeyboard>(); kb.Initialize(panel, font);
    }

    public void Recenter() { panel.position = head.position + head.forward * 1.8f; panel.rotation = Quaternion.LookRotation(head.forward, head.up); }
    void Clear() { VRKeyboard.Instance?.Hide(); scrollTarget=0; content.GetComponent<PageFade>().Begin(); actionWidth = 920; pageGeneration++; foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); } row = 0; content.anchoredPosition = Vector2.zero; }

    void Heading(string text) { Label(text, content, 0, row, 920, 45, 24); row += 55; Resize(); }

    void OnboardingWizard() {
        Clear(); pageTitle.text = "Welcome to Movie Night VR";
        Heading("🎬 Welcome to Movie Night VR!");
        Label("Your personal virtual cinema with support for Stremio, Nuvio, Jellyfin, MediaStorm, and Silo.\nFollow these quick steps to get streaming in seconds:", content, 0, row, actionWidth, 65, 20); row += 75;

        Plate(content, 0, row, actionWidth, 160, new Color(0.1f, 0.12f, 0.2f));
        Label("Step 1: Connect your streaming accounts or home media servers.\nPair via link code or authenticate your server.", content, 20, row + 15, actionWidth - 40, 50, 19);
        Button("⚡ Open Servers & Accounts Hub", content, 20, row + 75, 420, ServersHubPage);
        Button("🌌 Open Nuvio Web Player", content, 460, row + 75, 420, () => { browser.Open("https://nuvio.app/"); panelMotion.Show(false); });
        row += 175;

        Plate(content, 0, row, actionWidth, 160, new Color(0.1f, 0.12f, 0.2f));
        Label("Step 2: Browse Discover or Search catalogs for movies and series.\nHardware-accelerated native playback at up to 4K 60FPS.", content, 20, row + 15, actionWidth - 40, 50, 19);
        Button("🍿 Explore Discover Catalogs", content, 20, row + 75, 420, () => { settings[SettingsKeys.HasOnboarded] = true; Save(); Library(); });
        Button("🎬 Open Stremio Web Player", content, 460, row + 75, 420, () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
        row += 175;

        Plate(content, 0, row, actionWidth, 120, new Color(0.1f, 0.12f, 0.2f));
        Label("Step 3: Enjoy alone in Solo Mode or host a watch party with friends!", content, 20, row + 15, actionWidth - 40, 40, 19);
        Button("🛋 Enter Cinema (Skip Onboarding)", content, 20, row + 60, 420, () => { settings[SettingsKeys.HasOnboarded] = true; Save(); Home(); });
        row += 135;
        Resize();
    }

    void Home() {
        Clear(); pageTitle.text = "My room"; Heading(room.IsSolo?"Solo mode — no server needed":"Room & playback");
        ActionButton("Solo mode / return to my apartment",()=>{if(browser.Active)browser.Close();room.StartSolo(profile);Home();});
        ActionButton("🌌 Open Nuvio Streaming (Web / Stremio Addons)", () => { browser.Open("https://nuvio.app/"); panelMotion.Show(false); });
        ActionButton("🎬 Open Stremio Streaming (Web Player)", () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
        ActionButton("⚡ Servers & Streaming Hub", ServersHubPage);
        Heading("Solo: browse, watch direct videos, arrange your apartment, and play locally. Connect below for multiplayer.");
        ActionButton("Web browser / native player",()=>{browser.Toggle(); if(browser.Active)panelMotion.Show(false);});
        ActionButton("Web screen controls",BrowserControls);
        streamInput = Field("Direct video URL", "", null);
        ActionButton("Load video", () => playback.Load(streamInput.text.Trim(), "Direct stream"));
        ActionButton("👓 Cinema Controls (3D SBS / Void / Bed Mode)", CinemaControlsPage);
        ActionButton("Audio & subtitles",AudioSettings);
        ActionButton("Play / pause", () => playback.Toggle());
        ActionButton("Seek −10 seconds", () => playback.Seek(-10)); ActionButton("Seek +10 seconds", () => playback.Seek(10));
        Heading("Multiplayer — connect only when inviting friends");
        serverInput = Field("Server WebSocket URL", (string)settings[SettingsKeys.Server] ?? "ws://192.168.1.100:3000/ws", value => { settings[SettingsKeys.Server] = value; Save(); });
        roomInput = Field("Room code (empty creates a room)", "", null);
        ActionButton("Connect / create room", () => { room.Connect(serverInput.text.Trim(), roomInput.text.Trim(), profile); });
        ActionButton("Leave room / return to solo", () => room.Leave());
        if (!room.IsSolo && room.Connected) {
            Heading("Room: " + (room.RoomId ?? ""));
            ActionButton("📱 Open Host Dashboard & Invite QR", () => {
                string dashUrl = room.ApiEndpoint ?? "http://localhost:3000";
                browser.Open(dashUrl);
                panelMotion.Show(false);
            });
            ActionButton("Choose a seat / sofa", SeatsPage);
            Heading("Send Reaction (Floating Emojis)");
            ActionButton("🍿 Popcorn", () => SendReaction("🍿"));
            ActionButton("😂 Laugh", () => SendReaction("😂"));
            ActionButton("👏 Clap", () => SendReaction("👏"));
            ActionButton("❤️ Love", () => SendReaction("❤️"));
            ActionButton("😱 Shock", () => SendReaction("😱"));
            ActionButton("😴 Sleep", () => SendReaction("😴"));
        }
        int curMenuOpt = Mathf.Clamp((int?)settings[SettingsKeys.MenuButtonOption] ?? 0, 0, MenuButtonOptions.Length - 1);
        ActionButton("Hide / show menu: " + MenuButtonOptions[curMenuOpt], () => panelMotion.Show(false));
    }

    void SendReaction(string emoji) {
        room.Send(new JObject { ["type"] = "reaction", ["emoji"] = emoji });
        presence?.ShowReaction(room.PlayerId, emoji);
    }

    void SeatsPage() {
        Clear(); pageTitle.text = "Choose a seat / sofa";
        Heading("Select any vacant cinema seat to teleport and snap into view:");
        int seatCount = world != null && world.Data.Seats != null ? world.Data.Seats.Length : 6;
        for (int i = 0; i < seatCount; i++) {
            int seatIdx = i;
            string occupant = null;
            if (room.State != null && room.State["players"] is JArray players) {
                foreach (var p in players) {
                    if ((int?)p["seatedSeat"] == seatIdx && (bool?)p["connected"] == true) {
                        occupant = (string)p["profile"]?["name"] ?? "Player";
                        break;
                    }
                }
            }
            if (occupant != null) {
                Heading("Chair " + (seatIdx + 1) + " · Occupied by " + occupant);
            } else {
                ActionButton("Sit in Chair " + (seatIdx + 1) + " (Vacant)", () => {
                    motion.RequestSeat(seatIdx);
                    panelMotion.Show(false);
                });
            }
        }
        ActionButton("Stand up / return to aisle", () => { motion.Stand(); SeatsPage(); });
        ActionButton("Back to my room", Home);
    }

    void GamesPage() {
        Clear();pageTitle.text="Tabletop games";
        Heading("Place tables using Arrange room. Grip a chess piece, move it, and release above a green square. Trigger also works.");
        ActionButton("Arrange room",LayoutPage);
        if(room.IsSolo)ActionButton((bool?)room.State?["computer"]==false?"Practice both sides → play computer":"Play computer → practice both sides",()=>{room.Send(new JObject{["type"]="solo-opponent",["computer"]=(bool?)room.State?["computer"]==false});ShowStatus("Opponent updated. Refresh boards to see the mode.");});
        foreach(string id in new[]{"chess","tictactoe"}) {string game=id;Heading(id=="chess"?"Chess":"Tic-tac-toe");Heading(games.Description(id));
            if(room.IsHost)ActionButton("Spawn "+id+" on open table",()=>{var placement=GetComponent<RoomPlacement>();placement.Begin(game);panelMotion.Show(false);});}
        ActionButton("Back to my room",Home);
    }

    void CinemaControlsPage() {
        Clear(); pageTitle.text = "Cinema Display & Controls";
        Heading("Fluid, Bigscreen & DeoVR Experience");

        // Environment Mode Row
        Heading("Environment Mode (Fluid / Bigscreen)");
        Plate(content, 0, row, actionWidth, 54, new Color(0.08f, 0.09f, 0.15f));
        Button((world.CurrentEnvMode == WorldManager.EnvironmentMode.Cinema ? "● " : "") + "🛋 Cinema Room", content, 5, row + 4, 300, () => {
            world.SetEnvironmentMode(WorldManager.EnvironmentMode.Cinema);
            settings[SettingsKeys.EnvMode] = 0; Save(); CinemaControlsPage();
        });
        Button((world.CurrentEnvMode == WorldManager.EnvironmentMode.Void ? "● " : "") + "🌑 The Void", content, 310, row + 4, 300, () => {
            world.SetEnvironmentMode(WorldManager.EnvironmentMode.Void);
            settings[SettingsKeys.EnvMode] = 1; Save(); CinemaControlsPage();
        });
        Button((world.CurrentEnvMode == WorldManager.EnvironmentMode.Passthrough ? "● " : "") + "🥽 Passthrough", content, 615, row + 4, 300, () => {
            world.SetEnvironmentMode(WorldManager.EnvironmentMode.Passthrough);
            settings[SettingsKeys.EnvMode] = 2; Save(); CinemaControlsPage();
        });
        row += 62;

        // Stereo 3D Mode Row
        Heading("Stereoscopic 3D (DeoVR / Bigscreen)");
        Plate(content, 0, row, actionWidth, 54, new Color(0.08f, 0.09f, 0.15f));
        Button((world.StereoMode == 0 ? "● " : "") + "2D Mono", content, 5, row + 4, 225, () => {
            world.SetStereoMode(0, world.SwapEyes);
            settings[SettingsKeys.StereoMode] = 0; Save(); CinemaControlsPage();
        });
        Button((world.StereoMode == 1 ? "● " : "") + "3D Side-by-Side", content, 235, row + 4, 225, () => {
            world.SetStereoMode(1, world.SwapEyes);
            settings[SettingsKeys.StereoMode] = 1; Save(); CinemaControlsPage();
        });
        Button((world.StereoMode == 2 ? "● " : "") + "3D Over-Under", content, 465, row + 4, 225, () => {
            world.SetStereoMode(2, world.SwapEyes);
            settings[SettingsKeys.StereoMode] = 2; Save(); CinemaControlsPage();
        });
        Button((world.SwapEyes ? "● " : "") + "Invert Eyes", content, 695, row + 4, 220, () => {
            world.SetStereoMode(world.StereoMode, !world.SwapEyes);
            settings[SettingsKeys.SwapEyes] = world.SwapEyes; Save(); CinemaControlsPage();
        });
        row += 62;

        // HDR Tone-Mapping Row
        Heading("HDR Video Tone-Mapping (BT.2020 / PQ / HLG)");
        Plate(content, 0, row, actionWidth, 54, new Color(0.08f, 0.09f, 0.15f));
        Button((world.HdrMode == 0 ? "● " : "") + "SDR Standard", content, 5, row + 4, 225, () => {
            world.SetHdrMode(0); settings[SettingsKeys.HdrMode] = 0; Save(); CinemaControlsPage();
        });
        Button((world.HdrMode == 1 ? "● " : "") + "HLG (Broadcast)", content, 235, row + 4, 225, () => {
            world.SetHdrMode(1); settings[SettingsKeys.HdrMode] = 1; Save(); CinemaControlsPage();
        });
        Button((world.HdrMode == 2 ? "● " : "") + "HDR10 (PQ/ACES)", content, 465, row + 4, 225, () => {
            world.SetHdrMode(2); settings[SettingsKeys.HdrMode] = 2; Save(); CinemaControlsPage();
        });
        Button((world.HdrMode == 3 ? "● " : "") + "Gamut Boost", content, 695, row + 4, 220, () => {
            world.SetHdrMode(3); settings[SettingsKeys.HdrMode] = 3; Save(); CinemaControlsPage();
        });
        row += 62;

        // Bed Mode & Curvature
        Heading("Viewing Position & Curvature (Fluid / DeoVR)");
        ActionButton("Bed Mode (Ceiling Projection): " + (world.BedMode ? "Active (Ceiling)" : "Standard (Wall)"), () => {
            world.SetBedMode(!world.BedMode);
            settings[SettingsKeys.BedMode] = world.BedMode; Save(); CinemaControlsPage();
        });
        SpeedControl("Screen Curvature", world.CustomCurvature, 0f, 90f, 15f, val => {
            world.SetCurvature(val);
            settings[SettingsKeys.ScreenCurvature] = val; Save();
        }, "°", CinemaControlsPage);

        // Picture Tuning
        Heading("Picture Tuning (DeoVR Calibration)");
        SpeedControl("Brightness", world.Brightness, 0.4f, 1.8f, 0.1f, val => {
            world.SetPictureAdjustments(val, world.Contrast, world.Saturation);
            settings[SettingsKeys.ScreenBrightness] = val; Save();
        }, "x", CinemaControlsPage);
        SpeedControl("Contrast", world.Contrast, 0.6f, 1.8f, 0.1f, val => {
            world.SetPictureAdjustments(world.Brightness, val, world.Saturation);
            settings[SettingsKeys.ScreenContrast] = val; Save();
        }, "x", CinemaControlsPage);
        SpeedControl("Saturation", world.Saturation, 0.0f, 2.0f, 0.1f, val => {
            world.SetPictureAdjustments(world.Brightness, world.Contrast, val);
            settings[SettingsKeys.ScreenSaturation] = val; Save();
        }, "x", CinemaControlsPage);

        // Quick playback transport
        Heading("Quick Transport");
        Plate(content, 0, row, actionWidth, 54, new Color(0.12f, 0.15f, 0.22f));
        Button("⏮ -30s", content, 10, row + 4, 170, () => playback.Seek(-30));
        Button("⏪ -10s", content, 190, row + 4, 170, () => playback.Seek(-10));
        Button("⏯ Play/Pause", content, 370, row + 4, 180, () => playback.Toggle());
        Button("⏩ +10s", content, 560, row + 4, 170, () => playback.Seek(10));
        Button("⏭ +30s", content, 740, row + 4, 170, () => playback.Seek(30));
        row += 64;

        ActionButton("Audio & Subtitle Tracks", AudioSettings);
        ActionButton("Return to My Room", Home);
        Resize();
    }

    void AudioSettings() {
        Clear(); pageTitle.text = "Audio & subtitles";
        Heading("Audio tracks");
        ActionButton(playback.AudioInfo, () => { playback.NextAudioTrack(); AudioSettings(); });
        ActionButton("Cycle server-transcoded audio track", () => {
            playback.NextServerAudioTrack();
            AudioSettings();
        });
        Heading("Subtitle controls");
        if (playback.Subtitles != null) {
            ActionButton("Subtitles: " + (playback.Subtitles.Enabled ? "Enabled" : "Disabled"), () => {
                playback.Subtitles.Toggle();
                AudioSettings();
            });
            string trackLabel = playback.Subtitles.AvailableTracks.Count > 0 
                ? (playback.Subtitles.ActiveTrackIndex >= 0 ? playback.Subtitles.AvailableTracks[playback.Subtitles.ActiveTrackIndex].Language : "None selected")
                : (string.IsNullOrEmpty(playback.Subtitles.CurrentSubtitleUrl) ? "No tracks" : "1 active track");
            ActionButton("Choose subtitle track (" + trackLabel + ")", SubtitleTracksPage);
            ActionButton("Sync offset -0.5s", () => { playback.Subtitles.AdjustOffset(-0.5); AudioSettings(); });
            ActionButton("Sync offset +0.5s", () => { playback.Subtitles.AdjustOffset(0.5); AudioSettings(); });
            ActionButton("Reset subtitle offset", () => { playback.Subtitles.ResetOffset(); AudioSettings(); });
        }
        ActionButton("Refresh audio & subtitle information", AudioSettings);
        Heading("If a source uses unsupported AC-3/DTS audio, choose an AAC source or configure a compatible transcoding service. Voice volume is separate.");
    }

    void SubtitleTracksPage() {
        Clear(); pageTitle.text = "Subtitle Tracks";
        Heading("Select Subtitle Track");
        if (playback.Subtitles == null || playback.Subtitles.AvailableTracks.Count == 0) {
            if (!string.IsNullOrEmpty(playback.Subtitles?.CurrentSubtitleUrl)) {
                ActionButton("● Current Active Track", () => SubtitleTracksPage());
                ActionButton("Turn Off Subtitles", () => { playback.Subtitles.Clear(); SubtitleTracksPage(); });
            } else {
                Heading("No subtitle tracks available for this stream.");
            }
        } else {
            ActionButton(playback.Subtitles.ActiveTrackIndex < 0 ? "● Off (Disable Subtitles)" : "Off (Disable Subtitles)", () => {
                playback.Subtitles.SelectTrack(-1);
                SubtitleTracksPage();
            });
            for (int i = 0; i < playback.Subtitles.AvailableTracks.Count; i++) {
                int trackIdx = i;
                var track = playback.Subtitles.AvailableTracks[i];
                string activeDot = playback.Subtitles.ActiveTrackIndex == trackIdx ? "● " : "";
                ActionButton(activeDot + track.Language, () => {
                    playback.Subtitles.SelectTrack(trackIdx, room.ApiEndpoint);
                    SubtitleTracksPage();
                });
            }
        }
        ActionButton("Back to Audio & Subtitles", AudioSettings);
    }

    void BrowserControls() {
        Clear(); pageTitle.text="Web browser";
        Heading(room.IsSolo?"Your private browser — account session stays on this headset":"Shared server browser — everyone in the room sees and hears it");
        ActionButton(browser.Active?"Return to native player":"Open browser on the screen",()=>{browser.Toggle();BrowserControls();});
        ActionButton("Hide menu / interact with the screen",()=>panelMotion.Show(false));
        Heading("Quick Media Web Wrappers");
        Plate(content, 0, row, actionWidth, 126, new Color(0.12f, 0.15f, 0.22f));
        Button("🎬 Stremio Web", content, 10, row + 8, 290, () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
        Button("🍿 Jellyfin Web", content, 315, row + 8, 290, () => {
            string j = (string)settings[SettingsKeys.JellyfinUrl];
            if (!string.IsNullOrEmpty(j)) { browser.Open(j); panelMotion.Show(false); }
            else { ShowStatus("Configure Jellyfin URL in the Streaming Hub first"); serverHubTab = 3; ServersHubPage(); }
        });
        Button("🌌 Nuvio Web", content, 620, row + 8, 290, () => { browser.Open("https://nuvio.app/"); panelMotion.Show(false); });
        Button("📺 YouTube", content, 10, row + 66, 290, () => { browser.Open("https://www.youtube.com/"); panelMotion.Show(false); });
        Button("🅿️ Plex Web", content, 315, row + 66, 290, () => { browser.Open("https://app.plex.tv/desktop"); panelMotion.Show(false); });
        Button("🎮 Twitch", content, 620, row + 66, 290, () => { browser.Open("https://www.twitch.tv/"); panelMotion.Show(false); });
        row += 136;
        var address=Field("Web address", "https://web.stremio.com/",null);
        ActionButton("Go to address",()=>browser.Navigate(address.text));
        ActionButton("New tab",()=>{browser.NewTab(address.text);ShowStatus("Tab opened. Refresh tabs to see it.");});
        foreach(var tab in browser.Tabs){int index=(int)tab["index"];Heading(((bool)tab["active"]?"● ":"")+(string)tab["title"]);ActionButton("Switch to tab "+(index+1),()=>{browser.SelectTab(index);panelMotion.Show(false);});ActionButton("Close tab "+(index+1),()=>{browser.CloseTab(index);ShowStatus("Tab closed. Refresh tabs to update the list.");});}
        ActionButton("Refresh tabs",BrowserControls);
        ActionButton("Back",browser.Back);ActionButton("Forward",browser.Forward); ActionButton("Reload",browser.Reload);
        Field("Type into the selected web input", "",browser.Text);
        ActionButton(room.IsSolo?"Play this HTTP video on native screen":"Share playing HTTP video with room",browser.Share);
        ActionButton("Forget web-screen sign-in",browser.ForgetSession);
        Heading("Walk and turn with the sticks. Hold right grip + right stick to scroll. Select a web input with the beam, then enter text here. Web video plays locally until shared.");
    }

    void AddonsPage() {
        pageTitle.text = "Catalogs"; Clear(); Heading("Manage catalogs and add-ons");
        ActionButton("Add official and community add-ons (stremio-addons.net)", () => Application.OpenURL("https://stremio-addons.net/"));
        var field = Field("Add-on manifest URL", "https://", null);
        ActionButton("Install add-on", () => {
            string url = field.text.Trim();
            if (!url.EndsWith("manifest.json")) { ShowStatus("URL must end with manifest.json"); return; }
            ShowStatus("Installing add-on…"); library.Install(url, () => { SaveAddons(); AddonsPage(); });
        });
        ActionButton("Stremio account and official sync", AccountPage);
        ActionButton("⚡ Servers & Streaming Hub (Nuvio / Jellyfin / Silo)", ServersHubPage);
        foreach (var addon in library.Addons) {
            var selected = addon; Heading((string)addon["name"] ?? (string)addon["id"]);
            ActionButton("Remove " + ((string)addon["name"] ?? "add-on"), () => { library.Addons.Remove(selected); SaveAddons(); Library(); });
            foreach (var token in addon["catalogs"] as JArray ?? new JArray()) {
                var catalog = (JObject)token;
                ActionButton(((string)catalog["name"] ?? (string)catalog["id"]) + " / " + (string)catalog["type"], () => {
                    int generation = pageGeneration; ShowStatus("Loading catalog…");
                    library.Catalog(selected, catalog, items => { if (generation == pageGeneration) Catalog(selected, (string)catalog["type"], items); });
                });
            }
        }
        Field("Video type", (string)settings[SettingsKeys.VideoType] ?? "movie", value => settings[SettingsKeys.VideoType] = value);
        var id = Field("Video ID (e.g. tt1254207 or series episode ID)", "", null);
        ActionButton("Find streams by ID", () => Streams((string)settings[SettingsKeys.VideoType] ?? "movie", id.text.Trim(), id.text.Trim()));
    }

    void Library() {
        Clear(); pageTitle.text = "Discover";
        Heading("Your cinema. Your collection.");

        // Provider Filter Pills
        Plate(content, 0, row, actionWidth, 54, new Color(0.08f, 0.09f, 0.15f));
        Button((providerFilter == "all" ? "● " : "") + "🌐 All Catalogs", content, 5, row + 4, 185, () => { providerFilter = "all"; Library(); });
        Button((providerFilter == "stremio" ? "● " : "") + "🎬 Stremio / Nuvio", content, 195, row + 4, 195, () => { providerFilter = "stremio"; Library(); });
        Button((providerFilter == "jellyfin" ? "● " : "") + "🍿 Jellyfin", content, 395, row + 4, 165, () => { providerFilter = "jellyfin"; Library(); });
        Button((providerFilter == "mediastorm" ? "● " : "") + "⚡ MediaStorm", content, 565, row + 4, 175, () => { providerFilter = "mediastorm"; Library(); });
        Button((providerFilter == "silo" ? "● " : "") + "🚀 Silo", content, 745, row + 4, 170, () => { providerFilter = "silo"; Library(); });
        row += 62;

        var reg = GetComponent<MediaProviderRegistry>();
        if (providerFilter == "jellyfin" && reg?.Jellyfin != null) {
            RenderProviderView(reg.Jellyfin);
            return;
        } else if (providerFilter == "mediastorm" && reg?.MediaStorm != null) {
            RenderProviderView(reg.MediaStorm);
            return;
        } else if (providerFilter == "silo" && reg?.Silo != null) {
            RenderProviderView(reg.Silo);
            return;
        }

        var searchField = Field("Search across all providers (Stremio, Jellyfin, Silo, etc.)", "", null);
        ActionButton("🔍 Search All Providers", async () => {
            string q = searchField.text?.Trim();
            if (string.IsNullOrEmpty(q)) return;
            ShowStatus("Searching all providers for \"" + q + "\"…");
            var results = reg != null ? await reg.SearchAllProvidersAsync(q) : new List<MediaItem>();
            library.SearchAllAddons(q, stremioMetas => {
                if (stremioMetas != null) {
                    foreach (var sm in stremioMetas) {
                        if (sm is JObject obj) {
                            results.Add(new MediaItem {
                                ProviderId = "stremio",
                                ItemId = (string)obj["id"],
                                Title = (string)obj["name"] ?? "Untitled",
                                Type = (string)obj["type"] ?? "movie",
                                PosterUrl = (string)obj["poster"]
                            });
                        }
                    }
                }
                Clear(); pageTitle.text = "Search: " + q;
                ActionButton("Back to Discover", Library);
                Heading("Search Results for \"" + q + "\" (" + results.Count + " found)");
                if (results.Count == 0) Heading("No results found on any connected provider.");
                else MultiProviderPosterGrid(results);
            });
        });

        ActionButton("🕒 Unified Continue Watching (Cross-Provider)", async () => {
            if (reg != null) {
                ShowStatus("Fetching in-progress media…");
                var items = await reg.GetUnifiedContinueWatchingAsync();
                Clear(); pageTitle.text = "Continue Watching";
                ActionButton("Back to Discover", Library);
                Heading("Continue Watching Across Providers (" + items.Count + " in progress)");
                if (items.Count == 0) Heading("No active watch progress found on connected servers.");
                else MultiProviderPosterGrid(items);
            }
        });

        ActionButton("⚡ Servers & Streaming Hub (Stremio / Nuvio / Jellyfin)", ServersHubPage);
        ActionButton("Manage catalogs", AddonsPage);
        if (library.Addons.Count == 0) {
            library.Install("https://v3-cinemeta.strem.io/manifest.json", () => { SaveAddons(); Library(); });
            return;
        }

        JObject featuredAddon = null, featuredCatalog = null; int tabs = 0;
        foreach (var addon in library.Addons) {
            foreach (var token in addon["catalogs"] as JArray ?? new JArray()) {
                var selected = addon; var catalog = (JObject)token;
                if (featuredCatalog == null || ((string)addon["id"] == "com.linvo.cinemeta" && (string)catalog["type"] == "movie" && (string)catalog["id"] == "top")) {
                    featuredAddon = addon; featuredCatalog = catalog;
                }
                if (tabs < 3) {
                    Button(((string)catalog["name"] ?? (string)catalog["id"]) + " / " + (string)catalog["type"], content, tabs * 310, row, 298, () => {
                        int generation = pageGeneration; ShowStatus("Loading posters…");
                        library.Catalog(selected, catalog, items => { if (generation == pageGeneration) Catalog(selected, (string)catalog["type"], items); });
                    });
                    tabs++;
                }
            }
        }
        row += 70;
        ActionButton("All catalogs and add-ons", AddonsPage);
        if (featuredCatalog != null) {
            Heading("Featured titles");
            int generation = pageGeneration;
            var addon = featuredAddon;
            string type = (string)featuredCatalog["type"];
            library.Catalog(addon, featuredCatalog, items => { if (generation == pageGeneration) PosterGrid(addon, type, items, ""); });
        }
    }

    async void RenderProviderView(IMediaProvider prov) {
        if (!prov.IsAuthenticated) {
            Heading(prov.DisplayName + " is not connected.");
            Label("Configure your server URL and credentials in the Streaming Hub to browse its library natively in VR.", content, 0, row, actionWidth, 40, 19); row += 48;
            ActionButton("⚡ Open " + prov.DisplayName + " Settings in Hub", () => {
                if (prov.ProviderId == "jellyfin") serverHubTab = 3;
                else if (prov.ProviderId == "mediastorm") serverHubTab = 4;
                else if (prov.ProviderId == "silo") serverHubTab = 4;
                ServersHubPage();
            });
            Resize();
            return;
        }

        var searchInput = Field("Search " + prov.DisplayName + " library...", "", null);
        ActionButton("🔍 Search " + prov.DisplayName, () => {
            string q = searchInput.text?.Trim();
            if (string.IsNullOrEmpty(q)) return;
            SearchProvider(prov, q);
        });

        ShowStatus("Loading " + prov.DisplayName + " catalogs…");
        ActionButton("Cancel catalog loading", Library);
        int loadingGeneration = pageGeneration;
        List<MediaCatalog> catalogs;
        try
        {
            catalogs = await prov.GetCatalogsAsync();
        }
        catch (Exception error)
        {
            if (loadingGeneration != pageGeneration) return;
            Debug.LogWarning("[CinemaApp] Provider catalog load failed: " + error.Message);
            Heading("Unable to load " + prov.DisplayName + " catalogs");
            Label("Check the provider connection and credentials, then try again.", content, 0, row, actionWidth, 40, 19);
            ActionButton("Retry " + prov.DisplayName + " catalogs", () => RenderProviderView(prov));
            ShowStatus("Catalog load failed. Check the provider connection and try again.");
            Resize();
            return;
        }
        if (loadingGeneration != pageGeneration) return;
        Heading(prov.DisplayName + " Catalogs");
        int generation = pageGeneration;
        int t = 0;
        foreach (var cat in catalogs) {
            var currentCat = cat;
            if (t < 3) {
                Button(currentCat.Name + " (" + currentCat.Type + ")", content, t * 310, row, 298, async () => {
                    ShowStatus("Loading " + currentCat.Name + "…");
                    try
                    {
                        var items = await prov.GetCatalogItemsAsync(currentCat.CatalogId);
                        if (generation == pageGeneration) ProviderPosterGrid(items, prov, currentCat.CatalogId, 0);
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning("[CinemaApp] Provider catalog items load failed: " + error.Message);
                        ShowStatus("Catalog load failed. Check the provider connection and try again.");
                    }
                });
                t++;
            }
        }
        if (t > 0) row += 70;

        if (catalogs != null && catalogs.Count > 0) {
            Heading("Featured from " + catalogs[0].Name);
            int featuredGeneration = pageGeneration;
            try
            {
                var items = await prov.GetCatalogItemsAsync(catalogs[0].CatalogId);
                if (featuredGeneration != pageGeneration) return;
                if (generation == pageGeneration) ProviderPosterGrid(items, prov, catalogs[0].CatalogId, 0);
            }
            catch (Exception error)
            {
                if (featuredGeneration != pageGeneration) return;
                Debug.LogWarning("[CinemaApp] Featured catalog load failed: " + error.Message);
                ActionButton("Retry featured titles", () => RenderProviderView(prov));
                ShowStatus("Featured titles could not be loaded. Try another catalog.");
            }
        } else {
            Heading("No media catalogs found on " + prov.DisplayName);
            Label("Make sure libraries are configured on your server and credentials are active.", content, 0, row, actionWidth, 40, 19); row += 48;
            ActionButton("⚡ Open " + prov.DisplayName + " Settings in Hub", () => {
                if (prov.ProviderId == "jellyfin") serverHubTab = 3;
                else if (prov.ProviderId == "mediastorm") serverHubTab = 4;
                else if (prov.ProviderId == "silo") serverHubTab = 4;
                ServersHubPage();
            });
            Resize();
        }
    }

    async void SearchProvider(IMediaProvider prov, string query) {
        int generation = pageGeneration;
        ShowStatus("Searching " + prov.DisplayName + " for \"" + query + "\"…");
        ActionButton("Cancel search", Library);
        List<MediaItem> res = null;
        try
        {
            if (prov is JellyfinProvider jf) res = await jf.SearchAsync(query);
            else if (prov is MediaStormProvider ms) res = await ms.SearchAsync(query);
            else if (prov is SiloProvider silo) res = await silo.SearchAsync(query);
        }
        catch (Exception error)
        {
            if (generation != pageGeneration) return;
            Debug.LogWarning("[CinemaApp] Provider search failed: " + error.Message);
            Clear();
            pageTitle.text = prov.DisplayName + ": " + query;
            ActionButton("Back to " + prov.DisplayName, () => RenderProviderView(prov));
            Heading("Unable to search " + prov.DisplayName);
            Label("Check the provider connection and credentials, then try again.", content, 0, row, actionWidth, 40, 19);
            ActionButton("Retry search", () => SearchProvider(prov, query));
            ShowStatus("Search failed. Check the provider connection and try again.");
            Resize();
            return;
        }

        if (generation != pageGeneration) return;
        Clear(); pageTitle.text = prov.DisplayName + ": " + query;
        ActionButton("Back to " + prov.DisplayName, () => RenderProviderView(prov));
        Heading("Results for \"" + query + "\" (" + (res?.Count ?? 0) + " found)");
        if (res == null || res.Count == 0) Heading("No titles matched your search.");
        else ProviderPosterGrid(res, prov);
    }

    void ProviderPosterGrid(List<MediaItem> items, IMediaProvider prov, string catalogId = null, int skip = 0) {
        int count = 0; float start = row;
        foreach (var item in items) {
            var currentItem = item;
            string title = item.Title ?? "Untitled";
            float x = (count % 4) * 232, y = start + (count / 4) * 370;
            var card = new GameObject("Title: " + title, typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(content, false); Place(card.GetComponent<RectTransform>(), x, y, 216, 350);
            card.GetComponent<Button>().transition = Selectable.Transition.None; card.GetComponent<Image>().color = new Color(0.11f, 0.09f, 0.18f); card.AddComponent<UiMotion>().Card = true;
            var poster = new GameObject("Poster", typeof(RectTransform), typeof(RawImage), typeof(PosterImage));
            poster.transform.SetParent(card.transform, false); Place(poster.GetComponent<RectTransform>(), 6, 6, 204, 288);
            poster.GetComponent<RawImage>().color = new Color(0.2f, 0.17f, 0.28f); poster.GetComponent<RawImage>().raycastTarget = false;
            poster.GetComponent<PosterImage>().Load(item.PosterUrl);
            Label(title, card.transform, 10, 300, 196, 44, 18);
            card.GetComponent<Button>().onClick.AddListener(() => ProviderItemDetails(currentItem, prov));
            if (++count >= 60) break;
        }
        row = start + Mathf.Ceil(count / 4f) * 370;
        if (!string.IsNullOrEmpty(catalogId) && count >= 30) {
            int nextSkip = skip + count;
            ActionButton("Load more from " + prov.DisplayName + "…", async () => {
                ShowStatus("Loading more titles…");
                try
                {
                    var moreItems = await prov.GetCatalogItemsAsync(catalogId, nextSkip);
                    if (moreItems != null && moreItems.Count > 0) {
                        ProviderPosterGrid(moreItems, prov, catalogId, nextSkip);
                    } else {
                        ShowStatus("Reached end of catalog.");
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[CinemaApp] More catalog items load failed: " + error.Message);
                    ShowStatus("More titles could not be loaded. Try again.");
                }
            });
        }
        Resize();
        ShowStatus(count == 0 ? "No media found" : "Select a poster for details and playback.");
    }

    void MultiProviderPosterGrid(List<MediaItem> items) {
        var reg = GetComponent<MediaProviderRegistry>();
        int count = 0; float start = row;
        foreach (var item in items) {
            var currentItem = item;
            var prov = reg != null ? reg.GetProvider(currentItem.ProviderId) : null;
            string provBadge = prov != null ? ("[" + prov.DisplayName + "] ") : "";
            string title = provBadge + (item.Title ?? "Untitled");
            float x = (count % 4) * 232, y = start + (count / 4) * 370;
            var card = new GameObject("Title: " + title, typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(content, false); Place(card.GetComponent<RectTransform>(), x, y, 216, 350);
            card.GetComponent<Button>().transition = Selectable.Transition.None; card.GetComponent<Image>().color = new Color(0.11f, 0.09f, 0.18f); card.AddComponent<UiMotion>().Card = true;
            var poster = new GameObject("Poster", typeof(RectTransform), typeof(RawImage), typeof(PosterImage));
            poster.transform.SetParent(card.transform, false); Place(poster.GetComponent<RectTransform>(), 6, 6, 204, 288);
            poster.GetComponent<RawImage>().color = new Color(0.2f, 0.17f, 0.28f); poster.GetComponent<RawImage>().raycastTarget = false;
            poster.GetComponent<PosterImage>().Load(item.PosterUrl);
            Label(title, card.transform, 10, 300, 196, 44, 18);
            card.GetComponent<Button>().onClick.AddListener(() => {
                if (currentItem.ProviderId == "stremio") {
                    Streams(currentItem.Type ?? "movie", currentItem.ItemId, currentItem.Title);
                } else {
                    var targetProv = prov ?? (reg != null ? reg.Jellyfin : null);
                    if (targetProv != null) ProviderItemDetails(currentItem, targetProv);
                }
            });
            if (++count >= 60) break;
        }
        row = start + Mathf.Ceil(count / 4f) * 370;
        Resize();
        ShowStatus(count == 0 ? "No media found" : "Select a poster for details and playback.");
    }

    void ProviderItemDetails(MediaItem item, IMediaProvider prov) {
        Clear(); pageTitle.text = item.Title;
        ActionButton("Back to Discover", Library);
        Heading(item.Title);
        string desc = "Native stream from " + prov.DisplayName;
        Label(desc, content, 0, row, 920, 150, 21); row += 165;
        ActionButton("Watch / Choose Stream Source", async () => {
            ShowStatus("Resolving streams from " + prov.DisplayName + "…");
            int generation = pageGeneration;
            ActionButton("Cancel stream lookup", Library);
            List<ResolvedStream> streams;
            try
            {
                streams = await prov.GetStreamsAsync(item.ItemId);
            }
            catch (Exception error)
            {
                if (generation != pageGeneration) return;
                Debug.LogWarning("[CinemaApp] Provider stream resolution failed: " + error.Message);
                ActionButton("Retry stream lookup", () => ProviderItemDetails(item, prov));
                ShowStatus("Stream lookup failed. Check the provider connection and try again.");
                return;
            }
            if (generation != pageGeneration) return;
            Clear(); ActionButton("Back to Discover", Library); Heading("Sources for " + item.Title);
            if (streams == null || streams.Count == 0) {
                Heading("No streams available for this item.");
            } else {
                foreach (var s in streams) {
                    var curStream = s;
                    ActionButton(curStream.Title ?? "Stream", () => {
                        watching = new JObject { ["_id"] = item.ItemId, ["name"] = item.Title, ["type"] = item.Type, ["state"] = new JObject() };
                        watchingUrl = curStream.StreamUrl;
                        playback.Load(curStream, item.Title, item.ResumePositionTicks > 0 ? item.ResumePositionTicks / 10000000.0 : 0);
                    });
                }
            }
            Resize();
        });
        Resize();
    }

    void Catalog(JObject addon, string type, JArray items) {
        Clear(); pageTitle.text="Discover / "+type; ActionButton("Back to catalogs",Library);
        var search=Field("Search these titles", "", value=>CatalogFiltered(addon,type,items,value));
        PosterGrid(addon,type,items,"");
    }
    void CatalogFiltered(JObject addon,string type,JArray items,string query) {
        Clear(); pageTitle.text="Discover / "+type; ActionButton("Back to catalogs",Library);
        Field("Search these titles",query,value=>CatalogFiltered(addon,type,items,value)); PosterGrid(addon,type,items,query);
    }
    void PosterGrid(JObject addon,string type,JArray items,string query) {
        int count=0; float start=row;
        foreach(var token in items) {
            var item=(JObject)token; string title=(string)item["name"]??"Untitled";
            if(title.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0) continue;
            float x=(count%4)*232, y=start+(count/4)*370;
            var card=new GameObject("Title: "+title,typeof(RectTransform),typeof(Image),typeof(Button)); card.transform.SetParent(content,false); Place(card.GetComponent<RectTransform>(),x,y,216,350);
            card.GetComponent<Button>().transition=Selectable.Transition.None; card.GetComponent<Image>().color=new Color(0.11f,0.09f,0.18f); card.AddComponent<UiMotion>().Card=true;
            var poster = new GameObject("Poster",typeof(RectTransform),typeof(RawImage),typeof(PosterImage)); poster.transform.SetParent(card.transform,false); Place(poster.GetComponent<RectTransform>(),6,6,204,288); poster.GetComponent<RawImage>().color=new Color(0.2f,0.17f,0.28f); poster.GetComponent<RawImage>().raycastTarget=false; poster.GetComponent<PosterImage>().Load((string)item["poster"]);
            Label(title,card.transform,10,300,196,44,18); card.GetComponent<Button>().onClick.AddListener(()=>Details(addon,type,item));
            if(++count>=60) break;
        }
        row=start+Mathf.Ceil(count/4f)*370; Resize(); ShowStatus(count==0?"No matching titles":"Select a poster for details and sources.");
    }
    void Details(JObject addon,string type,JObject item) {
        Clear(); pageTitle.text="Title details"; string title=(string)item["name"], id=(string)item["id"];
        ActionButton("Back to Discover",Library); Heading(title);
        string description=(string)item["description"]??"Choose a source to watch with your room.";
        Label(description,content,0,row,920,150,21); row+=165;
        ActionButton(type=="series"?"Choose episode":"Watch / choose source",()=> {
            if(type!="series") { Streams(type,id,title); return; }
            int generation=pageGeneration; library.Episodes(addon,type,id,episodes=> {
                if(generation!=pageGeneration) return; Clear(); ActionButton("Back to Discover",Library); Heading(title);
                foreach(var episode in episodes) { string episodeId=(string)episode["id"], name="S"+episode["season"]+" E"+episode["episode"]+" "+episode["title"]; ActionButton(name,()=>Streams(type,episodeId,title+" "+name)); }
            });
        });
    }
    ResolvedStream CreateStremioResolvedStream(JObject stream, string url, string itemId, string title) {
        var res = new ResolvedStream {
            ProviderId = "stremio",
            ItemId = itemId,
            Title = title,
            StreamUrl = url,
            RequiresServerProxy = false
        };
        if (stream["subtitles"] is JArray subs && subs.Count > 0) {
            res.SubtitleTracks = new List<SubtitleTrackInfo>();
            foreach (var sub in subs) {
                if (sub["url"] != null) {
                    string lang = (string)sub["lang"] ?? (string)sub["id"] ?? "Subtitle";
                    res.SubtitleTracks.Add(new SubtitleTrackInfo { Language = lang, Url = (string)sub["url"] });
                }
            }
            if (res.SubtitleTracks.Count > 0) res.SubtitleUrl = res.SubtitleTracks[0].Url;
        }
        if (stream["behaviorHints"] is JObject hints) {
            if (hints["videoCodec"] != null) res.SourceCodec = (string)hints["videoCodec"];
            if (hints["proxyHeaders"]?["request"] is JObject reqHeaders) {
                res.Headers = new Dictionary<string, string>();
                foreach (var prop in reqHeaders.Properties()) res.Headers[prop.Name] = (string)prop.Value;
            }
        }
        return res;
    }

    void Streams(string type, string id, string title, double start=0) {
        Clear(); ActionButton("Back to library", Library); Heading("Sources: " + title); ShowStatus("Finding streams…"); int generation = pageGeneration;
        library.Streams(type, id, streams => {
            if (generation != pageGeneration) return;
            foreach (var token in streams) { var stream = (JObject)token;
                if(room.IsSolo && stream["externalUrl"]!=null){string website=(string)stream["externalUrl"];ActionButton("Open website: "+((string)stream["name"]??"Source"),()=>{try{browser.Open(MediaLibrary.Http(website));panelMotion.Show(false);}catch(Exception error){ShowStatus(error.Message);}});continue;}
                ActionButton((string)stream["name"] ?? (string)stream["title"] ?? "Stream", () => { try { library.ResolveStream(stream, (string)settings[SettingsKeys.StreamingService], url => { watching=new JObject { ["_id"]=id,["name"]=title,["type"]=type,["state"]=new JObject() }; watchingUrl=url; var resStream = CreateStremioResolvedStream(stream, url, id, title); playback.Load(resStream, title, start); }); } catch (Exception error) { ShowStatus(error.Message); } }); }
            ShowStatus(streams.Count == 0 ? "No streams. Install a stream-provider add-on." : "Choose a source, then use Room → Play.");
        });
    }
    static bool HasEndpoint(JArray values,string endpoint) { foreach(var value in values) if((string)value==endpoint) return true; return false; }
    void SpeedControl(string title,float current,float min,float max,float step,Action<float> changed,string unit,Action refresh=null) {
        Label(title+": "+current.ToString("0.##")+" "+unit,content,0,row,540,52,23);
        Button("−",content,570,row,150,()=>{changed(Mathf.Max(min,current-step));if(refresh!=null)refresh();else Comfort();});
        Button("+",content,740,row,150,()=>{changed(Mathf.Min(max,current+step));if(refresh!=null)refresh();else Comfort();}); row+=70; Resize();
    }
    void HistoryPage() {
        Clear(); pageTitle.text="Watch history";
        Button(historyLocal?"Source: this headset":"Source: Stremio account",content,0,row,450,()=>{historyLocal=!historyLocal;HistoryPage();});
        Button(historyContinue?"Continue watching":"All watched titles",content,470,row,450,()=>{historyContinue=!historyContinue;HistoryPage();}); row+=70;
        ActionButton("🌐 Cross-Provider Continue Watching (Jellyfin / Silo / MediaStorm)", async () => {
            var reg = GetComponent<MediaProviderRegistry>();
            if (reg != null) {
                ShowStatus("Loading resume items across providers...");
                var resumeItems = await reg.GetUnifiedContinueWatchingAsync();
                Clear();
                pageTitle.text = "Unified Watch Progress";
                ActionButton("Back to Watch History", HistoryPage);
                Heading("Active In-Progress Media Across All Servers");
                if (resumeItems == null || resumeItems.Count == 0) {
                    Heading("No active in-progress media on any connected provider.");
                } else {
                    foreach (var rItem in resumeItems) {
                        var cur = rItem;
                        var p = reg.GetProvider(cur.ProviderId);
                        string pName = p != null ? p.DisplayName : cur.ProviderId;
                        double seconds = cur.ResumePositionTicks > 0 ? (cur.ResumePositionTicks / 10000000.0) : 0;
                        string timeStr = seconds > 0 ? ("resume at " + TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss")) : "in progress";
                        ActionButton("[" + pName + "] " + cur.Title + "  ·  " + timeStr, () => {
                            if (p != null) ProviderItemDetails(cur, p);
                        });
                    }
                }
                Resize();
            }
        });
        if(!historyLocal) { if(!account.SignedIn) { ActionButton("Sign in to view Stremio history",AccountPage); return; } ActionButton("Refresh Stremio history",account.RefreshHistory); }
        var entries=historyLocal?(settings[SettingsKeys.RecentHistory] as JArray??new JArray()):account.History;
        int count=0;
        foreach(var token in entries) {
            var item=(JObject)token; var state=item["state"] as JObject??new JObject(); double offset=(double?)state["timeOffset"]??0, duration=(double?)state["duration"]??0;
            if(historyContinue && (offset<=0 || duration>0 && offset>=duration*0.95)) continue;
            string name=(string)item["name"]??"Untitled", type=(string)item["type"]??"movie", id=(string)state["video_id"]??(string)item["_id"];
            double resume=duration>0 && offset>=duration*0.95?0:offset/1000;
            ActionButton(name+"  ·  "+(resume>0?"resume at "+TimeSpan.FromSeconds(resume).ToString(@"hh\:mm\:ss"):"watched"),()=>Streams(type,id,name,resume)); count++;
        }
        if(count==0) Heading("No watched titles yet. Play a title to track history.");
    }
    void RecordHistory() {
        if(watching==null || playback.SourceUrl!=watchingUrl || !playback.Video.isPlaying || Time.unscaledTime<nextHistory) return;
        nextHistory=Time.unscaledTime+15; var state=(JObject)watching["state"]!; state["lastWatched"]=DateTime.UtcNow; state["timeOffset"]=Math.Max(0,playback.Position*1000); if(playback.Duration>0) state["duration"]=playback.Duration*1000;
        var recent=settings[SettingsKeys.RecentHistory] as JArray??new JArray(); for(int i=recent.Count-1;i>=0;i--) if((string)recent[i]["_id"]==(string)watching["_id"]) recent.RemoveAt(i); recent.Insert(0,watching.DeepClone()); while(recent.Count>100) recent.RemoveAt(recent.Count-1); settings[SettingsKeys.RecentHistory]=recent; Save();
    }
    void ChangeWorld(string id) {
        if(room.State==null) world.Load(id);
        else if(room.IsHost) room.Send(new JObject { ["type"]="world", ["world"]=id });
        else ShowStatus("Only the host can change the world");
    }

    void Comfort() {
        Clear(); pageTitle.text="Settings";
        ActionButton("Media Quest VR GitHub & Releases", () => Application.OpenURL("https://github.com/oOflyeyesOo/Media-VR"));
        ActionButton("Upstream Movie Night Source (AGPLv3)", () => Application.OpenURL("https://github.com/JeremyLakeyJr/stremio-vr"));
        Heading("Movement & turning");
        SpeedControl("Walk speed",motion.MoveSpeed,0.5f,3,0.25f,value=>{motion.MoveSpeed=value;settings[SettingsKeys.MoveSpeed]=value;Save();},"m/s");
        SpeedControl("Smooth turn speed",motion.TurnSpeed,15,180,15,value=>{motion.TurnSpeed=value;settings[SettingsKeys.TurnSpeed]=value;Save();},"°/s");
        ActionButton("Turn style: " + (motion.SnapTurn ? "Snap Turn" : "Smooth Turn"), () => { motion.SnapTurn = !motion.SnapTurn; settings[SettingsKeys.SnapTurn] = motion.SnapTurn; Save(); Comfort(); });
        if (motion.SnapTurn) {
            ActionButton("Snap turn angle: " + motion.SnapAngle + "°", () => {
                motion.SnapAngle = motion.SnapAngle >= 90 ? 15 : motion.SnapAngle + 15;
                settings[SettingsKeys.SnapAngle] = motion.SnapAngle; Save(); Comfort();
            });
        }
        ActionButton("Locomotion style: " + (motion.TeleportMode ? "Instant Teleport" : "Smooth Joystick Walk"), () => { motion.TeleportMode = !motion.TeleportMode; settings[SettingsKeys.TeleportMode] = motion.TeleportMode; Save(); Comfort(); });
        ActionButton("Movement direction: " + (motion.HandOriented ? "Left Hand / Controller-oriented" : "Head / Gaze-oriented"), () => { motion.HandOriented = !motion.HandOriented; settings[SettingsKeys.HandOriented] = motion.HandOriented; Save(); Comfort(); });
        ActionButton("Left stick sprint: " + (motion.SprintEnabled ? "Enabled" : "Disabled"), () => { motion.SprintEnabled = !motion.SprintEnabled; settings[SettingsKeys.SprintEnabled] = motion.SprintEnabled; Save(); Comfort(); });

        Heading("VR Menu Controls (Single Selected Option)");
        int curMenuOpt = Mathf.Clamp((int?)settings[SettingsKeys.MenuButtonOption] ?? 0, 0, MenuButtonOptions.Length - 1);
        ActionButton("Menu toggle button: " + MenuButtonOptions[curMenuOpt], () => {
            int next = (curMenuOpt + 1) % MenuButtonOptions.Length;
            settings[SettingsKeys.MenuButtonOption] = next;
            if (next == 6) motion.SprintEnabled = false; // Disable sprint conflict if stick click is menu
            Save();
            Comfort();
        });

        Heading("Cinema Display & Environments (Fluid / Bigscreen / DeoVR)");
        ActionButton("👓 Open Cinema & 3D Controls (Void, Passthrough, 3D, Bed Mode)", CinemaControlsPage);

        Heading("Open Concept Home");
        ActionButton("Arrange room",LayoutPage);
        ActionButton("Mirror: "+(world.MirrorVisible?"On":"Off"),()=>{world.MirrorVisible=!world.MirrorVisible;Comfort();});
        if(Resources.Load<GameObject>("Worlds/Home")) ActionButton("Open Concept Home (host)",()=>ChangeWorld("home"));
        ActionButton("Ambient video lighting: "+(ambient.Enabled?"On":"Off"),()=>{ambient.Enabled=!ambient.Enabled;settings[SettingsKeys.Ambient]=ambient.Enabled;Save();Comfort();});
        ActionButton("Ambient strength: "+Mathf.RoundToInt(ambient.Strength*100)+"%",()=>{ambient.Strength=ambient.Strength>=2?0.25f:Mathf.Min(2,ambient.Strength+0.25f);settings[SettingsKeys.AmbientStrength]=ambient.Strength;Save();Comfort();});
        Heading("Hide the menu: left stick walks, right stick turns.");
        ActionButton("Stand up / return to aisle",motion.Stand);
        ActionButton("Calibrate reclined view (look where you want the screen)",()=> { motion.CalibrateReclined(); Recenter(); Comfort(); });
        ActionButton("View angle +5°",()=> { motion.AdjustView(5); Recenter(); }); ActionButton("View angle −5°",()=> { motion.AdjustView(-5); Recenter(); });
        ActionButton("Reset view angle",()=> { motion.ResetView(); Recenter(); });
        ActionButton("Pointer angle: "+motion.RayPitch+"°  /  change by 15°",()=> { motion.RayPitch=motion.RayPitch>=75?-30:motion.RayPitch+15; settings[SettingsKeys.RayPitch]=motion.RayPitch; Save(); Comfort(); });
        Heading("Chairs — or point at a chair and press the trigger");
        for(int i=0;i<world.Data.Seats.Length;i++) { int seat=i; ActionButton("Sit in chair "+(i+1),()=> { motion.RequestSeat(seat); panelMotion.Show(false); }); }
    }

    void Customize() {
        Clear(); pageTitle.text = "Avatar studio"; Heading("Your avatar — saved on this device");
        actionWidth = 500;
        var previewObject = new GameObject("R6 preview", typeof(RectTransform), typeof(RawImage), typeof(AvatarPreview));
        previewObject.transform.SetParent(content, false); Place(previewObject.GetComponent<RectTransform>(), 550, 150, 370, 490);
        previewObject.GetComponent<RawImage>().raycastTarget = false;
        var preview = previewObject.GetComponent<AvatarPreview>(); preview.Initialize(profile);
        Button("Turn left", content, 550, 655, 175, () => preview.Turn(-30)); Button("Turn right", content, 740, 655, 180, () => preview.Turn(30));
        preview.StatusLabel=Label("Loading preview…", content, 550, 720, 370, 130, 19);
        ActionButton("Browse Open Source Avatars",()=>AvatarLibrary("",0));
        ActionButton("Room avatar credits",RoomAvatarCredits);
        if(OpenAvatarCatalog.Find((string)profile["avatarId"])!=null)ActionButton("Use customizable R6",()=>{profile.Remove("avatarId");UpdateProfile();Customize();});
        Field("Name", (string)profile["name"], value => { profile["name"] = value; UpdateProfile(); });
        if(string.IsNullOrEmpty((string)profile["avatarId"])) {
            Choice("body", new[] { "regular", "tall", "round" });
            Choice("head", new[] { "round", "block" });
            Choice("hair", new[] { "none", "short", "bun", "curly" });
            Choice("outfit", new[] { "casual", "suit", "dress", "hoodie" });
            Choice("accessory", new[] { "none", "glasses", "headphones", "catEars" });
            ActionButton("Randomize avatar", () => {
                string[] skins = { "#ffd1b3", "#e8b590", "#b98059", "#7c4b28", "#4a2c11" }, colors = { "#7958ff", "#ff588b", "#4cd3a5", "#f6a623", "#4a90e2" };
                profile["skin"] = skins[UnityEngine.Random.Range(0, skins.Length)]; profile["color"] = colors[UnityEngine.Random.Range(0, colors.Length)];
                profile["head"] = UnityEngine.Random.value > 0.5f ? "round" : "block"; profile["hair"] = new[] { "none", "short", "bun", "curly" }[UnityEngine.Random.Range(0, 4)];
                profile["outfit"] = new[] { "casual", "suit", "dress", "hoodie" }[UnityEngine.Random.Range(0, 4)];
                profile["accessory"] = new[] { "none", "glasses", "headphones", "catEars" }[UnityEngine.Random.Range(0, 4)];
                UpdateProfile(); Customize();
            });
        }
        ActionButton("Back to my room", Home);
    }

    void AvatarLibrary(string query, int offset) {
        Clear(); pageTitle.text = "Open Source Avatars";
        ActionButton("Back to Avatar studio", Customize);
        Field("Search name, collection or creator", query, value => AvatarLibrary(value, 0));
        var matches = OpenAvatarCatalog.Entries.Where(a => ((string)a["name"] + " " + a["collection"] + " " + a["creator"]).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        Heading(matches.Count + " avatars · downloads require internet");
        ActionButton("🗑 Clear downloaded avatar disk cache", () => {
            OpenAvatarCatalog.ClearCache();
            ShowStatus("Avatar disk cache cleared.");
            AvatarLibrary(query, offset);
        });
        foreach (var entry in matches.Skip(offset).Take(10)) { var selected = entry; ActionButton(OpenAvatarCatalog.Credit(entry), () => AvatarDetails(selected, query, offset)); }
        if (offset > 0) ActionButton("Previous page", () => AvatarLibrary(query, Math.Max(0, offset - 10)));
        if (offset + 10 < matches.Count) ActionButton("Next page", () => AvatarLibrary(query, offset + 10));
        ShowStatus("Downloaded avatars work offline. Large or incompatible models fall back to R6.");
    }
    void AvatarDetails(JObject entry, string query, int offset) {
        Clear(); pageTitle.text = (string)entry["name"];
        ActionButton("Back to avatar library", () => AvatarLibrary(query, offset));
        Heading(OpenAvatarCatalog.Credit(entry));
        ActionButton("Use this avatar", () => { profile["avatarId"] = (string)entry["id"]; UpdateProfile(); Customize(); });
    }
    void RoomAvatarCredits() {
        Clear(); pageTitle.text = "Avatar credits"; Heading("Room & avatar attribution");
        Label("Movie Night VR includes customizable avatars and curated open source models.\nR6 avatar rig created for Movie Night under AGPLv3.\nThird-party avatars: see individual attribution tags in the avatar library.", content, 0, row, 920, 220, 21); row += 235;
        ActionButton("Back to studio", Customize);
    }
    void Choice(string key, string[] values) { ActionButton(key + ": " + profile[key] + "  →", () => { int index = Array.IndexOf(values, (string)profile[key]); profile[key] = values[(index + 1) % values.Length]; UpdateProfile(); Customize(); }); }
    void UpdateProfile() { localBody.Apply(profile); Save(); if (room.Connected && room.State != null) room.Send(new JObject { ["type"] = "profile", ["profile"] = profile }); }
    void SaveAddons() { var urls = new JArray(); foreach (var addon in library.Addons) urls.Add(MediaLibrary.ResourceUrl((string)addon["endpoint"], "manifest.json")); settings["addons"] = urls; Save(); }
    void Save() { try { File.WriteAllText(SettingsPath, settings.ToString()); } catch { } }
    void ShowStatus(string message) { status.text = message; }
    void ActionButton(string text, Action click) { Button(text, content, 0, row, actionWidth, click); row += 62; Resize(); }

    InputField Field(string placeholder, string current, Action<string> changed) {
        var go = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(InputField)); go.transform.SetParent(content, false); Place(go.GetComponent<RectTransform>(), 0, row, 850, 52);
        go.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.16f);
        var text = new GameObject("Text", typeof(RectTransform), typeof(Text)); text.transform.SetParent(go.transform, false); Place(text.GetComponent<RectTransform>(), 12, 0, 826, 52);
        var label = text.GetComponent<Text>(); label.font = font; label.fontSize = 20; label.color = Color.white; label.alignment = TextAnchor.MiddleLeft;
        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text)); placeholderGo.transform.SetParent(go.transform, false); Place(placeholderGo.GetComponent<RectTransform>(), 12, 0, 826, 52);
        var ph = placeholderGo.GetComponent<Text>(); ph.font = font; ph.fontSize = 20; ph.color = new Color(0.5f, 0.45f, 0.65f); ph.text = placeholder; ph.alignment = TextAnchor.MiddleLeft;
        var field = go.GetComponent<InputField>(); field.textComponent = label; field.placeholder = ph; field.text = current ?? "";
        field.onEndEdit.AddListener(result => changed?.Invoke(result));
        Button("⌨️", content, 860, row, 60, () => VRKeyboard.Instance?.Attach(field));
        row += 65; Resize(); return field;
    }

    InputField PasswordField(string placeholder, string current, Action<string> changed) {
        var field = Field(placeholder, current, changed);
        field.contentType = InputField.ContentType.Password;
        field.ForceLabelUpdate();
        return field;
    }

    void Resize() { content.sizeDelta = new Vector2(930, Mathf.Max(690, row)); }
    Text Label(string text, Transform parent, float x, float y, float width, float height, int size) {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, width, height);
        var label = go.GetComponent<Text>(); label.font = font; label.fontSize = size; label.text = text; label.color = Color.white; label.supportRichText = false; label.raycastTarget = false; label.alignment = TextAnchor.MiddleLeft; return label;
    }
    void Button(string text, Transform parent, float x, float y, float width, Action click) {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, width, 52);
        go.GetComponent<Image>().color = new Color(0.13f,0.105f,0.21f); go.GetComponent<Button>().transition=Selectable.Transition.None; go.GetComponent<Button>().onClick.AddListener(() => click()); go.AddComponent<UiMotion>(); Label(text, go.transform, 12, 3, width - 24, 46, 20);
    }
    static void Plate(Transform parent, float x, float y, float width, float height, Color color) {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, width, height);
        go.GetComponent<Image>().color = color; go.GetComponent<Image>().raycastTarget = false;
    }
    static void Place(RectTransform rect, float x, float y, float w, float h) { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }

    static InputDevice GetValidDevice(XRNode node, InputDeviceCharacteristics fallback) {
        var dev = InputDevices.GetDeviceAtXRNode(node);
        if (dev.isValid) return dev;
        var list = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(fallback, list);
        return list.Count > 0 ? list[0] : dev;
    }

    bool CheckMenuButton(InputDevice leftDev, InputDevice rightDev) {
        int opt = Mathf.Clamp((int?)settings[SettingsKeys.MenuButtonOption] ?? 0, 0, MenuButtonOptions.Length - 1);
        switch (opt) {
            case 0:
                leftDev.TryGetFeatureValue(CommonUsages.menuButton, out bool m);
                return m;
            case 1:
                leftDev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool y);
                return y;
            case 2:
                leftDev.TryGetFeatureValue(CommonUsages.primaryButton, out bool x);
                return x;
            case 3:
                rightDev.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
                return a;
            case 4:
                rightDev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
                return b;
            case 5:
                rightDev.TryGetFeatureValue(CommonUsages.primaryButton, out bool aHold);
                if (aHold) {
                    rightALongPressTimer += Time.unscaledDeltaTime;
                    if (rightALongPressTimer >= 0.4f && !rightALongPressFired) {
                        rightALongPressFired = true;
                        return true;
                    }
                } else {
                    rightALongPressTimer = 0;
                    rightALongPressFired = false;
                }
                return false;
            case 6:
                leftDev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool click);
                return click;
            default:
                leftDev.TryGetFeatureValue(CommonUsages.menuButton, out bool defM);
                return defM;
        }
    }

    void Update() {
        RecordHistory();
        var controller = GetValidDevice(XRNode.RightHand, InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller);
        var leftController = GetValidDevice(XRNode.LeftHand, InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller);
        
        bool menuPressed = CheckMenuButton(leftController, controller);
        if ((menuPressed && !menuWasDown) || Input.GetKeyDown(KeyCode.Tab)) {
            if (placement.Active) placement.Cancel();
            else {
                panelMotion.Show(!panelMotion.Visible);
                if (panelMotion.Visible) {
                    if (browser.Active && room.IsHost) BrowserControls();
                    Recenter();
                }
            }
        }
        menuWasDown = menuPressed;

        pointer.enabled = true;
        Vector3 origin = controller.isValid ? right.position : head.position; Vector3 direction = controller.isValid ? RigMotion.RayDirection(right.rotation, motion.RayPitch) : head.forward;
        var ray = new Ray(origin, direction);
        if(placement.Active) {
            controller.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 placementRight);leftController.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 placementLeft);
            controller.TryGetFeatureValue(CommonUsages.triggerButton,out bool placementTrigger);placementTrigger|=Input.GetMouseButton(0);
            pointer.SetPosition(0,origin);pointer.SetPosition(1,ray.GetPoint(3));pointer.material.color=Color.cyan;
            placement.Tick(ray,placementRight,placementLeft,placementTrigger&&!triggerWasDown);triggerWasDown=placementTrigger;return;
        }
        leftController.TryGetFeatureValue(CommonUsages.gripButton,out bool leftGrip);controller.TryGetFeatureValue(CommonUsages.gripButton,out bool rightGrip);
        if(games.Grip(left,right,leftGrip,rightGrip,!panelMotion.Visible)){pointer.enabled=false;controller.TryGetFeatureValue(CommonUsages.triggerButton,out triggerWasDown);return;}
        var plane = new Plane(panel.forward, panel.position);
        float distance=0; bool intersects = panelMotion.Visible && plane.Raycast(ray, out distance) && distance < 10;
        var point = intersects ? ray.GetPoint(distance) : ray.GetPoint(3);
        pointer.SetPosition(0, origin); pointer.SetPosition(1, point);
        var data = new PointerEventData(EventSystem.current) { position = cameraView.WorldToScreenPoint(point) };
        if (!XRSettings.isDeviceActive) data.position = Input.mousePosition;
        hits.Clear(); if (panelMotion.Visible && (intersects || !XRSettings.isDeviceActive)) canvas.GetComponent<GraphicRaycaster>().Raycast(data, hits);
        var target = hits.Count > 0 ? hits[0].gameObject : null;
        controller.TryGetFeatureValue(CommonUsages.triggerButton,out bool webTrigger); controller.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 webStick);
        controller.TryGetFeatureValue(CommonUsages.gripButton,out bool webScrollGrip);
        bool webHit=browser.Point(ray,target==null && (webTrigger||Input.GetMouseButton(0)),target==null&&webScrollGrip?webStick.y:0,out var webPoint);
        if(webHit && target==null) pointer.SetPosition(1,webPoint);
        TabletopTarget tableTarget=null; SeatTarget chair=null;
        if(target==null && !webHit && Physics.Raycast(ray,out var worldHit,12,~((1<<30)|(1<<8)),QueryTriggerInteraction.Collide)) { chair=worldHit.collider.GetComponent<SeatTarget>(); tableTarget=worldHit.collider.GetComponent<TabletopTarget>(); pointer.SetPosition(1,worldHit.point); }
        bool uiInteractive=target!=null && (target.GetComponentInParent<Selectable>()!=null || target.GetComponentInParent<ScrollRect>()!=null);
        pointer.enabled=uiInteractive||(target==null&&(webHit||chair!=null||tableTarget!=null));
        if(Input.GetMouseButtonUp(0)) triggerWasDown=false;
        controller.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
        if(target!=null) {
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerMoveHandler);
            if (target != hovered) {
                if (hovered != null) ExecuteEvents.Execute(hovered, data, ExecuteEvents.pointerExitHandler);
                ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler); hovered = target;
            }
            if ((trigger && !triggerWasDown) || Input.GetMouseButtonDown(0)) { ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler); }
            if ((!trigger && triggerWasDown) || Input.GetMouseButtonUp(0)) ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
            controller.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
            if (Mathf.Abs(stick.y) > 0.1f) {
                var scrollRect = target.GetComponentInParent<ScrollRect>();
                if (scrollRect != null) {
                    scrollTarget = Mathf.Clamp01((scrollTarget == 0 ? scrollRect.verticalNormalizedPosition : scrollTarget) + stick.y * Time.unscaledDeltaTime * 2.5f);
                    scrollRect.verticalNormalizedPosition = Mathf.Lerp(scrollRect.verticalNormalizedPosition, scrollTarget, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
                }
            } else scrollTarget = 0;
        } else if (hovered != null) { ExecuteEvents.Execute(hovered, data, ExecuteEvents.pointerExitHandler); hovered = null; }
        if (target == null && !webHit && chair != null && ((trigger && !triggerWasDown) || Input.GetMouseButtonDown(0))) { motion.RequestSeat(chair.Index); panelMotion.Show(false); }
        triggerWasDown = trigger;
    }

    void LayoutPage() {
        Clear(); pageTitle.text = "Arrange room"; Heading(room.IsSolo?"Solo apartment layout — saved on this headset":"Room layout — host arranges for all connected players");
        if (!room.IsHost) { ActionButton("Only the host can arrange the cinema", Home); return; }
        layoutPageRevision = (int?)room.State?["layout"]?["revision"] ?? 0;
        ActionButton("Reset room to default layout", () => { room.Send(new JObject { ["type"] = "layout", ["action"] = "reset", ["revision"] = layoutPageRevision }); LayoutPage(); });
        var screens = new List<JObject>(); var tables = new List<JObject>();
        foreach (var item in room.State?["layout"]?["items"] as JArray ?? new JArray()) { if ((string)item["kind"] == "screen") screens.Add((JObject)item); else tables.Add((JObject)item); }
        Heading("Screens (up to 4)");
        if (screens.Count < 4) ActionButton("Place another screen", () => { placement.Begin("screen"); panelMotion.Show(false); });
        foreach (var screen in screens) { string id = (string)screen["id"]; ActionButton("Move screen " + id, () => { placement.Begin("screen", id); panelMotion.Show(false); }); ActionButton("Adjust screen " + id, () => MonitorPage(id)); ActionButton("Remove screen " + id, () => { placement.Remove(id); LayoutPage(); }); }
        Heading("Game tables (up to 3)");
        if (tables.Count < 3) { ActionButton("Place chess table", () => { placement.Begin("chess"); panelMotion.Show(false); }); ActionButton("Place tic-tac-toe table", () => { placement.Begin("tictactoe"); panelMotion.Show(false); }); }
        foreach (var table in tables) { string id = (string)table["id"]; ActionButton("Move " + (string)table["kind"] + " table " + id, () => { placement.Begin((string)table["kind"], id); panelMotion.Show(false); }); ActionButton("Remove table " + id, () => { placement.Remove(id); LayoutPage(); }); }
        ActionButton("Back to my room", Home);
    }

    void MonitorPage(string id) {
        Clear(); pageTitle.text = "Adjust monitor";
        float width = 2.1f, curve = 0;
        foreach (var item in room.State?["layout"]?["items"] as JArray ?? new JArray()) if ((string)item["id"] == id) { width = (float?)item["width"] ?? 2.1f; curve = (float?)item["curve"] ?? 0; }
        Heading("Size and curve for " + id);
        SpeedControl("Width", width, 1.2f, 4.5f, 0.2f, value => placement.Configure(id, value, curve), "m", () => MonitorPage(id));
        SpeedControl("Curve", curve, 0, 90, 10, value => placement.Configure(id, width, value), "°", () => MonitorPage(id));
        ActionButton("Back to layout", LayoutPage);
    }

    void AccountPage() {
        Clear(); pageTitle.text = "Stremio account"; Heading("Pair your Stremio or Nuvio account");
        if (account.SignedIn) {
            Heading("Signed in as " + (account.DisplayName ?? "account"));
            ActionButton("Sync account add-ons", account.SyncAddons);
            ActionButton("Sign out", account.SignOut);
        } else if (!string.IsNullOrEmpty(account.LinkCode)) {
            Heading("Sign-in code: " + account.LinkCode);
            Heading("Open this link on your phone, PC, or headset browser:");
            Label(account.LinkUrl, content, 0, row, 920, 50, 22); row += 58;
            ActionButton("Open sign-in link in Quest browser", account.OpenLink);
            ActionButton("Cancel sign-in", account.CancelLink);
            Heading("Waiting for approval (refreshes automatically)…");
        } else {
            ActionButton("Pair with device code (recommended)", account.BeginLink);
            Heading("Or sign in directly with credentials");
            string accEmail = (string)settings[SettingsKeys.AccountEmail] ?? "";
            var emailInput = Field("Account Email", accEmail, v => { settings[SettingsKeys.AccountEmail] = v; Save(); });
            var passInput = PasswordField("Account Password", "", null);
            ActionButton("Sign In with Email & Password", () => account.LoginWithCredentials(emailInput.text, passInput.text));
        }
        ActionButton("🎛 Cinema Controls (3D SBS / Void / Bed Mode)", CinemaControlsPage);
        ActionButton("🎬 Open Stremio Web Player", () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
        ActionButton("Back to media library", Library);
    }

    void ImportAccountAddons(JArray addons) {
        var endpoints = new JArray();
        foreach (var addon in addons) {
            if (addon is JObject obj && obj["transportUrl"] != null) {
                string url = (string)obj["transportUrl"]; endpoints.Add(url);
                library.Install(url, () => { SaveAddons(); if (pageTitle != null && pageTitle.text == "Discover") Library(); });
            }
        }
        settings["accountAddons"] = endpoints;
        settings["accountCache"] = addons.DeepClone();
        Save();
    }

    void ApplyBrowserRes(string res) {
        settings[SettingsKeys.BrowserRes] = res; Save();
        if (browser == null) return;
        if (res == "4k") browser.SetResolution(3840, 2160);
        else if (res == "1440p") browser.SetResolution(2560, 1440);
        else if (res == "720p") browser.SetResolution(1280, 720);
        else browser.SetResolution(1920, 1080);
    }

    void ServersHubPage() {
        Clear(); pageTitle.text = "Servers & Streaming";
        Heading("Media Servers, Debrid & Streaming Services");

        var reg = GetComponent<MediaProviderRegistry>();
        bool sConn = account.SignedIn;
        bool jConn = reg?.Jellyfin != null && reg.Jellyfin.IsAuthenticated;
        bool msConn = reg?.MediaStorm != null && reg.MediaStorm.IsAuthenticated;
        bool siloConn = reg?.Silo != null && reg.Silo.IsAuthenticated;

        // Top Navigation Bar with active tab indicators
        Plate(content, 0, row, actionWidth, 56, new Color(0.08f, 0.09f, 0.15f));
        Button((serverHubTab == 0 ? "● " : "") + "🚀 Launch", content, 5, row + 4, 145, () => { serverHubTab = 0; settings[SettingsKeys.LastServerTab] = 0; Save(); ServersHubPage(); });
        Button((sConn ? "● " : "○ ") + "🌌 Nuvio", content, 155, row + 4, 145, () => { serverHubTab = 1; settings[SettingsKeys.LastServerTab] = 1; Save(); ServersHubPage(); });
        Button((sConn ? "● " : "○ ") + "🎬 Stremio", content, 305, row + 4, 145, () => { serverHubTab = 2; settings[SettingsKeys.LastServerTab] = 2; Save(); ServersHubPage(); });
        Button((jConn ? "● " : "○ ") + "🍿 Jellyfin", content, 455, row + 4, 145, () => { serverHubTab = 3; settings[SettingsKeys.LastServerTab] = 3; Save(); ServersHubPage(); });
        Button(((msConn || siloConn) ? "● " : "○ ") + "⚡ Debrid/Silo", content, 605, row + 4, 150, () => { serverHubTab = 4; settings[SettingsKeys.LastServerTab] = 4; Save(); ServersHubPage(); });
        Button((serverHubTab == 5 ? "● " : "") + "🖥 4K/Display", content, 760, row + 4, 150, () => { serverHubTab = 5; settings[SettingsKeys.LastServerTab] = 5; Save(); ServersHubPage(); });
        row += 66;

        if (serverHubTab == 0) {
            Heading("🚀 One-Click Web Streamers");
            Plate(content, 0, row, actionWidth, 126, new Color(0.12f, 0.15f, 0.22f));
            Button("🌌 Nuvio Web", content, 10, row + 8, 290, () => { browser.Open("https://nuvio.app/"); panelMotion.Show(false); });
            Button("🎬 Stremio Web", content, 315, row + 8, 290, () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
            Button("🍿 Jellyfin Web", content, 620, row + 8, 290, () => {
                string j = (string)settings[SettingsKeys.JellyfinUrl];
                if (!string.IsNullOrEmpty(j)) { browser.Open(j); panelMotion.Show(false); }
                else { ShowStatus("Configure your Jellyfin URL in the Jellyfin tab first"); serverHubTab = 3; ServersHubPage(); }
            });
            Button("📺 YouTube", content, 10, row + 66, 290, () => { browser.Open("https://www.youtube.com/"); panelMotion.Show(false); });
            Button("🅿️ Plex Web", content, 315, row + 66, 290, () => { browser.Open("https://app.plex.tv/desktop"); panelMotion.Show(false); });
            Button("🎮 Twitch", content, 620, row + 66, 290, () => { browser.Open("https://www.twitch.tv/"); panelMotion.Show(false); });
            row += 136;

            Heading("Active Server Status");
            string sStatus = account.SignedIn ? "🟢 Stremio: Signed in (" + (account.DisplayName ?? "Account") + ")" : "⚪ Stremio: Not paired";
            string nStatus = !string.IsNullOrEmpty((string)settings[SettingsKeys.MellonUrl]) ? "🟢 Nuvio Sync: " + (string)settings[SettingsKeys.MellonUrl] : "⚪ Nuvio: Standby";
            string jStatus = jConn ? "🟢 Jellyfin: Connected" : "⚪ Jellyfin: Disconnected";
            string msStatus = msConn ? "🟢 MediaStorm: Active" : "⚪ MediaStorm: Standby";
            string siloStatus = siloConn ? "🟢 Silo: Active" : "⚪ Silo: Standby";
            string curRes = ((string)settings[SettingsKeys.BrowserRes] ?? "1080p").ToUpperInvariant();

            Label(sStatus + "\n" + nStatus + "\n" + jStatus + "\n" + msStatus + "\n" + siloStatus + "\n🖥 Display Quality: " + curRes, content, 0, row, actionWidth, 145, 19);
            row += 155;

            ActionButton("🎬 Manage Stremio Pairing & Sync", () => { serverHubTab = 2; ServersHubPage(); });
            ActionButton("🌌 Configure Nuvio Addon Sync", () => { serverHubTab = 1; ServersHubPage(); });
            ActionButton("🍿 Configure Jellyfin Media Server", () => { serverHubTab = 3; ServersHubPage(); });
            ActionButton("🖥 Change Web Screen Resolution (1080p / 4K / 720p)", () => { serverHubTab = 5; ServersHubPage(); });
        }
        else if (serverHubTab == 1) {
            Heading("🌌 Nuvio Streaming & Account Sync");
            Label("Nuvio is an open-source Stremio fork sharing the same account, addon, and streaming ecosystem.", content, 0, row, actionWidth, 35, 18); row += 40;
            ActionButton("🌌 Open Nuvio Web Player (nuvio.app)", () => { browser.Open("https://nuvio.app/"); panelMotion.Show(false); });

            Heading("Nuvio / Stremio Account Connection");
            if (account.SignedIn) {
                Heading("🟢 Connected Account: " + (account.DisplayName ?? "Nuvio User"));
                ActionButton("⚡ Sync Account Add-ons & Catalogs", account.SyncAddons);
                ActionButton("Sign out", account.SignOut);
            } else if (!string.IsNullOrEmpty(account.LinkCode)) {
                Heading("Sign-in code: " + account.LinkCode);
                Heading("Open this link on your phone, PC, or headset browser:");
                Label(account.LinkUrl, content, 0, row, 920, 50, 22); row += 58;
                ActionButton("Open sign-in in Quest browser", account.OpenLink);
                ActionButton("Cancel sign-in", account.CancelLink);
                Heading("Waiting for approval (will refresh automatically)…");
            } else {
                ActionButton("⚡ Pair with Device Link Code", account.BeginLink);
                Heading("— Or Sign In Directly with Credentials —");
                string accEmail = (string)settings[SettingsKeys.AccountEmail] ?? "";
                var emailInput = Field("Account Email", accEmail, v => { settings[SettingsKeys.AccountEmail] = v; Save(); });
                var passInput = PasswordField("Account Password", "", null);
                ActionButton("Sign In with Email & Password", () => account.LoginWithCredentials(emailInput.text, passInput.text));
            }

            Heading("Self-Hosted Nuvio / Mellon Profile Sync (Optional)");
            string mUrl = (string)settings[SettingsKeys.MellonUrl] ?? "http://192.168.1.166:8085";
            string mToken = (string)settings[SettingsKeys.MellonToken] ?? "";
            var mUrlInput = Field("Mellon / Nuvio Sync URL", mUrl, v => { settings[SettingsKeys.MellonUrl] = v; Save(); });
            var mTokenInput = PasswordField("Mellon Auth Token", mToken, v => { settings[SettingsKeys.MellonToken] = v; Save(); });
            ActionButton("Sync Self-Hosted Mellon Addons", async () => {
                if (reg == null) { ShowStatus("Provider registry is unavailable."); return; }
                if (string.IsNullOrWhiteSpace(mUrlInput.text) || string.IsNullOrWhiteSpace(mTokenInput.text))
                {
                    ShowStatus("Enter a Mellon / Nuvio URL and auth token first.");
                    return;
                }

                ShowStatus("Syncing Mellon profile addons...");
                try
                {
                    int count = await reg.SyncMellonAddonsAsync(mUrlInput.text, mTokenInput.text);
                    ShowStatus("Synced " + count + " addon(s) from Mellon into VR library!");
                    ServersHubPage();
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[CinemaApp] Mellon addon sync failed: " + error.Message);
                    ShowStatus("Mellon sync failed. Check the URL, token, and server.");
                }
            });
        }
        else if (serverHubTab == 2) {
            Heading("🎬 Stremio Account Pairing");
            if (account.SignedIn) {
                Heading("🟢 Signed in: " + (account.DisplayName ?? "Stremio Account"));
                ActionButton("⚡ Sync Stremio Account Add-ons", account.SyncAddons);
                ActionButton("Sign out from Stremio", account.SignOut);
            } else if (!string.IsNullOrEmpty(account.LinkCode)) {
                Heading("Sign-in code: " + account.LinkCode);
                Heading("Open this link on your phone, PC, or Quest browser:");
                Label(account.LinkUrl, content, 0, row, 920, 50, 22); row += 58;
                ActionButton("Open sign-in in Quest browser", account.OpenLink);
                ActionButton("Cancel sign-in", account.CancelLink);
                Heading("Waiting for Stremio approval (will refresh automatically)…");
            } else {
                ActionButton("⚡ Pair with Device Link Code", account.BeginLink);
                Heading("— Or Sign In Directly with Credentials —");
                string accEmail = (string)settings[SettingsKeys.AccountEmail] ?? "";
                var emailInput = Field("Stremio Email", accEmail, v => { settings[SettingsKeys.AccountEmail] = v; Save(); });
                var passInput = PasswordField("Stremio Password", "", null);
                ActionButton("Sign In with Email & Password", () => account.LoginWithCredentials(emailInput.text, passInput.text));
            }
            ActionButton("🎬 Open Stremio Web Player (web.stremio.com)", () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
            ActionButton("Manage Catalogs & Addon Manifests", AddonsPage);
        }
        else if (serverHubTab == 3) {
            Heading("🍿 Jellyfin / Remux Media Server");
            string statusText = jConn ? "🟢 Connected to Jellyfin" : "⚪ Disconnected";
            Heading(statusText);
            string jUrl = (string)settings[SettingsKeys.JellyfinUrl] ?? "";
            string jUser = (string)settings[SettingsKeys.JellyfinUser] ?? "";
            string jPass = (string)settings[SettingsKeys.JellyfinPass] ?? "";
            var jUrlInput = Field("Jellyfin URL (e.g. http://192.168.1.100:8096)", jUrl, v => { settings[SettingsKeys.JellyfinUrl] = v; Save(); });
            var jUserInput = Field("Jellyfin Username", jUser, v => { settings[SettingsKeys.JellyfinUser] = v; Save(); });
            var jPassInput = PasswordField("Jellyfin Password", jPass, v => { settings[SettingsKeys.JellyfinPass] = v; Save(); });
            ActionButton("Connect & Authenticate Jellyfin", async () => {
                if (reg?.Jellyfin != null && !string.IsNullOrEmpty(jUrlInput.text)) {
                    ShowStatus("Connecting to Jellyfin...");
                    bool ok = await reg.Jellyfin.AuthenticateAsync(jUrlInput.text, jUserInput.text, jPassInput.text);
                    ShowStatus(ok ? "Jellyfin connected successfully!" : "Jellyfin authentication failed.");
                    ServersHubPage();
                }
            });
            if (jConn) {
                ActionButton("Disconnect Jellyfin", () => {
                    reg.Jellyfin.RestoreSession("", "", "");
                    settings[SettingsKeys.JellyfinToken] = "";
                    settings[SettingsKeys.JellyfinUserId] = "";
                    Save();
                    ShowStatus("Disconnected from Jellyfin.");
                    ServersHubPage();
                });
            }
            ActionButton("🍿 Open Jellyfin Web Player", () => {
                string j = (string)settings[SettingsKeys.JellyfinUrl];
                if (!string.IsNullOrEmpty(j)) { browser.Open(j); panelMotion.Show(false); }
                else { ShowStatus("Enter your Jellyfin URL above first"); }
            });

            Heading("lostb1t / Standalone Remux Server");
            Label("Remux aggregates Stremio, WebDAV, and Debrid streams into a fast direct-play HTTP / HLS server.", content, 0, row, actionWidth, 35, 18); row += 40;
            string rUrl = (string)settings[SettingsKeys.RemuxUrl] ?? "";
            var rUrlInput = Field("Remux Server URL (e.g. http://192.168.1.166:3000)", rUrl, v => { settings[SettingsKeys.RemuxUrl] = v; Save(); });
            ActionButton("Test / Open Remux Web UI", () => {
                string r = rUrlInput.text?.Trim();
                if (!string.IsNullOrEmpty(r)) {
                    browser.Open(r);
                    panelMotion.Show(false);
                } else {
                    ShowStatus("Enter Remux Server URL first");
                }
            });
        }
        else if (serverHubTab == 4) {
            Heading("⚡ MediaStorm Debrid / Usenet Streamer");
            string msStatus = msConn ? "🟢 Connected" : "⚪ Standby";
            Heading("Status: " + msStatus);
            string msUrl = (string)settings[SettingsKeys.MediaStormUrl] ?? "";
            string msKey = (string)settings[SettingsKeys.MediaStormKey] ?? "";
            var msUrlInput = Field("MediaStorm URL (e.g. http://192.168.1.166:7777)", msUrl, v => { settings[SettingsKeys.MediaStormUrl] = v; Save(); });
            var msKeyInput = PasswordField("MediaStorm API Key", msKey, v => { settings[SettingsKeys.MediaStormKey] = v; Save(); });
            ActionButton("Connect MediaStorm", async () => {
                if (reg?.MediaStorm != null && !string.IsNullOrEmpty(msUrlInput.text)) {
                    ShowStatus("Connecting to MediaStorm...");
                    bool ok = await reg.MediaStorm.AuthenticateAsync(msUrlInput.text, msKeyInput.text);
                    ShowStatus(ok ? "MediaStorm connected successfully!" : "MediaStorm connection failed.");
                    ServersHubPage();
                }
            });
            if (msConn) {
                ActionButton("Disconnect MediaStorm", () => {
                    reg.MediaStorm.RestoreSession("", "");
                    settings[SettingsKeys.MediaStormKey] = "";
                    Save();
                    ShowStatus("Disconnected from MediaStorm.");
                    ServersHubPage();
                });
            }

            Heading("🚀 Silo High-Performance Backend");
            string siloStatus = siloConn ? "🟢 Connected" : "⚪ Standby";
            Heading("Status: " + siloStatus);
            string sUrl = (string)settings[SettingsKeys.SiloUrl] ?? "";
            string sUser = (string)settings[SettingsKeys.SiloUser] ?? "";
            string sPass = (string)settings[SettingsKeys.SiloPass] ?? "";
            var sUrlInput = Field("Silo Server URL", sUrl, v => { settings[SettingsKeys.SiloUrl] = v; Save(); });
            var sUserInput = Field("Silo Username / Token", sUser, v => { settings[SettingsKeys.SiloUser] = v; Save(); });
            var sPassInput = PasswordField("Silo Password", sPass, v => { settings[SettingsKeys.SiloPass] = v; Save(); });
            ActionButton("Connect Silo", async () => {
                if (reg?.Silo != null && !string.IsNullOrEmpty(sUrlInput.text)) {
                    ShowStatus("Connecting to Silo...");
                    bool ok = await reg.Silo.AuthenticateAsync(sUrlInput.text, sUserInput.text, sPassInput.text);
                    ShowStatus(ok ? "Silo connected successfully!" : "Silo connection failed.");
                    ServersHubPage();
                }
            });
            if (siloConn) {
                ActionButton("Disconnect Silo", () => {
                    reg.Silo.RestoreSession("", "");
                    settings[SettingsKeys.SiloToken] = "";
                    Save();
                    ShowStatus("Disconnected from Silo.");
                    ServersHubPage();
                });
            }
        }
        else if (serverHubTab == 5) {
            Heading("🖥 Virtual Web Screen Resolution & 4K Options");
            Label("Native movie streams (ExoPlayer/Hardware Decoder) play at up to 4K 60FPS automatically.\nSelect your preferred resolution for the interactive web desktop:", content, 0, row, actionWidth, 48, 18); row += 55;
            string cur = (string)settings[SettingsKeys.BrowserRes] ?? "1080p";

            ActionButton((cur == "1080p" ? "● " : "○ ") + "1080p Full HD (1920×1080) — Balanced & Crisp [Recommended]", () => { ApplyBrowserRes("1080p"); ServersHubPage(); });
            ActionButton((cur == "4k" ? "● " : "○ ") + "4K Ultra HD (3840×2160) — Maximum Desktop Sharpness", () => { ApplyBrowserRes("4k"); ServersHubPage(); });
            ActionButton((cur == "1440p" ? "● " : "○ ") + "1440p Quad HD (2560×1440) — High Density", () => { ApplyBrowserRes("1440p"); ServersHubPage(); });
            ActionButton((cur == "720p" ? "● " : "○ ") + "720p Standard HD (1280×720) — Battery Saver", () => { ApplyBrowserRes("720p"); ServersHubPage(); });

            Heading("Active Resolution: " + browser.WebWidth + "×" + browser.WebHeight);
        }

        ActionButton("🎛 Cinema Controls (3D SBS / Void / Bed Mode)", CinemaControlsPage);
        ActionButton("🎬 Open Stremio Web Player", () => { browser.Open("https://web.stremio.com/"); panelMotion.Show(false); });
        ActionButton("Back to media library", Library);
        Resize();
    }
}
}
