namespace StremioVR
{
    /// <summary>
    /// Centralized registry for all persistent PlayerPrefs and settings keys
    /// used across the Movie Night VR headset application.
    /// </summary>
    public static class SettingsKeys
    {
        // Locomotion & Comfort
        public const string MoveSpeed = "moveSpeed";
        public const string TurnSpeed = "turnSpeed";
        public const string SnapTurn = "snapTurn";
        public const string SnapAngle = "snapAngle";
        public const string HandOriented = "handOriented";
        public const string RayPitch = "rayPitch";
        public const string MenuButtonOption = "menuButtonOption";
        public const string SprintEnabled = "sprintEnabled";

        // Display & Video
        public const string Ambient = "ambient";
        public const string AmbientStrength = "ambientStrength";
        public const string BrowserRes = "browserRes";
        public const string WebFps = "webFps";
        public const string SubtitlesEnabled = "subtitlesEnabled";

        // Onboarding & State
        public const string HasOnboarded = "hasOnboarded";
        public const string LastServerTab = "lastServerTab";
        public const string StreamingService = "streamingService";
        public const string RecentHistory = "recent";
        public const string VideoType = "type";
        public const string Server = "server";

        // Accounts & Providers
        public const string AccountEmail = "accEmail";
        public const string JellyfinUrl = "jellyfinUrl";
        public const string JellyfinUser = "jellyfinUser";
        public const string JellyfinPass = "jellyfinPass";
        public const string MediaStormUrl = "mediastormUrl";
        public const string MediaStormKey = "mediastormKey";
        public const string SiloUrl = "siloUrl";
        public const string SiloUser = "siloUser";
        public const string SiloPass = "siloPass";
        public const string MellonUrl = "mellonUrl";
        public const string MellonToken = "mellonToken";
    }
}
