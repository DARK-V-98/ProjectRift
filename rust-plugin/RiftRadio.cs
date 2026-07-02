// RiftRadio — in-game radio for PROJECT RIFT.
//
// Players deploy a Boombox, then /radio opens a menu of Sri Lankan stations.
// Picking one plays it on the nearest boombox (range-based — people nearby hear
// it, ~30m, standard Rust behaviour). Stations are also added to the vanilla
// boombox radio wheel as a fallback.
//
//   /radio        open the station menu
//   /radio off    stop the nearest boombox
//
// NOTE: Rust boomboxes can only play DIRECT audio stream URLs (Icecast/Shoutcast
// .mp3/.aac). YouTube links do NOT work (not a raw stream). Put working stream
// URLs in the config.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RiftRadio", "ESYSTEMLK", "1.0.0")]
    [Description("In-game Sri Lankan radio: /radio menu sets the nearest boombox station.")]
    public class RiftRadio : RustPlugin
    {
        private const string UiRoot = "riftradio.menu";
        private Configuration config;
        private readonly HashSet<ulong> openMenus = new HashSet<ulong>();

        public class Station
        {
            public string Name;
            public string Url;
        }

        public class Configuration
        {
            public float FindRadius = 6f;                 // how close you must be to a boombox
            public bool AddToRadioWheel = true;           // also expose on the vanilla boombox dial
            public bool AutoBindKey = true;               // auto-bind a key for every player
            public string BindKey = "keypad5";            // key that opens the radio menu
            public List<Station> Stations = new List<Station>
            {
                // Direct stream URLs sourced from radio-browser.info (verify/replace if any go down).
                new Station { Name = "Hiru FM",    Url = "https://radio.lotustechnologieslk.net:2020/stream/hirufmgarden/stream/1/" },
                new Station { Name = "Gold FM",    Url = "https://radio.lotustechnologieslk.net:2020/stream/goldfmgarden" },
                new Station { Name = "Sooriyan FM", Url = "https://radio.lotustechnologieslk.net:8006/" },
                new Station { Name = "Y FM",       Url = "http://live.trusl.com:1180/;" },
                new Station { Name = "Sirasa FM",  Url = "http://live.trusl.com:1170/;" },
                new Station { Name = "Yes FM",     Url = "http://live.trusl.com:1150/" },
                new Station { Name = "Neth FM",    Url = "https://cp11.serverse.com/proxy/nethfm/stream" },
                new Station { Name = "Ran FM",     Url = "http://207.148.74.192:7860/ran.mp3" },
                new Station { Name = "E FM",       Url = "http://207.148.74.192:7860/stream.mp3" },
                new Station { Name = "Sitha FM",   Url = "http://shaincast.caster.fm:48148/listen.mp3" },
                new Station { Name = "Real FM",    Url = "https://srv02.onlineradio.voaplus.com/realfm" },
                new Station { Name = "SLBC City FM", Url = "http://220.247.227.20:8000/citystream" },
                new Station { Name = "SLBC Radio Sri Lanka", Url = "http://220.247.227.20:8000/RSLstream" },
            };
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<Configuration>() ?? new Configuration(); }
            catch { config = new Configuration(); }
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(config);

        private void OnServerInitialized()
        {
            // de-duplicate stations (a doubled config would overflow the menu)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = config.Stations.Where(s => s != null && !string.IsNullOrEmpty(s.Url) && seen.Add(s.Url)).ToList();
            if (cleaned.Count != config.Stations.Count)
            {
                config.Stations = cleaned;
                SaveConfig();
                Puts($"Removed duplicate stations — {cleaned.Count} unique remain.");
            }

            if (config.AddToRadioWheel) ApplyWheel();
            if (config.AutoBindKey)
                foreach (var p in BasePlayer.activePlayerList) BindFor(p);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UiRoot);
                if (config.AutoBindKey && !string.IsNullOrEmpty(config.BindKey))
                    p.SendConsoleCommand($"bind {config.BindKey} \"\"");   // clean up on unload
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (config.AutoBindKey && player != null)
                timer.Once(8f, () => { if (player != null && player.IsConnected) BindFor(player); });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiRoot);
            openMenus.Remove(player.userID);
        }

        // Auto-bind the radio key on the player's client (no manual /bind needed).
        private void BindFor(BasePlayer player)
        {
            if (player == null || string.IsNullOrEmpty(config.BindKey)) return;
            player.SendConsoleCommand($"bind {config.BindKey} riftradio.toggle");
        }

        // Register the stations on the vanilla boombox dial via the server convar.
        private void ApplyWheel()
        {
            try
            {
                var parts = new List<string>();
                foreach (var s in config.Stations)
                    if (!string.IsNullOrEmpty(s.Name) && !string.IsNullOrEmpty(s.Url))
                        parts.Add($"{s.Name},{s.Url}");
                if (parts.Count == 0) return;
                string value = string.Join(",", parts);
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "boombox.serverurllist", value);
                Puts($"Registered {parts.Count} radio stations on the boombox wheel.");
            }
            catch (Exception ex) { PrintWarning($"Radio wheel setup failed: {ex.Message}"); }
        }

        // ---- commands -------------------------------------------------------
        [ChatCommand("radio")]
        private void CmdRadio(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;
            if (args.Length > 0 && args[0].ToLower() == "off")
            {
                StopNearest(player);
                return;
            }
            OpenMenu(player);
        }

        [ConsoleCommand("riftradio.play")]
        private void CcPlay(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int i = arg.GetInt(0, -1);
            if (i < 0 || i >= config.Stations.Count) return;
            PlayNearest(player, config.Stations[i]);
        }

        [ConsoleCommand("riftradio.stop")]
        private void CcStop(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) StopNearest(player);
        }

        [ConsoleCommand("riftradio.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiRoot);
            openMenus.Remove(player.userID);
        }

        // Bindable: open if closed, close if open.
        [ConsoleCommand("riftradio.toggle")]
        private void CcToggle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (openMenus.Contains(player.userID))
            {
                CuiHelper.DestroyUi(player, UiRoot);
                openMenus.Remove(player.userID);
            }
            else OpenMenu(player);
        }

        [ConsoleCommand("riftradio.open")]
        private void CcOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) OpenMenu(player);
        }

        // ---- boombox control ------------------------------------------------
        private DeployableBoomBox FindNearest(BasePlayer player)
        {
            var list = new List<DeployableBoomBox>();
            Vis.Entities(player.transform.position, config.FindRadius, list);
            DeployableBoomBox best = null;
            float bestD = float.MaxValue;
            foreach (var b in list)
            {
                if (b == null || b.IsDestroyed) continue;
                float d = Vector3.Distance(player.transform.position, b.transform.position);
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        private void PlayNearest(BasePlayer player, Station station)
        {
            var box = FindNearest(player);
            if (box == null)
            {
                player.ChatMessage("<color=#ff5470>No boombox nearby.</color> Deploy a boombox and stand next to it.");
                return;
            }
            try
            {
                box.BoxController.CurrentRadioIp = station.Url;
                box.SetFlag(BaseEntity.Flags.On, true);
                box.SendNetworkUpdate();
                player.ChatMessage($"<color=#B026FF>[RADIO]</color> Now playing <color=#00e5ff>{station.Name}</color> on your boombox.");
            }
            catch (Exception ex)
            {
                player.ChatMessage("<color=#ff5470>Could not set the station.</color> Try the boombox radio dial instead.");
                PrintWarning($"Radio set failed: {ex.Message}");
            }
            OpenMenu(player); // refresh menu (shows now-playing)
        }

        private void StopNearest(BasePlayer player)
        {
            var box = FindNearest(player);
            if (box == null) { player.ChatMessage("No boombox nearby."); return; }
            try
            {
                box.SetFlag(BaseEntity.Flags.On, false);
                box.SendNetworkUpdate();
                player.ChatMessage("<color=#B026FF>[RADIO]</color> Stopped.");
            }
            catch (Exception ex) { PrintWarning($"Radio stop failed: {ex.Message}"); }
        }

        // ---- UI -------------------------------------------------------------
        private void OpenMenu(BasePlayer player)
        {
            var box = FindNearest(player);
            string nowPlaying = box != null && box.IsOn() && box.BoxController != null
                ? box.BoxController.CurrentRadioIp : null;

            var c = new CuiElementContainer();
            CuiHelper.DestroyUi(player, UiRoot);

            // landscape panel
            c.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.06 0.10 0.98", Material = "assets/content/ui/uibackgroundblur.mat", FadeIn = 0.4f },
                RectTransform = { AnchorMin = "0.27 0.30", AnchorMax = "0.73 0.72" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            // accent top bar
            c.Add(new CuiPanel { Image = { Color = "0.69 0.15 1 1", FadeIn = 0.5f },
                RectTransform = { AnchorMin = "0 0.955", AnchorMax = "1 0.965" } }, UiRoot);

            Label(c, "RIFT RADIO", 15, "0.03 0.88", "0.55 0.96", TextAnchor.MiddleLeft, "0.69 0.15 1 1");
            Label(c,
                box == null ? "<color=#ffb020>Stand next to a boombox</color>" : "Select a station",
                10, "0.4 0.88", "0.9 0.96", TextAnchor.MiddleRight, "0.7 0.74 0.85 1");
            c.Add(new CuiButton
            {
                Button = { Command = "riftradio.close", Color = "0.8 0.2 0.3 0.9" },
                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.93 0.88", AnchorMax = "0.98 0.96" }
            }, UiRoot);

            // station grid — 3 columns (landscape)
            float[] cx0 = { 0.04f, 0.355f, 0.675f };
            float[] cx1 = { 0.325f, 0.645f, 0.96f };
            float top = 0.80f, h = 0.107f;
            for (int i = 0; i < config.Stations.Count; i++)
            {
                var s = config.Stations[i];
                int col = i % 3, row = i / 3;
                float y1 = top - row * h, y0 = y1 - h + 0.016f;
                bool playing = nowPlaying != null && nowPlaying == s.Url;
                c.Add(new CuiButton
                {
                    Button = { Command = $"riftradio.play {i}", Color = playing ? "0.17 0.75 0.38 0.95" : "1 1 1 0.06", FadeIn = 0.3f },
                    Text = { Text = (playing ? "> " : "") + s.Name, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.92 0.94 1 1" },
                    RectTransform = { AnchorMin = $"{cx0[col]} {y0}", AnchorMax = $"{cx1[col]} {y1}" }
                }, UiRoot);
            }

            // stop button + hint
            c.Add(new CuiButton
            {
                Button = { Command = "riftradio.stop", Color = "0.8 0.25 0.32 0.95" },
                Text = { Text = "STOP", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.04 0.03", AnchorMax = "0.3 0.1" }
            }, UiRoot);
            Label(c, "Plays on the nearest boombox — nearby players hear it (~30m)", 9,
                "0.33 0.03", "0.96 0.1", TextAnchor.MiddleRight, "0.55 0.6 0.72 1");

            CuiHelper.AddUi(player, c);
            openMenus.Add(player.userID);
        }

        private void Label(CuiElementContainer c, string text, int size, string min, string max, TextAnchor align, string color)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, UiRoot);
        }
    }
}
