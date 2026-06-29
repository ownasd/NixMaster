using System.IO;
using Newtonsoft.Json;

namespace NixMaster.Core
{
    public class LineConfig
    {
        public string LineName { get; set; } = "Line 1";
        public string FirebaseUrl { get; set; } = "https://nix-traceability-default-rtdb.firebaseio.com";
        public string NodePath { get; set; } = "EndToEndTraceability";
    }

    public class AppSettings
    {
        public string FirebaseUrl      { get; set; } = "https://nix-traceability-default-rtdb.firebaseio.com";
        public string NodePath         { get; set; } = "EndToEndTraceability";
        public int    RefreshInterval  { get; set; } = 30;    // seconds
        public bool   AutoRefresh      { get; set; } = true;
        public string User             { get; set; } = "";
        public System.Collections.Generic.List<string> SubAssyProducts { get; set; } = new System.Collections.Generic.List<string> { "IR PCBA SUB ASSy", "IR INDICATION PCBA SUB ASSY", "Camera sub assy" };
        public System.Collections.Generic.List<LineConfig> Lines { get; set; } = new System.Collections.Generic.List<LineConfig> {
            new LineConfig { LineName = "Camera Controller Unit", FirebaseUrl = "https://nix-traceability-default-rtdb.firebaseio.com", NodePath = "EndToEndTraceability" }
        };
    }

    public static class AppState
    {
        public static AppSettings Settings    { get; set; } = new AppSettings();
        public static string      CurrentUser { get; set; } = "Unknown";

        private static readonly string SettingsFile = "nixmaster_settings.json";

        public static void LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                var json   = File.ReadAllText(SettingsFile);
                var loaded = JsonConvert.DeserializeObject<AppSettings>(json);
                if (loaded != null) Settings = loaded;
            }
        }

        public static void SaveSettings()
        {
            File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));
        }
    }
}
