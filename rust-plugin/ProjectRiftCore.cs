// ============================================================================
//  Project Rift Core  —  Carbon / Oxide plugin for Rust
//  Author: ESYSTEMLK   |   https://projectrift.esystemlk.com
//
//  Features
//    • Live heartbeat to the website API (player count / max / hostname)
//    • Native Rust loading-screen header image + website URL
//    • Modern glassmorphism in-game UI:
//        - Always-on HUD  (live online count + wipe countdown)
//        - Cinematic welcome screen on connect
//        - /info panel (rules, commands, links)
//    • Chat commands: /info /discord /website /loading  (+ admin /rift)
//
//  Drop in:  carbon/plugins/   (config auto-generates in carbon/configs/)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("ProjectRiftCore", "ESYSTEMLK", "1.4.0")]
    [Description("Live heartbeat + modern in-game UI (HUD, welcome, info, death screen) for Project Rift.")]
    public class ProjectRiftCore : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;

        #region Configuration
        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Website API URL (heartbeat is POSTed here)")]
            public string ApiUrl = "https://projectrift.esystemlk.com/api/server";

            [JsonProperty("API Key (must match PROJECT_RIFT_API_KEY on the website)")]
            public string ApiKey = "change-me";

            [JsonProperty("Website URL")]
            public string WebsiteUrl = "https://projectrift.esystemlk.com";

            [JsonProperty("Discord invite URL")]
            public string DiscordUrl = "https://discord.gg/yourinvite";

            [JsonProperty("Loading-screen header image URL (512x256, shown by Rust)")]
            public string HeaderImageUrl = "https://projectrift.esystemlk.com/serverheader.png";

            [JsonProperty("Welcome screen background image URL")]
            public string WelcomeImageUrl = "https://projectrift.esystemlk.com/bgmobile.png";

            [JsonProperty("Welcome screen logo image URL (square)")]
            public string LogoUrl = "https://projectrift.esystemlk.com/serverlogo.png";

            [JsonProperty("Next wipe date (UTC ISO 8601, blank = auto next Thursday 18:00 UTC)")]
            public string WipeDateUtc = "";

            [JsonProperty("Heartbeat interval (seconds)")]
            public float HeartbeatInterval = 30f;

            [JsonProperty("Show always-on HUD")]
            public bool HudEnabled = true;

            [JsonProperty("Show custom death screen")]
            public bool DeathScreenEnabled = true;

            [JsonProperty("Show welcome screen on connect")]
            public bool ShowWelcomeScreen = true;

            [JsonProperty("ENTER THE RIFT loading animation length (seconds)")]
            public float EnterLoadingSeconds = 2.6f;

            [JsonProperty("Spinning logo frames base URL (frame0.png .. frameN.png) — needs ImageLibrary")]
            public string SpinFrameBaseUrl = "https://projectrift.esystemlk.com/spin/frame";

            [JsonProperty("Spinning logo frame count")]
            public int SpinFrameCount = 24;

            [JsonProperty("Loading tips (one is shown at random)")]
            public string[] Tips =
            {
                "Protect your Tool Cupboard — it controls your whole base.",
                "Raid weekends are dangerous. Upgrade to stone before Friday.",
                "Join the Discord for free starter kits and giveaways.",
                "Recyclers turn junk into scrap. Recycle before you log off.",
                "Teamwork wins raids. Find squads in the Discord #lfg channel."
            };

            [JsonProperty("Server rules (shown in /info)")]
            public string[] Rules =
            {
                "Max team size: 4 players",
                "No cheating, scripting or exploiting",
                "No racism, slurs or harassment",
                "Group limits are strictly enforced"
            };
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception("null config");
            }
            catch
            {
                PrintWarning("Configuration invalid — generating a fresh default.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);
        #endregion

        #region UI palette / names
        private const string HudName = "ProjectRift.HUD";
        private const string WelcomeName = "ProjectRift.Welcome";
        private const string InfoName = "ProjectRift.Info";
        private const string DeathName = "ProjectRift.Death";
        private const string EnterName = "ProjectRift.Enter";

        // tracks when each player's current life began (for "time survived")
        private readonly Dictionary<ulong, float> aliveSince = new Dictionary<ulong, float>();

        // glassmorphism palette (Rust CUI uses "r g b a", 0-1)
        private const string Glass = "0.06 0.05 0.10 0.82";   // frosted panel
        private const string GlassDark = "0.02 0.03 0.05 0.94"; // full-screen backdrop
        private const string Card = "0.07 0.06 0.12 0.92";
        private const string Purple = "0.690 0.149 1 1";
        private const string Cyan = "0 0.898 1 1";
        private const string White = "0.96 0.97 1 1";
        private const string Muted = "0.62 0.65 0.78 1";
        private const string Line = "1 1 1 0.06";
        private const string Blur = "assets/content/ui/uibackgroundblur.mat";
        private const string FontBold = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";
        #endregion

        #region Lifecycle
        private void OnServerInitialized()
        {
            Puts("Project Rift Core v1.1 loaded.");

            if (!string.IsNullOrEmpty(config.HeaderImageUrl))
                ConVar.Server.headerimage = config.HeaderImageUrl;
            if (!string.IsNullOrEmpty(config.WebsiteUrl))
                ConVar.Server.url = config.WebsiteUrl;

            RegisterSpinFrames();

            SendHeartbeat();
            timer.Every(config.HeartbeatInterval, SendHeartbeat);

            // Refresh the HUD (live count + countdown) every minute.
            timer.Every(60f, RefreshHudAll);

            // (re)draw HUD for everyone already online (e.g. on hot-reload).
            foreach (var p in BasePlayer.activePlayerList) BuildHud(p);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, HudName);
                CuiHelper.DestroyUi(p, WelcomeName);
                CuiHelper.DestroyUi(p, EnterName);
                CuiHelper.DestroyUi(p, InfoName);
                CuiHelper.DestroyUi(p, DeathName);
            }
            deathSessions.Clear();
        }
        #endregion

        #region Heartbeat -> website
        private void SendHeartbeat()
        {
            if (string.IsNullOrEmpty(config.ApiUrl)) return;

            var payload = new Dictionary<string, object>
            {
                ["players"] = BasePlayer.activePlayerList.Count,
                ["maxPlayers"] = ConVar.Server.maxplayers,
                ["hostname"] = ConVar.Server.hostname
            };

            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["x-api-key"] = config.ApiKey
            };

            webrequest.Enqueue(config.ApiUrl, JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"Heartbeat failed (HTTP {code}). Check ApiUrl / ApiKey.");
                }, this, RequestMethod.POST, headers, 8f);
        }
        #endregion

        #region Wipe helper
        private DateTime GetNextWipe()
        {
            if (!string.IsNullOrEmpty(config.WipeDateUtc) &&
                DateTime.TryParse(config.WipeDateUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                return dt;

            var now = DateTime.UtcNow;
            var d = new DateTime(now.Year, now.Month, now.Day, 18, 0, 0, DateTimeKind.Utc);
            int add = ((int)DayOfWeek.Thursday - (int)d.DayOfWeek + 7) % 7;
            if (add == 0 && now > d) add = 7;
            return d.AddDays(add);
        }

        private string WipeText()
        {
            var span = GetNextWipe() - DateTime.UtcNow;
            if (span.TotalSeconds <= 0) return "WIPING SOON";
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
            if (span.TotalHours >= 1) return $"{span.Hours}h {span.Minutes}m";
            return $"{span.Minutes}m";
        }
        #endregion

        #region HUD  (always-on, glassmorphism)
        private void RefreshHudAll()
        {
            if (!config.HudEnabled) return;
            foreach (var p in BasePlayer.activePlayerList) BuildHud(p);
        }

        private void BuildHud(BasePlayer player)
        {
            if (!config.HudEnabled || player == null || !player.IsConnected) return;
            CuiHelper.DestroyUi(player, HudName);

            int players = BasePlayer.activePlayerList.Count;
            int max = ConVar.Server.maxplayers;
            var c = new CuiElementContainer();

            // frosted-glass card, top-right
            string card = c.Add(new CuiPanel
            {
                Image = { Color = Glass, Material = Blur },
                RectTransform = { AnchorMin = "0.838 0.9", AnchorMax = "0.995 0.988" }
            }, "Hud", HudName);

            // left accent bar
            c.Add(new CuiPanel
            {
                Image = { Color = Purple },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.014 1" }
            }, card);

            // thin top hairline
            c.Add(new CuiPanel
            {
                Image = { Color = Line },
                RectTransform = { AnchorMin = "0.014 0.985", AnchorMax = "1 1" }
            }, card);

            // brand
            c.Add(new CuiLabel
            {
                Text = { Text = "PROJECT <color=#00e5ff>RIFT</color>", FontSize = 14, Font = FontBold, Align = TextAnchor.MiddleLeft, Color = White },
                RectTransform = { AnchorMin = "0.07 0.58", AnchorMax = "0.98 0.96" }
            }, card);

            // online
            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#2bff88>●</color>  {players}<color=#9aa3c4>/{max}</color>  ONLINE", FontSize = 12, Font = FontReg, Align = TextAnchor.MiddleLeft, Color = "0.82 0.85 0.94 1" },
                RectTransform = { AnchorMin = "0.07 0.31", AnchorMax = "0.98 0.58" }
            }, card);

            // wipe countdown
            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#9aa3c4>NEXT WIPE</color>  <color=#b026ff>{WipeText()}</color>", FontSize = 11, Font = FontReg, Align = TextAnchor.MiddleLeft, Color = Muted },
                RectTransform = { AnchorMin = "0.07 0.04", AnchorMax = "0.98 0.31" }
            }, card);

            CuiHelper.AddUi(player, c);
        }
        #endregion

        #region Welcome screen (cinematic)
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            timer.Once(2f, () =>
            {
                if (player == null || !player.IsConnected) return;
                if (config.ShowWelcomeScreen) ShowWelcome(player);
                BuildHud(player);
            });
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null) return;
            if (!aliveSince.ContainsKey(player.userID))
                aliveSince[player.userID] = Time.realtimeSinceStartup;
            BuildHud(player);
        }

        private void ShowWelcome(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, WelcomeName);
            string tip = config.Tips.Length > 0 ? config.Tips[UnityEngine.Random.Range(0, config.Tips.Length)] : "";
            int players = BasePlayer.activePlayerList.Count;
            int max = ConVar.Server.maxplayers;

            var c = new CuiElementContainer();

            string root = c.Add(new CuiPanel
            {
                Image = { Color = GlassDark },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", WelcomeName);

            // faint background art
            if (!string.IsNullOrEmpty(config.WelcomeImageUrl))
                c.Add(new CuiElement
                {
                    Parent = root,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.WelcomeImageUrl, Color = "1 1 1 0.28" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });

            // vignette
            c.Add(new CuiPanel
            {
                Image = { Color = "0.02 0.03 0.05 0.55" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, root);

            // round logo (fixed square via offsets)
            if (!string.IsNullOrEmpty(config.LogoUrl))
                c.Add(new CuiElement
                {
                    Parent = root,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.LogoUrl, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.68", AnchorMax = "0.5 0.68", OffsetMin = "-66 -66", OffsetMax = "66 66" }
                    }
                });

            c.Add(new CuiLabel
            {
                Text = { Text = "PROJECT <color=#b026ff>RIFT</color>", FontSize = 52, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.6" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = "S U R V I V E    ·    B U I L D    ·    R A I D    ·    D O M I N A T E", FontSize = 15, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Cyan },
                RectTransform = { AnchorMin = "0 0.45", AnchorMax = "1 0.5" }
            }, root);

            // accent divider
            c.Add(new CuiPanel
            {
                Image = { Color = "0.69 0.149 1 0.6" },
                RectTransform = { AnchorMin = "0.43 0.435", AnchorMax = "0.57 0.438" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#2bff88>●</color>  {players} / {max} PLAYERS ONLINE", FontSize = 18, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = "0.85 0.88 0.96 1" },
                RectTransform = { AnchorMin = "0 0.37", AnchorMax = "1 0.42" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#b026ff>TIP</color>   {tip}", FontSize = 15, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Muted },
                RectTransform = { AnchorMin = "0.15 0.31", AnchorMax = "0.85 0.36" }
            }, root);

            // ENTER THE RIFT button (glass + accent) — stays until clicked
            c.Add(new CuiButton
            {
                Button = { Color = Purple, Command = "projectrift.enter" },
                Text = { Text = "ENTER THE RIFT", FontSize = 16, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0.4 0.2", AnchorMax = "0.6 0.258" }
            }, root);

            // info button
            c.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0.08", Command = "projectrift.info" },
                Text = { Text = "SERVER INFO", FontSize = 13, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Cyan },
                RectTransform = { AnchorMin = "0.4 0.14", AnchorMax = "0.6 0.185" }
            }, root);

            // NOTE: no auto-close — the screen stays until the player clicks ENTER.
            CuiHelper.AddUi(player, c);
        }

        // ---- spinning logo frames (ImageLibrary) ----
        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "ImageLibrary")
            {
                ImageLibrary = plugin;
                RegisterSpinFrames();
            }
        }

        private void RegisterSpinFrames()
        {
            if (ImageLibrary == null || config.SpinFrameCount <= 0 || string.IsNullOrEmpty(config.SpinFrameBaseUrl)) return;
            for (int i = 0; i < config.SpinFrameCount; i++)
                ImageLibrary.Call("AddImage", $"{config.SpinFrameBaseUrl}{i}.png", $"rift_spin_{i}", 0UL);
            Puts($"Queued {config.SpinFrameCount} spinning-logo frames with ImageLibrary.");
        }

        private bool HasSpinFrames()
        {
            if (ImageLibrary == null || config.SpinFrameCount <= 0) return false;
            var has = ImageLibrary.Call("HasImage", "rift_spin_0", 0UL);
            return has is bool b && b;
        }

        // ---- ENTER THE RIFT loading sequence ----
        private void ShowEnterLoading(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, WelcomeName);
            CuiHelper.DestroyUi(player, EnterName);
            bool spin = HasSpinFrames();

            var c = new CuiElementContainer();
            string root = c.Add(new CuiPanel
            {
                Image = { Color = "0.02 0.02 0.04 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", EnterName);

            if (!string.IsNullOrEmpty(config.WelcomeImageUrl))
                c.Add(new CuiElement
                {
                    Parent = root,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.WelcomeImageUrl, Color = "1 1 1 0.18" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });

            // static logo only when we DON'T have real spin frames
            // (with frames, AnimateEnter draws the rotating logo each tick)
            if (!spin && !string.IsNullOrEmpty(config.LogoUrl))
                c.Add(new CuiElement
                {
                    Parent = root,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.LogoUrl, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.56", AnchorMax = "0.5 0.56", OffsetMin = "-52 -52", OffsetMax = "52 52" }
                    }
                });

            c.Add(new CuiLabel
            {
                Text = { Text = "E N T E R I N G   T H E   R I F T", FontSize = 17, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0 0.4", AnchorMax = "1 0.45" }
            }, root);

            // progress track (fill is animated)
            c.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.08" },
                RectTransform = { AnchorMin = "0.35 0.36", AnchorMax = "0.65 0.372" }
            }, root);

            CuiHelper.AddUi(player, c);
            AnimateEnter(player, Time.realtimeSinceStartup, spin);
        }

        private void AnimateEnter(BasePlayer player, float start, bool spin)
        {
            if (player == null || !player.IsConnected) return;

            float duration = config.EnterLoadingSeconds <= 0f ? 2.6f : config.EnterLoadingSeconds;
            float elapsed = Time.realtimeSinceStartup - start;
            float t = Mathf.Clamp01(elapsed / duration);

            CuiHelper.DestroyUi(player, EnterName + ".ring");
            CuiHelper.DestroyUi(player, EnterName + ".logo");
            CuiHelper.DestroyUi(player, EnterName + ".fill");
            var c = new CuiElementContainer();

            if (spin)
            {
                // REAL spinning logo — cycle pre-rendered rotation frames
                int idx = (int)(elapsed / 0.05f) % config.SpinFrameCount;
                var png = ImageLibrary?.Call("GetImage", $"rift_spin_{idx}", 0UL) as string;
                if (!string.IsNullOrEmpty(png))
                    c.Add(new CuiElement
                    {
                        Parent = EnterName,
                        Name = EnterName + ".logo",
                        Components =
                        {
                            new CuiRawImageComponent { Png = png },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.56", AnchorMax = "0.5 0.56", OffsetMin = "-58 -58", OffsetMax = "58 58" }
                        }
                    });
            }
            else
            {
                // fallback: spinner ring of 8 dots rotating around the logo
                string ring = c.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, EnterName, EnterName + ".ring");

                const int dots = 8;
                int active = (int)(elapsed / 0.09f) % dots;
                for (int i = 0; i < dots; i++)
                {
                    float ang = (Mathf.PI * 2f * i / dots) - Mathf.PI / 2f;
                    float r = 74f;
                    float ox = Mathf.Cos(ang) * r;
                    float oy = Mathf.Sin(ang) * r;
                    int dist = (active - i + dots) % dots;
                    float a = Mathf.Lerp(1f, 0.12f, dist / (float)dots);
                    string col = dist == 0 ? "0 0.898 1 1" : $"0.69 0.149 1 {a.ToString(CultureInfo.InvariantCulture)}";

                    c.Add(new CuiPanel
                    {
                        Image = { Color = col },
                        RectTransform =
                        {
                            AnchorMin = "0.5 0.56", AnchorMax = "0.5 0.56",
                            OffsetMin = $"{(ox - 5f).ToString(CultureInfo.InvariantCulture)} {(oy - 5f).ToString(CultureInfo.InvariantCulture)}",
                            OffsetMax = $"{(ox + 5f).ToString(CultureInfo.InvariantCulture)} {(oy + 5f).ToString(CultureInfo.InvariantCulture)}"
                        }
                    }, ring);
                }
            }

            // progress fill
            float fillMax = 0.35f + (0.65f - 0.35f) * t;
            c.Add(new CuiPanel
            {
                Image = { Color = "0.69 0.149 1 1" },
                RectTransform = { AnchorMin = "0.35 0.36", AnchorMax = $"{fillMax.ToString(CultureInfo.InvariantCulture)} 0.372" }
            }, EnterName, EnterName + ".fill");

            CuiHelper.AddUi(player, c);

            if (t >= 1f)
            {
                CuiHelper.DestroyUi(player, EnterName);
                return;
            }
            timer.Once(spin ? 0.05f : 0.09f, () => AnimateEnter(player, start, spin));
        }
        #endregion

        #region Death screen
        private class DeathSession
        {
            public string TopLabel, KillerName, Weapon = "—", Distance = "—", Hit = "—", Alive;
            public bool Pvp;
        }

        private readonly Dictionary<ulong, DeathSession> deathSessions = new Dictionary<ulong, DeathSession>();
        private const string BagsName = "ProjectRift.Death.Bags";

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            aliveSince[player.userID] = Time.realtimeSinceStartup;
            deathSessions.Remove(player.userID);
            CuiHelper.DestroyUi(player, DeathName);
            BuildHud(player);
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!config.DeathScreenEnabled || player == null || !player.userID.IsSteamId()) return;

            var attacker = info?.InitiatorPlayer;
            bool pvp = attacker != null && attacker != player && !attacker.IsNpc && attacker.userID.IsSteamId();
            var s = new DeathSession();

            if (pvp)
            {
                s.Pvp = true;
                s.TopLabel = "ELIMINATED BY";
                s.KillerName = attacker.displayName;
                s.Weapon = WeaponName(info);
                s.Distance = Mathf.RoundToInt(Vector3.Distance(attacker.transform.position, player.transform.position)) + "m";
                s.Hit = (info != null && info.isHeadshot) ? "HEADSHOT" : "BODY";
            }
            else if (attacker != null && attacker.IsNpc)
            {
                s.TopLabel = "ELIMINATED BY";
                s.KillerName = string.IsNullOrEmpty(attacker.displayName) ? "Scientist" : attacker.displayName;
                s.Weapon = WeaponName(info);
                s.Distance = Mathf.RoundToInt(Vector3.Distance(attacker.transform.position, player.transform.position)) + "m";
            }
            else
            {
                s.TopLabel = "CAUSE OF DEATH";
                s.KillerName = CauseText(info);
            }

            s.Alive = aliveSince.TryGetValue(player.userID, out var t)
                ? FormatAlive(Time.realtimeSinceStartup - t) : null;

            deathSessions[player.userID] = s;
            ShowDeathScreen(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            aliveSince.Remove(player.userID);
            deathSessions.Remove(player.userID);
        }

        private string WeaponName(HitInfo info)
        {
            var item = info?.Weapon?.GetItem();
            if (item?.info?.displayName != null) return item.info.displayName.english;
            var prefab = info?.WeaponPrefab?.ShortPrefabName;
            return string.IsNullOrEmpty(prefab) ? "Unknown" : prefab;
        }

        private string CauseText(HitInfo info)
        {
            var dt = info?.damageTypes != null ? info.damageTypes.GetMajorityDamageType() : Rust.DamageType.Generic;
            switch (dt)
            {
                case Rust.DamageType.Fall: return "Fell to death";
                case Rust.DamageType.Cold: return "Froze to death";
                case Rust.DamageType.Heat: return "Burned to death";
                case Rust.DamageType.Bleeding: return "Bled out";
                case Rust.DamageType.Hunger: return "Starved";
                case Rust.DamageType.Thirst: return "Dehydrated";
                case Rust.DamageType.Drowned: return "Drowned";
                case Rust.DamageType.Poison: return "Poisoned";
                case Rust.DamageType.Radiation: return "Radiation poisoning";
                case Rust.DamageType.Suicide: return "Took the easy way out";
                case Rust.DamageType.Bite:
                case Rust.DamageType.Slash:
                case Rust.DamageType.Bullet: return "Killed by wildlife";
                default: return "Eliminated";
            }
        }

        private string FormatAlive(float seconds)
        {
            int s = Mathf.Max(0, (int)seconds);
            int h = s / 3600; s %= 3600;
            int m = s / 60; s %= 60;
            if (h > 0) return $"{h}h {m}m";
            if (m > 0) return $"{m}m {s}s";
            return $"{s}s";
        }

        private void ShowDeathScreen(BasePlayer victim)
        {
            if (victim == null || !deathSessions.TryGetValue(victim.userID, out var s)) return;
            CuiHelper.DestroyUi(victim, DeathName);

            bool pvp = s.Pvp;
            string topLabel = s.TopLabel, killerName = s.KillerName, weapon = s.Weapon, distance = s.Distance, hit = s.Hit, alive = s.Alive;
            string initial = string.IsNullOrEmpty(killerName) ? "?" : killerName.Substring(0, 1).ToUpper();

            var c = new CuiElementContainer();

            string root = c.Add(new CuiPanel
            {
                Image = { Color = "0.03 0.02 0.05 0.96" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", DeathName);

            // red glow at the top
            c.Add(new CuiPanel
            {
                Image = { Color = "0.55 0.05 0.14 0.28" },
                RectTransform = { AnchorMin = "0 0.8", AnchorMax = "1 1" }
            }, root);

            // YOU DIED
            c.Add(new CuiLabel
            {
                Text = { Text = "YOU <color=#ff4060>DIED</color>", FontSize = 60, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0 0.8", AnchorMax = "1 0.91" }
            }, root);
            c.Add(new CuiLabel
            {
                Text = { Text = "E L I M I N A T E D   O N   P R O J E C T   R I F T", FontSize = 12, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Cyan },
                RectTransform = { AnchorMin = "0 0.765", AnchorMax = "1 0.8" }
            }, root);

            // kill card
            string card = c.Add(new CuiPanel
            {
                Image = { Color = Card, Material = Blur },
                RectTransform = { AnchorMin = "0.355 0.51", AnchorMax = "0.645 0.75" }
            }, root);

            // killer initial badge
            string badge = c.Add(new CuiPanel
            {
                Image = { Color = pvp ? "0.69 0.149 1 1" : "0.55 0.08 0.16 1" },
                RectTransform = { AnchorMin = "0.43 0.74", AnchorMax = "0.57 0.95" }
            }, card);
            c.Add(new CuiLabel
            {
                Text = { Text = initial, FontSize = 22, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, badge);

            c.Add(new CuiLabel
            {
                Text = { Text = topLabel, FontSize = 11, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Muted },
                RectTransform = { AnchorMin = "0 0.64", AnchorMax = "1 0.72" }
            }, card);
            c.Add(new CuiLabel
            {
                Text = { Text = killerName, FontSize = 22, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0.04 0.5", AnchorMax = "0.96 0.64" }
            }, card);

            // divider
            c.Add(new CuiPanel
            {
                Image = { Color = "0.69 0.149 1 0.5" },
                RectTransform = { AnchorMin = "0.18 0.47", AnchorMax = "0.82 0.475" }
            }, card);

            // stat cells
            AddDeathStat(c, card, 0.04f, 0.345f, "WEAPON", weapon);
            AddDeathStat(c, card, 0.355f, 0.645f, "DISTANCE", distance);
            AddDeathStat(c, card, 0.655f, 0.96f, "HIT", hit, hit == "HEADSHOT" ? "#ff4060" : null);

            // time survived
            if (alive != null)
                c.Add(new CuiLabel
                {
                    Text = { Text = $"<color=#9aa3c4>YOU SURVIVED</color>  {alive}", FontSize = 13, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = "0.82 0.85 0.94 1" },
                    RectTransform = { AnchorMin = "0 0.475", AnchorMax = "1 0.508" }
                }, root);

            // respawn-points heading (the bag list is refreshed live below)
            c.Add(new CuiLabel
            {
                Text = { Text = "RESPAWN AT", FontSize = 11, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Cyan },
                RectTransform = { AnchorMin = "0 0.435", AnchorMax = "1 0.468" }
            }, root);

            // random respawn
            c.Add(new CuiButton
            {
                Button = { Color = "1 1 1 0.1", Command = "respawn" },
                Text = { Text = "RANDOM RESPAWN", FontSize = 14, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = Cyan },
                RectTransform = { AnchorMin = "0.39 0.1", AnchorMax = "0.61 0.155" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = "Killed unfairly?  Report it in <color=#00e5ff>/discord</color>", FontSize = 12, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Muted },
                RectTransform = { AnchorMin = "0 0.055", AnchorMax = "1 0.095" }
            }, root);

            CuiHelper.AddUi(victim, c);

            // live bag list — rebuilt every second so cooldowns tick down for real
            RefreshBags(victim);
        }

        private void RefreshBags(BasePlayer victim)
        {
            if (victim == null || !deathSessions.TryGetValue(victim.userID, out var session)) return;

            // stop + clean up once the player is alive again
            if (!victim.IsConnected || !victim.IsDead())
            {
                deathSessions.Remove(victim.userID);
                CuiHelper.DestroyUi(victim, DeathName);
                return;
            }

            CuiHelper.DestroyUi(victim, BagsName);
            var c = new CuiElementContainer();

            string box = c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.33 0.17", AnchorMax = "0.67 0.43" }
            }, DeathName, BagsName);

            var bags = new List<SleepingBag>();
            foreach (var b in SleepingBag.sleepingBags)
                if (b != null && b.deployerUserID == victim.userID) bags.Add(b);

            if (bags.Count == 0)
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = "No sleeping bags or beds placed — random respawn only", FontSize = 12, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Muted },
                    RectTransform = { AnchorMin = "0 0.55", AnchorMax = "1 1" }
                }, box);
            }
            else
            {
                int max = Mathf.Min(bags.Count, 5);
                float rowH = 1f / max;
                for (int i = 0; i < max; i++)
                {
                    var bag = bags[i];
                    float remain = bag.unlockTime - Time.realtimeSinceStartup;
                    bool ready = remain <= 0f;
                    bool isBed = bag.ShortPrefabName != null && bag.ShortPrefabName.Contains("bed");
                    string nm = string.IsNullOrEmpty(bag.niceName)
                        ? (isBed ? "Bed" : "Sleeping Bag") : bag.niceName;

                    float yMax = 1f - i * rowH;
                    float yMin = yMax - rowH * 0.86f;
                    string min = $"0 {yMin.ToString(CultureInfo.InvariantCulture)}";
                    string maxa = $"1 {yMax.ToString(CultureInfo.InvariantCulture)}";

                    bool canClick = ready && bag.net != null;
                    string row = c.Add(new CuiButton
                    {
                        Button = { Color = ready ? "0.69 0.149 1 0.85" : "1 1 1 0.05", Command = canClick ? $"respawn_sleepingbag {bag.net.ID.Value}" : "" },
                        Text = { Text = "" },
                        RectTransform = { AnchorMin = min, AnchorMax = maxa }
                    }, box);

                    c.Add(new CuiLabel
                    {
                        Text = { Text = $"<color=#9aa3c4>{(isBed ? "BED" : "BAG")}</color>  {nm}", FontSize = 12, Font = FontReg, Align = TextAnchor.MiddleLeft, Color = White },
                        RectTransform = { AnchorMin = "0.06 0", AnchorMax = "0.72 1" }
                    }, row);

                    c.Add(new CuiLabel
                    {
                        Text = { Text = ready ? "RESPAWN" : FormatCooldown(remain), FontSize = 11, Font = FontBold, Align = TextAnchor.MiddleRight, Color = ready ? White : Muted },
                        RectTransform = { AnchorMin = "0.3 0", AnchorMax = "0.94 1" }
                    }, row);
                }
            }

            CuiHelper.AddUi(victim, c);

            // re-run while the player is still on the death screen
            timer.Once(1f, () =>
            {
                if (victim != null && deathSessions.TryGetValue(victim.userID, out var cur) && cur == session)
                    RefreshBags(victim);
            });
        }

        private string FormatCooldown(float remain)
        {
            int s = Mathf.CeilToInt(remain);
            int m = s / 60; s %= 60;
            return $"{m}:{s:00}";
        }

        private void AddDeathStat(CuiElementContainer c, string parent, float xMin, float xMax, string label, string value, string valueColor = null)
        {
            string cell = c.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.04" },
                RectTransform = { AnchorMin = $"{xMin} 0.08", AnchorMax = $"{xMax} 0.42" }
            }, parent);
            c.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 10, Font = FontReg, Align = TextAnchor.UpperCenter, Color = Muted },
                RectTransform = { AnchorMin = "0 0.52", AnchorMax = "1 0.92" }
            }, cell);
            c.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 14, Font = FontBold, Align = TextAnchor.LowerCenter, Color = valueColor != null ? "1 0.25 0.38 1" : White },
                RectTransform = { AnchorMin = "0 0.08", AnchorMax = "1 0.55" }
            }, cell);
        }
        #endregion

        #region Info panel
        private void ShowInfo(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, InfoName);
            var c = new CuiElementContainer();

            // click-outside-to-close backdrop
            string backdrop = c.Add(new CuiButton
            {
                Button = { Color = "0.01 0.01 0.02 0.75", Command = "projectrift.closeinfo", Close = InfoName },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "Overlay", InfoName);

            // card
            string card = c.Add(new CuiPanel
            {
                Image = { Color = Card, Material = Blur },
                RectTransform = { AnchorMin = "0.345 0.18", AnchorMax = "0.655 0.82" },
                CursorEnabled = true
            }, backdrop);

            // header bar
            c.Add(new CuiPanel
            {
                Image = { Color = "0.69 0.149 1 1" },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" }
            }, card);
            c.Add(new CuiLabel
            {
                Text = { Text = "PROJECT <color=#001018>RIFT</color>  —  SERVER INFO", FontSize = 16, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" }
            }, card);

            // quick stats row
            AddInfoStat(c, card, 0.04f, 0.36f, "STATUS", "<color=#2bff88>ONLINE</color>");
            AddInfoStat(c, card, 0.36f, 0.68f, "TEAM LIMIT", "4");
            AddInfoStat(c, card, 0.68f, 0.96f, "NEXT WIPE", WipeText());

            // rules
            c.Add(new CuiLabel
            {
                Text = { Text = "RULES", FontSize = 13, Font = FontBold, Align = TextAnchor.UpperLeft, Color = Cyan },
                RectTransform = { AnchorMin = "0.06 0.66", AnchorMax = "0.94 0.7" }
            }, card);
            string rules = "";
            foreach (var r in config.Rules) rules += $"<color=#b026ff>›</color>  {r}\n";
            c.Add(new CuiLabel
            {
                Text = { Text = rules, FontSize = 13, Font = FontReg, Align = TextAnchor.UpperLeft, Color = "0.82 0.85 0.94 1" },
                RectTransform = { AnchorMin = "0.06 0.46", AnchorMax = "0.94 0.66" }
            }, card);

            // commands
            c.Add(new CuiLabel
            {
                Text = { Text = "COMMANDS", FontSize = 13, Font = FontBold, Align = TextAnchor.UpperLeft, Color = Cyan },
                RectTransform = { AnchorMin = "0.06 0.4", AnchorMax = "0.94 0.44" }
            }, card);
            c.Add(new CuiLabel
            {
                Text = { Text = "<color=#b026ff>/info</color>  server info     <color=#b026ff>/discord</color>  community\n<color=#b026ff>/website</color>  our site     <color=#b026ff>/loading</color>  welcome", FontSize = 13, Font = FontReg, Align = TextAnchor.UpperLeft, Color = "0.82 0.85 0.94 1" },
                RectTransform = { AnchorMin = "0.06 0.28", AnchorMax = "0.94 0.4" }
            }, card);

            // discord highlight
            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#9aa3c4>DISCORD</color>   {config.DiscordUrl}", FontSize = 13, Font = FontReg, Align = TextAnchor.MiddleCenter, Color = Cyan },
                RectTransform = { AnchorMin = "0.06 0.18", AnchorMax = "0.94 0.24" }
            }, card);

            // close button
            c.Add(new CuiButton
            {
                Button = { Color = Purple, Command = "projectrift.closeinfo", Close = InfoName },
                Text = { Text = "CLOSE", FontSize = 14, Font = FontBold, Align = TextAnchor.MiddleCenter, Color = White },
                RectTransform = { AnchorMin = "0.38 0.05", AnchorMax = "0.62 0.12" }
            }, card);

            CuiHelper.AddUi(player, c);
        }

        private void AddInfoStat(CuiElementContainer c, string parent, float xMin, float xMax, string label, string value)
        {
            string cell = c.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.04" },
                RectTransform = { AnchorMin = $"{xMin + 0.01f} 0.72", AnchorMax = $"{xMax - 0.01f} 0.9" }
            }, parent);
            c.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 10, Font = FontReg, Align = TextAnchor.UpperCenter, Color = Muted },
                RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.92" }
            }, cell);
            c.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 17, Font = FontBold, Align = TextAnchor.LowerCenter, Color = White },
                RectTransform = { AnchorMin = "0 0.08", AnchorMax = "1 0.55" }
            }, cell);
        }
        #endregion

        #region Console + chat commands
        [ConsoleCommand("projectrift.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p != null) CuiHelper.DestroyUi(p, WelcomeName);
        }

        [ConsoleCommand("projectrift.enter")]
        private void CcEnter(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p != null) ShowEnterLoading(p);
        }

        [ConsoleCommand("projectrift.info")]
        private void CcInfo(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p != null) { CuiHelper.DestroyUi(p, WelcomeName); ShowInfo(p); }
        }

        [ConsoleCommand("projectrift.closeinfo")]
        private void CcCloseInfo(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p != null) CuiHelper.DestroyUi(p, InfoName);
        }

        [ChatCommand("info")]
        private void CmdInfo(BasePlayer player) => ShowInfo(player);

        [ChatCommand("discord")]
        private void CmdDiscord(BasePlayer player) =>
            player.ChatMessage($"<color=#00e5ff>Join our Discord:</color> {config.DiscordUrl}");

        [ChatCommand("website")]
        private void CmdWebsite(BasePlayer player) =>
            player.ChatMessage($"<color=#00e5ff>Project Rift:</color> {config.WebsiteUrl}");

        [ChatCommand("loading")]
        private void CmdLoading(BasePlayer player) => ShowWelcome(player);

        [ChatCommand("rift")]
        private void CmdRift(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) { player.ChatMessage("<color=#ff5470>No permission.</color>"); return; }
            SendHeartbeat();
            RefreshHudAll();
            player.ChatMessage("<color=#2bff88>Project Rift:</color> HUD refreshed + heartbeat sent.");
        }
        #endregion
    }
}
