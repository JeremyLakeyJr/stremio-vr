using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace StremioVR {
public sealed class CinemaApp : MonoBehaviour {
    int layoutPageRevision=-1; RoomPlacement placement; TabletopGames games; LocalBody localBody; WebCinema browser; RoomClient room; CinemaPlayback playback; VoiceChat voice; MediaLibrary library; StremioAccount account; RigMotion motion; WorldManager world; Transform tracking; VideoAmbient ambient; PanelMotion panelMotion; float scrollTarget; bool historyContinue, historyLocal; JObject watching; string watchingUrl; float nextHistory;
    Transform rig, head, left, right, panel; Camera cameraView; Canvas canvas; Text status, members;
    RectTransform content; Font font; JObject settings, profile; float row; int pageGeneration; float actionWidth = 920; Text pageTitle;
    InputField serverInput, roomInput, streamInput; readonly List<RaycastResult> hits = new List<RaycastResult>();
    bool triggerWasDown; string shownRoom; GameObject hovered; LineRenderer pointer;
    string SettingsPath => Path.Combine(Application.persistentDataPath, "profile.json");
    void Awake() {
        Application.targetFrameRate = 72; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        try { settings = JObject.Parse(File.ReadAllText(SettingsPath)); } catch { settings = new JObject(); }
        profile = settings["profile"] as JObject ?? new JObject { ["name"] = "Guest", ["skin"] = "#b98059", ["color"] = "#7958ff", ["head"] = "round", ["body"] = "regular", ["hair"] = "none", ["outfit"] = "casual", ["accessory"] = "none" };
        room = gameObject.AddComponent<RoomClient>(); library = gameObject.AddComponent<MediaLibrary>();
        voice = gameObject.AddComponent<VoiceChat>(); voice.Initialize(room);
        room.Status += ShowStatus; library.Status += ShowStatus; voice.Status += ShowStatus;
        rig = new GameObject("XR Rig").transform;
        tracking = new GameObject("Tracking space").transform; tracking.SetParent(rig, false);
        head = new GameObject("Head").transform; head.SetParent(tracking); head.localPosition = new Vector3(0, 1.65f, 0);
        cameraView = head.gameObject.AddComponent<Camera>(); cameraView.clearFlags = CameraClearFlags.SolidColor; cameraView.nearClipPlane = 0.05f; cameraView.farClipPlane = 100; cameraView.cullingMask &= ~((1 << 30) | (1 << 29)); cameraView.backgroundColor = new Color(0.02f, 0.025f, 0.05f); head.gameObject.tag = "MainCamera"; head.gameObject.AddComponent<AudioListener>();
        cameraView.allowHDR=false;
        left = new GameObject("Left controller").transform; left.SetParent(tracking);
        right = new GameObject("Right controller").transform; right.SetParent(tracking);
        var presence = gameObject.AddComponent<AvatarPresence>(); presence.Initialize(room, rig, head, left, right);
        localBody=gameObject.AddComponent<LocalBody>(); localBody.Head=head; localBody.Left=left; localBody.Right=right; localBody.Room=room; localBody.Apply(profile);
        world = gameObject.AddComponent<WorldManager>(); world.Initialize(room);
        motion = gameObject.AddComponent<RigMotion>(); motion.Rig=rig; motion.Tracking=tracking; motion.Head=head; motion.Room=room; motion.World=world;
        motion.MoveSpeed=(float?)settings["moveSpeed"]??1.5f; motion.TurnSpeed=(float?)settings["turnSpeed"]??60; motion.RayPitch=(float?)settings["rayPitch"]??45;
        motion.MenuVisible=()=>placement!=null && placement.Active || panelMotion!=null && panelMotion.Visible || browser!=null && browser.Active; motion.Status+=ShowStatus; motion.Initialize();
        account=gameObject.AddComponent<StremioAccount>(); account.Status+=ShowStatus; account.AddonsImported+=ImportAccountAddons;
        account.HistoryLoaded+=()=>{if(pageTitle!=null && pageTitle.text=="Watch history") HistoryPage();};
        account.Changed+=()=> { if(pageTitle!=null && pageTitle.text=="Stremio account") AccountPage(); };
        playback = gameObject.AddComponent<CinemaPlayback>(); playback.Initialize(room, world.Screen); playback.Status += ShowStatus;
        ambient=gameObject.AddComponent<VideoAmbient>(); ambient.Video=playback.Video; ambient.Screen=world.Screen.transform; ambient.Enabled=(bool?)settings["ambient"]??true; ambient.Strength=(float?)settings["ambientStrength"]??0.65f;
        world.Changed+=()=>{ playback.SetScreen(world.Screen); ambient.Screen=world.Screen.transform; motion.ResetWorld(); Recenter(); };
        browser=gameObject.AddComponent<WebCinema>(); browser.Room=room; browser.Playback=playback; browser.World=world; browser.Ambient=ambient; browser.Status+=ShowStatus;
        games=gameObject.AddComponent<TabletopGames>();games.Initialize(room,world);games.Status+=ShowStatus;
        placement=gameObject.AddComponent<RoomPlacement>();placement.Room=room;placement.Head=head;placement.Status+=ShowStatus;
        placement.Finished+=()=>{panelMotion.Show(true);Recenter();LayoutPage();};
        BuildCanvas(); Library();
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
        if(library.Addons.Count>0) Library();
    }
    void Start() {
        room.StartSolo(profile);account.Resume();
#if UNITY_ANDROID && DEVELOPMENT_BUILD
        // Explicit development-only launch options make USB smoke tests repeatable.
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
        Button("My room", panel, 15, 175, 215, Home);
        Button("Discover", panel, 15, 245, 215, Library);
        Button("Watch history",panel,15,315,215,HistoryPage);
        Button("Avatar", panel, 15, 385, 215, Customize);
        Button("Settings / worlds", panel, 15, 455, 215, Comfort);

        Button("Toggle mic", panel, 15, 525, 215, () => voice.Toggle());
        Button("Toggle audio", panel, 15, 595, 215, () => voice.ToggleDeafen());
        Button("Stremio account", panel, 15, 665, 215, AccountPage);
        Button("Recenter", panel, 15, 755, 215, Recenter);
        Label("Hide menu to walk/turn\nLeft stick: move\nRight stick: turn / scroll", panel, 20, 835, 215, 120, 17);
        Plate(panel, 260, 910, 1000, 72, new Color(0.06f,0.05f,0.1f));
        Button("My room", panel, 275, 920, 220, Home); Button("Discover", panel, 510, 920, 220, Library); Button("Games", panel, 745, 920, 220, GamesPage); Button("Mic", panel, 980, 920, 260, () => voice.Toggle());
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect)); scrollGo.transform.SetParent(panel, false);
        Place(scrollGo.GetComponent<RectTransform>(), 280, 185, 950, 620); scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.05f); scrollGo.GetComponent<Mask>().showMaskGraphic = false;
        content = new GameObject("Content", typeof(RectTransform), typeof(PageFade)).GetComponent<RectTransform>(); content.SetParent(scrollGo.transform, false); Place(content, 0, 0, 930, 620);
        var scroll = scrollGo.GetComponent<ScrollRect>(); scroll.viewport = scrollGo.GetComponent<RectTransform>(); scroll.content = content; scroll.horizontal = false;
        pointer = new GameObject("Controller ray").AddComponent<LineRenderer>(); pointer.positionCount = 2; pointer.startWidth = 0.003f; pointer.endWidth = 0.001f; pointer.material = new Material(Shader.Find("Unlit/Color")); pointer.material.color = new Color(0.1f, 0.9f, 0.65f);
    }
    public void Recenter() { panel.position = head.position + head.forward * 1.8f; panel.rotation = Quaternion.LookRotation(head.forward, head.up); }
    void Clear() { scrollTarget=0; content.GetComponent<PageFade>().Begin(); actionWidth = 920; pageGeneration++; foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); } row = 0; content.anchoredPosition = Vector2.zero; }
    void Home() {
        Clear(); pageTitle.text = "My room"; Heading(room.IsSolo?"Solo mode — no server needed":"Room & playback");
        ActionButton("Solo mode / return to my apartment",()=>{if(browser.Active)browser.Close();room.StartSolo(profile);Home();});
        Heading("Solo: browse, watch direct videos, arrange your apartment, and play locally. Connect below for multiplayer.");
        ActionButton("Web browser / native player",()=>{browser.Toggle(); if(browser.Active)panelMotion.Show(false);});
        ActionButton("Web screen controls",BrowserControls);
        streamInput = Field("Direct video URL", "", null);
        ActionButton("Load video", () => playback.Load(streamInput.text.Trim(), "Direct stream"));
        ActionButton("Movie audio / track",AudioSettings);
        ActionButton("Play / pause", () => playback.Toggle());
        ActionButton("Seek −10 seconds", () => playback.Seek(-10)); ActionButton("Seek +10 seconds", () => playback.Seek(10));
        Heading("Multiplayer — connect only when inviting friends");
        serverInput = Field("Server WebSocket URL", (string)settings["server"] ?? "ws://192.168.1.100:3000/ws", value => { settings["server"] = value; Save(); });
        roomInput = Field("Room code (empty creates a room)", "", null);
        ActionButton("Connect / create room", () => { room.Connect(serverInput.text.Trim(), roomInput.text.Trim(), profile); });
        ActionButton("Leave room / return to solo", () => room.Leave());
        ActionButton("Hide / show panel: left controller menu button", () => panelMotion.Show(false));
    }
    void GamesPage() {
        Clear();pageTitle.text="Tabletop games";
        Heading("Place tables using Arrange room. Grip a chess piece, move it, and release above a green square. Trigger also works.");
        ActionButton("Arrange room",LayoutPage);
        if(room.IsSolo)ActionButton((bool?)room.State?["computer"]==false?"Practice both sides → play computer":"Play computer → practice both sides",()=>{room.Send(new JObject{["type"]="solo-opponent",["computer"]=(bool?)room.State?["computer"]==false});ShowStatus("Opponent updated. Refresh boards to see the mode.");});
        foreach(string id in new[]{"chess","tictactoe"}) {string game=id;Heading(id=="chess"?"Chess":"Tic-tac-toe");Heading(games.Description(id));
            if(!room.IsSolo){ActionButton("Join "+id,()=>{games.Command(game,"join");});ActionButton("Leave "+id,()=>games.Command(game,"leave"));}ActionButton("Reset "+id,()=>games.Command(game,"reset"));
        }
        ActionButton("Promotion: "+games.Promotion.ToUpperInvariant(),()=>{string choices="qrbn";games.SetPromotion(choices[(choices.IndexOf(games.Promotion)+1)%4].ToString());GamesPage();});
        Heading("Chess Set by Jacob G · CC BY-ND 4.0\nprintables.com/model/32741-chess-set\ncreativecommons.org/licenses/by-nd/4.0/\nOriginal geometry; technical format conversion only. Supplied as-is.");ActionButton("Refresh boards",GamesPage);ActionButton("Hide menu / play",()=>panelMotion.Show(false));
    }
    void BeginPlacement(string kind,string id=null) {
        if(browser.Active)browser.Close();placement.Begin(kind,id);if(placement.Active)panelMotion.Show(false);
    }
    void LayoutPage() {
        Clear();pageTitle.text="Arrange room";layoutPageRevision=(int?)room.State?["layout"]?["revision"]??-1;
        Heading(room.IsHost?"Aim to place; use sticks to adjust. Trigger confirms; menu cancels.":"Only the room host can place, move, or remove objects.");
        ActionButton("Add movie screen",()=>BeginPlacement("screen"));
        var items=room.State?["layout"]?["items"] as JArray??new JArray();
        foreach(string kind in new[]{"chess","tictactoe"}) {bool found=false;foreach(var item in items)if((string)item["kind"]==kind)found=true;if(!found){string type=kind;ActionButton("Place "+kind+" table",()=>BeginPlacement(type));}}
        int number=0;foreach(var item in items) {string id=(string)item["id"],kind=(string)item["kind"];Heading((++number)+" · "+kind);ActionButton("Move "+kind,()=>BeginPlacement(kind,id));ActionButton("Remove "+kind,()=>placement.Remove(id));}
        Heading("Up to six extra screens and one table for each game. Solo layouts save on this headset; multiplayer layouts last for the room.");
        ActionButton("Refresh layout",LayoutPage);
    }
    void AudioSettings() {
        Clear();pageTitle.text="Movie audio";Heading(playback.AudioInfo);
        ActionButton("Next audio track",()=>{playback.NextAudioTrack();ShowStatus("Preparing selected audio track…");});
        SpeedControl("Movie volume",playback.Volume,0,1,0.1f,value=>playback.SetVolume(value),"",AudioSettings);
        ActionButton("Refresh audio information",AudioSettings);
        Heading("If a source uses unsupported AC-3/DTS audio, choose an AAC source or configure a compatible transcoding service. Voice volume is separate.");
    }
    void BrowserControls() {
        Clear(); pageTitle.text="Web browser";
        Heading("Your private browser — account session stays on this headset");
        ActionButton(browser.Active?"Return to native player":"Open browser on the screen",()=>{browser.Toggle();BrowserControls();});
        ActionButton("Hide menu / interact with the screen",()=>panelMotion.Show(false));
        var address=Field("Web address", "https://web.stremio.com/",null);
        ActionButton("Go to address",()=>browser.Navigate(address.text));
        ActionButton("New tab",()=>{browser.NewTab(address.text);ShowStatus("Tab opened. Refresh tabs to see it.");});
        foreach(var tab in browser.Tabs){int index=(int)tab["index"];Heading(((bool)tab["active"]?"● ":"")+(string)tab["title"]);ActionButton("Switch to tab "+(index+1),()=>{browser.SelectTab(index);panelMotion.Show(false);});ActionButton("Close tab "+(index+1),()=>{browser.CloseTab(index);ShowStatus("Tab closed. Refresh tabs to update the list.");});}
        ActionButton("Refresh tabs",BrowserControls);
        ActionButton("Back",browser.Back);ActionButton("Forward",browser.Forward); ActionButton("Reload",browser.Reload);
        Field("Type into the selected web input", "",browser.Text);
        ActionButton(room.IsSolo?"Play this HTTP video on native screen":"Share playing HTTP video with room",browser.Share);
        ActionButton("Forget web-screen sign-in",browser.ForgetSession);
        Heading("Select a web input with the beam, then enter text here. Web video plays locally until shared.");
    }
    void AddonsPage() {
        pageTitle.text = "Add-ons";
        Clear(); Heading("Stremio add-ons");
        ActionButton(account.SignedIn ? "Account / sync add-ons" : "Sign in to Stremio / import add-ons", AccountPage);
        var addonUrl = Field("Manifest URL", "https://v3-cinemeta.strem.io/manifest.json", null);
        ActionButton("Install add-on", () => library.Install(addonUrl.text, () => { SaveAddons(); Library(); }));
        Field("Stremio streaming service URL (optional)", (string)settings["streamingService"] ?? "", value => { settings["streamingService"] = value; Save(); });
        Heading("Installed catalogs");
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
        Field("Video type", (string)settings["type"] ?? "movie", value => settings["type"] = value);
        var id = Field("Video ID (e.g. tt1254207 or series episode ID)", "", null);
        ActionButton("Find streams by ID", () => Streams((string)settings["type"] ?? "movie", id.text.Trim(), id.text.Trim()));
    }
    void Library() {
        Clear(); pageTitle.text="Discover";
        Heading("Your cinema. Your collection.");
        ActionButton("Manage add-ons / Stremio account",AddonsPage);
        if(library.Addons.Count==0) { Heading("Sign in to bring your Stremio add-ons here."); ActionButton("Link Stremio account",AccountPage); ActionButton("Install Cinemeta catalog",()=>library.Install("https://v3-cinemeta.strem.io/manifest.json",()=>{SaveAddons(); Library();})); return; }
        JObject featuredAddon=null, featuredCatalog=null; int tabs=0;
        foreach(var addon in library.Addons) foreach(var token in addon["catalogs"] as JArray??new JArray()) {
            var selected=addon; var catalog=(JObject)token;
            if(featuredCatalog==null || (string)addon["id"]=="com.linvo.cinemeta" && (string)catalog["type"]=="movie" && (string)catalog["id"]=="top") { featuredAddon=addon; featuredCatalog=catalog; }
            if(tabs<3) { Button(((string)catalog["name"]??(string)catalog["id"])+" / "+(string)catalog["type"],content,tabs*310,row,298,()=> {
                int generation=pageGeneration; ShowStatus("Loading posters…"); library.Catalog(selected,catalog,items=>{if(generation==pageGeneration) Catalog(selected,(string)catalog["type"],items);});
            }); tabs++; }
        }
        row+=70; ActionButton("All catalogs and add-ons",AddonsPage);
        if(featuredCatalog!=null) {
            Heading("Featured titles"); int generation=pageGeneration; var addon=featuredAddon; string type=(string)featuredCatalog["type"];
            library.Catalog(addon,featuredCatalog,items=>{if(generation==pageGeneration) PosterGrid(addon,type,items,"");});
        }
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
            var poster=new GameObject("Poster",typeof(RectTransform),typeof(RawImage),typeof(PosterImage)); poster.transform.SetParent(card.transform,false); Place(poster.GetComponent<RectTransform>(),6,6,204,288); poster.GetComponent<RawImage>().color=new Color(0.2f,0.17f,0.28f); poster.GetComponent<RawImage>().raycastTarget=false; poster.GetComponent<PosterImage>().Load((string)item["poster"]);
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
    void Streams(string type, string id, string title, double start=0) {
        Clear(); ActionButton("Back to library", Library); Heading("Sources: " + title); ShowStatus("Finding streams…"); int generation = pageGeneration;
        library.Streams(type, id, streams => {
            if (generation != pageGeneration) return;
            foreach (var token in streams) { var stream = (JObject)token;
                if(room.IsSolo && stream["externalUrl"]!=null){string website=(string)stream["externalUrl"];ActionButton("Open website: "+((string)stream["name"]??"Source"),()=>{try{browser.Open(MediaLibrary.Http(website));panelMotion.Show(false);}catch(Exception error){ShowStatus(error.Message);}});continue;}
                ActionButton((string)stream["name"] ?? (string)stream["title"] ?? "Stream", () => { try { library.ResolveStream(stream, (string)settings["streamingService"], url => { watching=new JObject { ["_id"]=id,["name"]=title,["type"]=type,["state"]=new JObject() }; watchingUrl=url; playback.Load(url, title, start); }); } catch (Exception error) { ShowStatus(error.Message); } }); }
            ShowStatus(streams.Count == 0 ? "No streams. Install a stream-provider add-on." : "Choose a source, then use Room → Play.");
        });
    }
    static bool HasEndpoint(JArray values,string endpoint) { foreach(var value in values) if((string)value==endpoint) return true; return false; }
    void ImportAccountAddons(JArray descriptors) {
        var previous=new HashSet<string>(); foreach(var endpoint in settings["accountAddons"] as JArray??new JArray()) previous.Add((string)endpoint);
        library.Addons.RemoveAll(addon=>previous.Contains((string)addon["endpoint"]));
        var imported=new JArray(); int skipped=0;
        foreach(var descriptor in descriptors) {
            try { var addon=MediaLibrary.AccountAddon((JObject)descriptor); string endpoint=(string)addon["endpoint"]; library.Addons.RemoveAll(a=>(string)a["endpoint"]==endpoint); library.Addons.Add(addon); imported.Add(endpoint); }
            catch { skipped++; }
        }
        settings["accountAddons"]=imported; var cache=new JArray(); foreach(var addon in library.Addons) if(HasEndpoint(imported,(string)addon["endpoint"])) cache.Add(addon.DeepClone()); settings["accountCache"]=cache; SaveAddons();
        if(pageTitle.text=="Discover") Library();
        ShowStatus("Imported "+imported.Count+" account add-ons"+(skipped>0?"; "+skipped+" unsupported entries skipped":""));
    }
    void AccountPage() {
        Clear(); pageTitle.text="Stremio account";
        if(account.SignedIn) { Heading("Signed in: "+(account.DisplayName??"Stremio account")); ActionButton("Sync my account add-ons",account.SyncAddons); ActionButton("Sign out and remove imported add-ons",account.SignOut); }
        else if(!string.IsNullOrEmpty(account.LinkCode)) {
            Heading("Sign-in code: "+account.LinkCode); Heading("Open this link on your phone or computer:");
            Label(account.LinkUrl,content,0,row,920,100,22); row+=110;
            ActionButton("Open sign-in in Quest browser",account.OpenLink); ActionButton("Cancel sign-in",account.CancelLink);
            Heading("Waiting for Stremio approval…");
        } else { Heading("Sign in on your phone, computer, or Quest browser"); ActionButton("Link my Stremio account",account.BeginLink); }
        ActionButton("Back to media library",Library);
    }
    void SpeedControl(string title,float current,float min,float max,float step,Action<float> changed,string unit,Action refresh=null) {
        Label(title+": "+current.ToString("0.##")+" "+unit,content,0,row,540,52,23);
        Button("−",content,570,row,150,()=>{changed(Mathf.Max(min,current-step));if(refresh!=null)refresh();else Comfort();});
        Button("+",content,740,row,150,()=>{changed(Mathf.Min(max,current+step));if(refresh!=null)refresh();else Comfort();}); row+=70; Resize();
    }
    void HistoryPage() {
        Clear(); pageTitle.text="Watch history";
        Button(historyLocal?"Source: this headset":"Source: Stremio account",content,0,row,450,()=>{historyLocal=!historyLocal;HistoryPage();});
        Button(historyContinue?"Continue watching":"All watched titles",content,470,row,450,()=>{historyContinue=!historyContinue;HistoryPage();}); row+=70;
        if(!historyLocal) { if(!account.SignedIn) { ActionButton("Sign in to view Stremio history",AccountPage); return; } ActionButton("Refresh Stremio history",account.RefreshHistory); }
        var entries=historyLocal?(settings["recent"] as JArray??new JArray()):account.History;
        int count=0;
        foreach(var token in entries) {
            var item=(JObject)token; var state=item["state"] as JObject??new JObject(); double offset=(double?)state["timeOffset"]??0, duration=(double?)state["duration"]??0;
            if(historyContinue && (offset<=0 || duration>0 && offset>=duration*0.95)) continue;
            string name=(string)item["name"]??"Untitled", type=(string)item["type"]??"movie", id=(string)state["video_id"]??(string)item["_id"];
            double resume=duration>0 && offset>=duration*0.95?0:offset/1000;
            ActionButton(name+"  ·  "+(resume>0?"resume at "+TimeSpan.FromSeconds(resume).ToString(@"hh\:mm\:ss"):"watched"),()=>Streams(type,id,name,resume)); count++;
        }
        if(count==0) Heading("No watched titles in this view yet.");
    }
    void RecordHistory() {
        if(watching==null || playback.SourceUrl!=watchingUrl || !playback.Video.isPlaying || Time.unscaledTime<nextHistory) return;
        nextHistory=Time.unscaledTime+15; var state=(JObject)watching["state"]; state["lastWatched"]=DateTime.UtcNow; state["timeOffset"]=Math.Max(0,playback.Position*1000); if(playback.Duration>0) state["duration"]=playback.Duration*1000;
        var recent=settings["recent"] as JArray??new JArray(); for(int i=recent.Count-1;i>=0;i--) if((string)recent[i]["_id"]==(string)watching["_id"]) recent.RemoveAt(i); recent.Insert(0,watching.DeepClone()); while(recent.Count>100) recent.RemoveAt(recent.Count-1); settings["recent"]=recent; Save();
    }
    void ChangeWorld(string id) {
        if(room.State==null) world.Load(id);
        else if(room.IsHost) room.Send(new JObject { ["type"]="world",["world"]=id });
        else ShowStatus("Only the host can change the world");
    }
    void Comfort() {
        Clear(); pageTitle.text="Settings";
        ActionButton("Movie Night source / AGPLv3",()=>Application.OpenURL("https://github.com/JeremyLakeyJr/stremio-vr"));
        Heading("Movement & turning");
        SpeedControl("Walk speed",motion.MoveSpeed,0.5f,3,0.25f,value=>{motion.MoveSpeed=value;settings["moveSpeed"]=value;Save();},"m/s");
        SpeedControl("Smooth turn speed",motion.TurnSpeed,15,180,15,value=>{motion.TurnSpeed=value;settings["turnSpeed"]=value;Save();},"°/s");
        Heading("Open Concept Home");
        ActionButton("Arrange room",LayoutPage);
        ActionButton("Mirror: "+(world.MirrorVisible?"On":"Off"),()=>{world.MirrorVisible=!world.MirrorVisible;Comfort();});
        if(Resources.Load<GameObject>("Worlds/Home")) ActionButton("Open Concept Home (host)",()=>ChangeWorld("home"));
        ActionButton("Ambient video lighting: "+(ambient.Enabled?"On":"Off"),()=>{ambient.Enabled=!ambient.Enabled;settings["ambient"]=ambient.Enabled;Save();Comfort();});
        ActionButton("Ambient strength: "+Mathf.RoundToInt(ambient.Strength*100)+"%",()=>{ambient.Strength=ambient.Strength>=1?0.25f:Mathf.Min(1,ambient.Strength+0.25f);settings["ambientStrength"]=ambient.Strength;Save();Comfort();});
        Heading("Hide the menu: left stick walks, right stick turns.");
        ActionButton("Stand up / return to aisle",motion.Stand);
        ActionButton("Calibrate reclined view (look where you want the screen)",()=> { motion.CalibrateReclined(); Recenter(); Comfort(); });
        ActionButton("View angle +5°",()=> { motion.AdjustView(5); Recenter(); }); ActionButton("View angle −5°",()=> { motion.AdjustView(-5); Recenter(); });
        ActionButton("Reset view angle",()=> { motion.ResetView(); Recenter(); });
        ActionButton("Pointer angle: "+motion.RayPitch+"°  /  change by 15°",()=> { motion.RayPitch=motion.RayPitch>=75?-30:motion.RayPitch+15; settings["rayPitch"]=motion.RayPitch; Save(); Comfort(); });
        Heading("Chairs — or point at a chair and press the trigger");
        for(int i=0;i<world.Data.Seats.Length;i++) { int seat=i; ActionButton("Sit in chair "+(i+1),()=> { motion.RequestSeat(seat); panelMotion.Show(false); }); }
    }
    void Customize() {
        Clear(); pageTitle.text = "Avatar studio"; Heading("R6 avatar — saved on this device");
        actionWidth = 500;
        var previewObject = new GameObject("R6 preview", typeof(RectTransform), typeof(RawImage), typeof(AvatarPreview));
        previewObject.transform.SetParent(content, false); Place(previewObject.GetComponent<RectTransform>(), 550, 150, 370, 490);
        previewObject.GetComponent<RawImage>().raycastTarget = false;
        var preview = previewObject.GetComponent<AvatarPreview>(); preview.Initialize(profile);
        Button("Turn left", content, 550, 655, 175, () => preview.Turn(-30)); Button("Turn right", content, 740, 655, 180, () => preview.Turn(30));
        Label("Your character\nChanges sync to the room", content, 550, 720, 370, 90, 19);
        Field("Name", (string)profile["name"], value => { profile["name"] = value; UpdateProfile(); });
        Choice("head", new[] { "round", "square" }); Choice("body", new[] { "regular", "wide" }); Choice("hair", new[] { "none", "short", "tall" });
        Choice("skin", new[] { "#f5d0ad", "#d7a17b", "#b98059", "#815335", "#452b20" });
        Choice("outfit", new[] { "casual", "formal", "sport" }); Choice("color", new[] { "#7958ff", "#238caa", "#ce5353", "#333333" });
        Choice("accessory", new[] { "none", "glasses", "hat", "headset" });
        content.sizeDelta = new Vector2(930, Mathf.Max(830, row));
    }
    void Choice(string key, string[] values) { ActionButton(key + ": " + profile[key] + "  →", () => { int index = Array.IndexOf(values, (string)profile[key]); profile[key] = values[(index + 1) % values.Length]; UpdateProfile(); Customize(); }); }
    void UpdateProfile() { localBody.Apply(profile); Save(); if (room.Connected && room.State != null) room.Send(new JObject { ["type"] = "profile", ["profile"] = profile }); }
    void SaveAddons() { var urls = new JArray(); foreach (var addon in library.Addons) urls.Add(MediaLibrary.ResourceUrl((string)addon["endpoint"],"manifest.json")); settings["addons"] = urls; Save(); }
    void Save() { settings["profile"] = profile; try { File.WriteAllText(SettingsPath, settings.ToString()); } catch (Exception error) { ShowStatus("Could not save settings: " + error.Message); } }
    void ShowStatus(string message) { if (status != null) status.text = message; if (Debug.isDebugBuild) Debug.Log("[Cinema] " + message); }
    void Heading(string text) { Label(text, content, 0, row, 920, 45, 24); row += 55; Resize(); }
    void ActionButton(string text, Action click) { Button(text, content, 0, row, actionWidth, click); row += 65; Resize(); }
    InputField Field(string label, string value, Action<string> changed) {
        Label(label, content, 0, row, 920, 28, 18); row += 30;
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(InputField)); go.transform.SetParent(content, false); Place(go.GetComponent<RectTransform>(), 0, row, 920, 48); go.GetComponent<Image>().color = new Color(0.13f, 0.15f, 0.22f);
        var text = Label("", go.transform, 12, 4, 890, 40, 21); text.supportRichText = false;
        var field = go.GetComponent<InputField>(); field.textComponent = text; field.text = value; field.characterLimit = 4096;
        field.onEndEdit.AddListener(result => changed?.Invoke(result)); row += 65; Resize(); return field;
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
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, width, height); go.GetComponent<Image>().color = color; go.GetComponent<Image>().raycastTarget = false;
    }
    static void Place(RectTransform rect, float x, float y, float w, float h) { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
    bool menuWasDown;
    void Update() {
        RecordHistory();
        var controller = InputDevices.GetDeviceAtXRNode(XRNode.RightHand); var leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        leftController.TryGetFeatureValue(CommonUsages.menuButton, out bool menu);
        if ((menu && !menuWasDown) || Input.GetKeyDown(KeyCode.Tab)) { if(placement.Active)placement.Cancel();else {panelMotion.Show(!panelMotion.Visible); if (panelMotion.Visible) {if(browser.Active&&room.IsHost)BrowserControls();Recenter();}} } menuWasDown = menu;

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
        if(games.Grip(left,right,leftGrip,rightGrip,!panelMotion.Visible&&!browser.Active)){pointer.enabled=false;controller.TryGetFeatureValue(CommonUsages.triggerButton,out triggerWasDown);return;}
        var plane = new Plane(panel.forward, panel.position);
        float distance=0; bool intersects = panelMotion.Visible && plane.Raycast(ray, out distance) && distance < 10;
        var point = intersects ? ray.GetPoint(distance) : ray.GetPoint(3);
        pointer.SetPosition(0, origin); pointer.SetPosition(1, point);
        var data = new PointerEventData(EventSystem.current) { position = cameraView.WorldToScreenPoint(point) };
        if (!XRSettings.isDeviceActive) data.position = Input.mousePosition;
        hits.Clear(); if (panelMotion.Visible && (intersects || !XRSettings.isDeviceActive)) canvas.GetComponent<GraphicRaycaster>().Raycast(data, hits);
        var target = hits.Count > 0 ? hits[0].gameObject : null;
        controller.TryGetFeatureValue(CommonUsages.triggerButton,out bool webTrigger); controller.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 webStick);
        bool webHit=browser.Point(ray,target==null && (webTrigger||Input.GetMouseButton(0)),target==null?webStick.y:0,out var webPoint);
        if(webHit && target==null) pointer.SetPosition(1,webPoint);
        TabletopTarget tableTarget=null; SeatTarget chair=null;
        if(target==null && !webHit && Physics.Raycast(ray,out var worldHit,12,~((1<<30)|(1<<8)),QueryTriggerInteraction.Collide)) { chair=worldHit.collider.GetComponent<SeatTarget>(); tableTarget=worldHit.collider.GetComponent<TabletopTarget>(); pointer.SetPosition(1,worldHit.point); }
        pointer.material.color=chair!=null||tableTarget!=null?Color.yellow:new Color(0.1f,0.9f,0.65f);
        if(!triggerWasDown && (webTrigger||Input.GetMouseButton(0))) {if(chair!=null)motion.RequestSeat(chair.Index);if(tableTarget!=null)tableTarget.Activate();}
        if (target != hovered) { if (hovered != null) ExecuteEvents.ExecuteHierarchy(hovered, data, ExecuteEvents.pointerExitHandler); hovered = target; if (hovered != null) ExecuteEvents.ExecuteHierarchy(hovered, data, ExecuteEvents.pointerEnterHandler); }
        controller.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger); trigger |= Input.GetMouseButton(0);
        if (trigger && !triggerWasDown && target != null) {
            var field = target.GetComponentInParent<InputField>();
            if (field != null) StartCoroutine(Edit(field));
            else ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerClickHandler);
        }
        triggerWasDown = trigger;
        controller.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        float scroll = (panelMotion.Visible ? stick.y : 0) * Time.unscaledDeltaTime * 600 + Input.mouseScrollDelta.y * 35;
        scrollTarget=Mathf.Clamp(scrollTarget-scroll,0,Mathf.Max(0,content.sizeDelta.y-620)); content.anchoredPosition=Vector2.Lerp(content.anchoredPosition,new Vector2(0,scrollTarget),1-Mathf.Exp(-14*Time.unscaledDeltaTime));
    }
    bool editing;
    IEnumerator Edit(InputField field) {
        if (editing) yield break;
        editing = true;
        var keyboard = new GameObject("Keyboard", typeof(RectTransform), typeof(Image)); keyboard.transform.SetParent(panel, false);
        Place(keyboard.GetComponent<RectTransform>(), 0, 0, 1280, 1000); keyboard.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.09f);
        string original = field.text; bool upper = false;
        var preview = Label(field.text, keyboard.transform, 25, 25, 950, 120, 23);
        string keys = "1234567890-_=qwertyuiop[]asdfghjkl;:'zxcvbnm,./?@!#$%&*()+";
        for (int i = 0; i < keys.Length; i++) {
            char key = keys[i]; int index = i;
            Button(key.ToString(), keyboard.transform, 25 + index % 12 * 79, 180 + index / 12 * 70, 73, () => {
                if (field != null && field.text.Length < 4096) { field.text += upper ? char.ToUpperInvariant(key) : key; preview.text = field.text; }
            });
        }
        Button("Shift", keyboard.transform, 25, 600, 220, () => { upper = !upper; preview.text = (upper ? "SHIFT ON: " : "") + field.text; });
        Button("Space", keyboard.transform, 260, 600, 220, () => { field.text += " "; preview.text = field.text; });
        Button("Backspace", keyboard.transform, 495, 600, 220, () => { if (field.text.Length > 0) field.text = field.text.Substring(0, field.text.Length - 1); preview.text = field.text; });
        Button("Clear", keyboard.transform, 730, 600, 220, () => { field.text = ""; preview.text = ""; });
        Button("Done", keyboard.transform, 25, 700, 450, () => { field.onEndEdit.Invoke(field.text); editing = false; Destroy(keyboard); });
        Button("Cancel", keyboard.transform, 500, 700, 450, () => { field.text = original; editing = false; Destroy(keyboard); });
        yield return null;
    }
}
}
