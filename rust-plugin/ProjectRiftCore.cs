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
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("ProjectRiftCore", "ESYSTEMLK", "1.9.0")]
    [Description("Live heartbeat + modern in-game UI (HUD, radiation warning, notifications, welcome, info, death screen) for Project Rift.")]
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

            [JsonProperty("Welcome screen logo image URL (use the ROUND logo)")]
            public string LogoUrl = "https://projectrift.esystemlk.com/icons/icon-512.png";

            [JsonProperty("Next wipe date (UTC ISO 8601, blank = auto next Thursday 18:00 UTC)")]
            public string WipeDateUtc = "";

            [JsonProperty("Heartbeat interval (seconds)")]
            public float HeartbeatInterval = 30f;

            [JsonProperty("Notifications API URL (polled for admin broadcasts)")]
            public string NotificationsApiUrl = "https://projectrift.esystemlk.com/api/notifications";

            [JsonProperty("Notification poll interval (seconds)")]
            public float NotificationPollSeconds = 5f;

            [JsonProperty("Show always-on HUD")]
            public bool HudEnabled = true;

            [JsonProperty("Show custom death screen")]
            public bool DeathScreenEnabled = true;

            [JsonProperty("Warn players approaching radiation zones")]
            public bool RadiationWarnEnabled = true;

            [JsonProperty("Radiation warning range (metres)")]
            public float RadiationWarnRange = 60f;

            [JsonProperty("Radiation check interval (seconds)")]
            public float RadiationCheckSeconds = 1f;

            [JsonProperty("Play a beep when approaching radiation")]
            public bool RadiationBeepEnabled = true;

            [JsonProperty("Radiation approach beep effect prefab")]
            public string RadiationBeepPrefab = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab";

            [JsonProperty("Radiation enter alarm effect prefab")]
            public string RadiationEnterPrefab = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";

            [JsonProperty("Custom vitals bars (HP/water/food) over the default bars")]
            public bool VitalsEnabled = true;

            // Anchored to the BOTTOM-RIGHT corner with pixel offsets so it lines
            // up on every resolution/aspect ratio (same as Rust's native bars).
            // Nudge these until the dark panel fully covers the default bars.
            [JsonProperty("Vitals panel offset min (px from bottom-right corner, e.g. \"-300 16\")")]
            public string VitalsOffsetMin = "-300 16";

            [JsonProperty("Vitals panel offset max (px from bottom-right corner, e.g. \"-8 176\")")]
            public string VitalsOffsetMax = "-8 176";

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
        private const string NotifyName = "ProjectRift.Notify";
        private const string RadName   = "ProjectRift.Rad";
        private const string StatsName = "ProjectRift.Stats";
        private const string StatsBarsName = "ProjectRift.Stats.b";

        // cached radiation trigger colliders + per-player last shown warning
        private readonly List<Collider> radZones = new List<Collider>();
        private readonly Dictionary<ulong, string> lastRadText = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, float> radNextBeep = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> radInside = new HashSet<ulong>();

        // tracks when each player's current life began (for "time survived")
        private readonly Dictionary<ulong, float> aliveSince = new Dictionary<ulong, float>();

        // playtime: persisted total seconds (by SteamID string) + current session start
        private Dictionary<string, double> playtime = new Dictionary<string, double>();
        private readonly Dictionary<ulong, float> sessionStart = new Dictionary<ulong, float>();

        // notification polling state
        private long lastNotifId = 0;
        private bool notifPrimed = false;
        private int notifSeq = 0;

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

        // Vitals stat-bar colours (green / blue / orange — matches Rust native HUD palette)
        private const string HealthGreen = "0.17 0.90 0.40 1";
        private const string HydroBlue   = "0.11 0.62 0.95 1";
        private const string FoodOrange  = "0.96 0.60 0.11 1";
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
            LoadPlaytime();

            SendHeartbeat();
            timer.Every(config.HeartbeatInterval, SendHeartbeat);

            // Poll the website for admin notifications.
            timer.Every(Mathf.Max(2f, config.NotificationPollSeconds), PollNotifications);

            // Radiation proximity warnings (cache zones after the world is loaded).
            if (config.RadiationWarnEnabled)
            {
                timer.Once(12f, CacheRadiationZones);
                timer.Every(Mathf.Max(0.5f, config.RadiationCheckSeconds), RadiationTick);
            }

            // Refresh the HUD (name + playtime) every minute.
            timer.Every(60f, RefreshHudAll);

            // Refresh ONLY the vitals bar fills every second (the opaque cover
            // persists, so the default bars never flash through).
            timer.Every(1f, () =>
            {
                if (!config.VitalsEnabled) return;
                foreach (var p in BasePlayer.activePlayerList)
                    if (p != null && p.IsConnected && !p.IsDead() && !p.IsSleeping())
                        RefreshStatsBars(p);
            });
            // (re)draw HUD + start session timers for everyone already online.
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (!sessionStart.ContainsKey(p.userID)) sessionStart[p.userID] = Time.realtimeSinceStartup;
                BuildHud(p);
            }
        }

        #region Playtime persistence
        private void LoadPlaytime()
        {
            try { playtime = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, double>>("ProjectRiftCore_Playtime") ?? new Dictionary<string, double>(); }
            catch { playtime = new Dictionary<string, double>(); }
        }

        private void SavePlaytime() => Interface.Oxide.DataFileSystem.WriteObject("ProjectRiftCore_Playtime", playtime);

        private void CommitSession(BasePlayer player)
        {
            if (player == null || !sessionStart.TryGetValue(player.userID, out var st)) return;
            double add = Time.realtimeSinceStartup - st;
            playtime.TryGetValue(player.UserIDString, out var cur);
            playtime[player.UserIDString] = cur + add;
            sessionStart.Remove(player.userID);
        }

        private string PlaytimeText(BasePlayer player)
        {
            playtime.TryGetValue(player.UserIDString, out var total);
            if (sessionStart.TryGetValue(player.userID, out var st))
                total += Time.realtimeSinceStartup - st;
            int s = (int)total, h = s / 3600, m = (s % 3600) / 60;
            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }
        #endregion

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                CommitSession(p);
                CuiHelper.DestroyUi(p, HudName);
                CuiHelper.DestroyUi(p, StatsName);
                CuiHelper.DestroyUi(p, WelcomeName);
                CuiHelper.DestroyUi(p, EnterName);
                CuiHelper.DestroyUi(p, InfoName);
                CuiHelper.DestroyUi(p, DeathName);
                CuiHelper.DestroyUi(p, NotifyName);
                CuiHelper.DestroyUi(p, RadName);
            }
            SavePlaytime();
            deathSessions.Clear();
            lastRadText.Clear();
            radNextBeep.Clear();
            radInside.Clear();
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
                RectTransform = { AnchorMin = "0.84 0.878", AnchorMax = "0.998 0.988" }
            }, "Hud", HudName);

            // left accent bar
            c.Add(new CuiPanel
            {
                Image = { Color = Purple },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.014 1" }
            }, card);

            // ---- server logo (no player avatar) ----
            string logoPng = ImageLibrary?.Call("GetImage", "rift_logo", 0UL) as string;
            var logoImg = new CuiRawImageComponent();
            if (!string.IsNullOrEmpty(logoPng)) logoImg.Png = logoPng;
            else logoImg.Url = config.LogoUrl;
            c.Add(new CuiElement
            {
                Parent = card,
                Components =
                {
                    logoImg,
                    new CuiRectTransformComponent { AnchorMin = "0.06 0.5", AnchorMax = "0.06 0.5", OffsetMin = "2 -22", OffsetMax = "44 22" }
                }
            });

            // player name
            string name = player.displayName ?? "Survivor";
            if (name.Length > 15) name = name.Substring(0, 15) + "…";
            c.Add(new CuiLabel
            {
                Text = { Text = name, FontSize = 14, Font = FontBold, Align = TextAnchor.LowerLeft, Color = White },
                RectTransform = { AnchorMin = "0.3 0.64", AnchorMax = "0.97 0.93" }
            }, card);

            // playtime
            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#9aa3c4>PLAYTIME</color>  <color=#00e5ff>{PlaytimeText(player)}</color>", FontSize = 11, Font = FontReg, Align = TextAnchor.MiddleLeft, Color = Muted },
                RectTransform = { AnchorMin = "0.3 0.38", AnchorMax = "0.97 0.62" }
            }, card);

            // online count
            c.Add(new CuiLabel
            {
                Text = { Text = $"<color=#2bff88>●</color>  {players}<color=#9aa3c4>/{max}</color>  ONLINE", FontSize = 12, Font = FontReg, Align = TextAnchor.MiddleLeft, Color = "0.82 0.85 0.94 1" },
                RectTransform = { AnchorMin = "0.3 0.08", AnchorMax = "0.97 0.36" }
            }, card);

            CuiHelper.AddUi(player, c);
            BuildStats(player);
        }
        #endregion

        #region Vitals HUD (health / hydration / food — live progress bars)
        // Builds the PERSISTENT opaque cover (hides the default bars 100%, never
        // flickers) then fills in the bars. Called on spawn / wake / periodically.
        private void BuildStats(BasePlayer player)
        {
            if (!config.VitalsEnabled || player == null || !player.IsConnected) return;
            CuiHelper.DestroyUi(player, StatsName);
            if (player.IsSleeping() || player.IsDead()) return;

            string urlBase = (config.WebsiteUrl ?? "").TrimEnd('/');
            var c = new CuiElementContainer();

            // ROUNDED opaque panel (tinted dark) on the Overlay layer, pinned to
            // the bottom-right corner so it covers the default bars on any monitor.
            string panelPng = ImageLibrary?.Call("GetImage", "rift_panel", 0UL) as string;
            var panel = new CuiRawImageComponent { Color = "0.04 0.03 0.07 1" };
            if (!string.IsNullOrEmpty(panelPng)) panel.Png = panelPng;
            else panel.Url = urlBase + "/ui/panel.png";
            c.Add(new CuiElement
            {
                Name = StatsName,
                Parent = "Overlay",
                Components =
                {
                    panel,
                    new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = config.VitalsOffsetMin, OffsetMax = config.VitalsOffsetMax }
                }
            });

            // static icons (heart / droplet / cutlery), tinted per stat colour
            AddStatIcon(c, StatsName, urlBase, 0.67f, 0.96f, "rift_hp",    "/ui/hp.png",    HealthGreen);
            AddStatIcon(c, StatsName, urlBase, 0.355f, 0.645f, "rift_water", "/ui/water.png", HydroBlue);
            AddStatIcon(c, StatsName, urlBase, 0.04f, 0.33f,  "rift_cal",   "/ui/cal.png",   FoodOrange);

            CuiHelper.AddUi(player, c);
            RefreshStatsBars(player);
        }

        private void AddStatIcon(CuiElementContainer c, string parent, string urlBase,
            float yMin, float yMax, string imgName, string urlPath, string color)
        {
            string png = ImageLibrary?.Call("GetImage", imgName, 0UL) as string;
            var img = new CuiRawImageComponent { Color = color };
            if (!string.IsNullOrEmpty(png)) img.Png = png;
            else img.Url = urlBase + urlPath;
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    img,
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"0.07 {(yMin + 0.02f).ToString(CultureInfo.InvariantCulture)}",
                        AnchorMax = $"0.19 {(yMax - 0.02f).ToString(CultureInfo.InvariantCulture)}"
                    }
                }
            });
        }

        // Updates ONLY the bar fills/values inside the persistent cover, so the
        // default bars are never revealed between refreshes (no flicker-through).
        private void RefreshStatsBars(BasePlayer player)
        {
            if (!config.VitalsEnabled || player == null || !player.IsConnected) return;
            CuiHelper.DestroyUi(player, StatsBarsName);
            if (player.IsSleeping() || player.IsDead()) return;

            float hp       = player.health;
            float hpMax    = player.MaxHealth();
            float hydro    = player.metabolism.hydration.value;
            float hydroMax = player.metabolism.hydration.max;
            float food     = player.metabolism.calories.value;
            float foodMax  = player.metabolism.calories.max;

            float hpPct    = hpMax    > 0 ? Mathf.Clamp01(hp    / hpMax)    : 0f;
            float hydroPct = hydroMax > 0 ? Mathf.Clamp01(hydro / hydroMax) : 0f;
            float foodPct  = foodMax  > 0 ? Mathf.Clamp01(food  / foodMax)  : 0f;

            var c = new CuiElementContainer();
            string box = c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, StatsName, StatsBarsName);

            AddStatBar(c, box, 0.67f, 0.96f,
                $"{Mathf.RoundToInt(hp)} / {Mathf.RoundToInt(hpMax)}", hpPct, HealthGreen);
            AddStatBar(c, box, 0.355f, 0.645f,
                $"{Mathf.RoundToInt(hydro)} / {Mathf.RoundToInt(hydroMax)}", hydroPct, HydroBlue);
            AddStatBar(c, box, 0.04f, 0.33f,
                $"{Mathf.RoundToInt(food)} / {Mathf.RoundToInt(foodMax)}", foodPct, FoodOrange);

            CuiHelper.AddUi(player, c);
        }

        private void AddStatBar(CuiElementContainer c, string parent,
            float yMin, float yMax, string valueLabel, float pct, string barColor)
        {
            // dark track background (icon sits to the left, drawn in the cover)
            string track = c.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.07" },
                RectTransform =
                {
                    AnchorMin = $"0.24 {(yMin + 0.05f).ToString(CultureInfo.InvariantCulture)}",
                    AnchorMax = $"0.66 {(yMax - 0.05f).ToString(CultureInfo.InvariantCulture)}"
                }
            }, parent);

            // coloured fill bar (width proportional to pct)
            if (pct > 0f)
                c.Add(new CuiPanel
                {
                    Image = { Color = barColor },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = $"{pct.ToString(CultureInfo.InvariantCulture)} 1"
                    }
                }, track);

            // numeric value (right of bar)
            c.Add(new CuiLabel
            {
                Text = { Text = valueLabel, FontSize = 11, Font = FontReg,
                         Align = TextAnchor.MiddleRight, Color = White },
                RectTransform =
                {
                    AnchorMin = $"0.68 {yMin.ToString(CultureInfo.InvariantCulture)}",
                    AnchorMax = $"0.97 {yMax.ToString(CultureInfo.InvariantCulture)}"
                }
            }, parent);
        }

        // Poll every 2 s to keep the vitals bars live (avoids hook signature issues)
        // Called from OnServerInitialized — see timer registration below.
        #endregion

        #region Notifications (header shower)
        private void PollNotifications()
        {
            if (string.IsNullOrEmpty(config.NotificationsApiUrl)) return;

            webrequest.Enqueue($"{config.NotificationsApiUrl}?since={lastNotifId}", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                try
                {
                    var json = JObject.Parse(response);
                    long latest = json["latestId"]?.Value<long>() ?? lastNotifId;

                    // first successful poll just primes the cursor (no backlog spam)
                    if (!notifPrimed)
                    {
                        lastNotifId = latest;
                        notifPrimed = true;
                        return;
                    }

                    var arr = json["notifications"] as JArray;
                    if (arr == null) return;
                    foreach (var n in arr)
                    {
                        long id = n["id"]?.Value<long>() ?? 0;
                        if (id <= lastNotifId) continue;
                        lastNotifId = Math.Max(lastNotifId, id);
                        ShowNotificationAll(
                            n["title"]?.Value<string>() ?? "",
                            n["message"]?.Value<string>() ?? "",
                            n["type"]?.Value<string>() ?? "info",
                            n["durationSec"]?.Value<float>() ?? 6f);
                    }
                }
                catch (Exception e) { PrintWarning("Notification parse failed: " + e.Message); }
            }, this, RequestMethod.GET, null, 8f);
        }

        private void ShowNotificationAll(string title, string message, string type, float duration)
        {
            if (string.IsNullOrEmpty(message)) return;
            int myId = ++notifSeq;
            foreach (var p in BasePlayer.activePlayerList) BuildNotif(p, title, message, type);

            timer.Once(Mathf.Clamp(duration, 2f, 30f), () =>
            {
                if (notifSeq != myId) return; // a newer notification replaced it
                foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, NotifyName);
            });
        }

        private void BuildNotif(BasePlayer player, string title, string message, string type)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, NotifyName);

            string accent =
                type == "success" ? "0.17 1 0.53 1" :
                type == "warning" ? "1 0.69 0.13 1" :
                type == "alert" ? "1 0.25 0.38 1" : "0 0.898 1 1";
            string head = string.IsNullOrEmpty(title) ? "PROJECT RIFT" : title.ToUpper();

            var c = new CuiElementContainer();
            string root = c.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.09 0.9", Material = Blur, FadeIn = 0.35f },
                RectTransform = { AnchorMin = "0.34 0.925", AnchorMax = "0.66 0.967" }
            }, "Hud", NotifyName);

            c.Add(new CuiPanel
            {
                Image = { Color = accent, FadeIn = 0.35f },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.012 1" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = head, FontSize = 10, Font = FontBold, Align = TextAnchor.UpperLeft, Color = accent, FadeIn = 0.35f },
                RectTransform = { AnchorMin = "0.04 0.5", AnchorMax = "0.98 0.94" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = message, FontSize = 13, Font = FontReg, Align = TextAnchor.LowerLeft, Color = White, FadeIn = 0.35f },
                RectTransform = { AnchorMin = "0.04 0.08", AnchorMax = "0.98 0.55" }
            }, root);

            CuiHelper.AddUi(player, c);
        }
        #endregion

        #region Radiation proximity warning
        private void CacheRadiationZones()
        {
            radZones.Clear();
            foreach (var tr in UnityEngine.Object.FindObjectsOfType<TriggerRadiation>())
            {
                if (tr == null) continue;
                var col = tr.GetComponent<Collider>() ?? tr.GetComponentInChildren<Collider>();
                if (col != null) radZones.Add(col);
            }
            Puts($"Cached {radZones.Count} radiation zones.");
        }

        private void RadiationTick()
        {
            if (radZones.Count == 0) return;
            float range = config.RadiationWarnRange;
            float reject = (range + 250f) * (range + 250f);

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected || p.IsSleeping() || p.IsDead())
                {
                    ClearRad(p);
                    continue;
                }

                Vector3 pos = p.transform.position;
                float best = float.MaxValue;
                for (int i = 0; i < radZones.Count; i++)
                {
                    var col = radZones[i];
                    if (col == null) continue;
                    if ((col.bounds.center - pos).sqrMagnitude > reject) continue;
                    Vector3 cp;
                    try { cp = col.ClosestPoint(pos); } catch { continue; }
                    float d = Vector3.Distance(pos, cp);
                    if (d < best) best = d;
                }

                if (best <= range)
                {
                    ShowRad(p, best);
                    if (config.RadiationBeepEnabled) HandleRadSound(p, best);
                }
                else
                {
                    ClearRad(p);
                    radNextBeep.Remove(p.userID);
                    radInside.Remove(p.userID);
                }
            }
        }

        private void HandleRadSound(BasePlayer player, float dist)
        {
            // Inside the zone: alarm once on entry, then let Rust's own geiger sound take over.
            if (dist <= 1f)
            {
                if (radInside.Add(player.userID))
                    RunEffect(player, config.RadiationEnterPrefab);
                return;
            }
            radInside.Remove(player.userID);

            // Approaching: beep, faster the closer they get (geiger-style).
            float now = Time.realtimeSinceStartup;
            if (radNextBeep.TryGetValue(player.userID, out var next) && now < next) return;
            float cadence = dist < 10f ? 0.5f : dist < 25f ? 1f : dist < 45f ? 2f : 3f;
            radNextBeep[player.userID] = now + cadence;
            RunEffect(player, config.RadiationBeepPrefab);
        }

        private void RunEffect(BasePlayer player, string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || player == null || player.net == null) return;
            var effect = new Effect(prefab, player, 0, Vector3.zero, Vector3.forward);
            EffectNetwork.Send(effect, player.net.connection);
        }

        private void ShowRad(BasePlayer player, float dist)
        {
            string text, dot;
            if (dist <= 1f)
            {
                text = "IN RADIATION ZONE";
                dot = "1 0.25 0.25 1";
            }
            else
            {
                text = $"RADIATION  {Mathf.CeilToInt(dist)}m";
                dot = dist < 20f ? "1 0.32 0.2 1" : "1 0.7 0.15 1";
            }

            if (lastRadText.TryGetValue(player.userID, out var prev) && prev == text) return;
            lastRadText[player.userID] = text;

            CuiHelper.DestroyUi(player, RadName);
            var c = new CuiElementContainer();
            string card = c.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.04 0.04 0.88", Material = Blur },
                RectTransform = { AnchorMin = "0.84 0.832", AnchorMax = "0.998 0.872" }
            }, "Hud", RadName);

            c.Add(new CuiPanel
            {
                Image = { Color = dot },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.012 1" }
            }, card);

            c.Add(new CuiLabel
            {
                Text = { Text = $"<color={Rich(dot)}>●</color>  {text}", FontSize = 12, Font = FontBold, Align = TextAnchor.MiddleLeft, Color = "0.96 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.06 0", AnchorMax = "0.98 1" }
            }, card);

            CuiHelper.AddUi(player, c);
        }

        private void ClearRad(BasePlayer player)
        {
            if (player != null && lastRadText.Remove(player.userID))
                CuiHelper.DestroyUi(player, RadName);
        }

        // convert "r g b a" (0-1) to a #rrggbb hex for rich-text color tags
        private string Rich(string rgba)
        {
            var parts = rgba.Split(' ');
            int r = Mathf.RoundToInt(float.Parse(parts[0], CultureInfo.InvariantCulture) * 255);
            int g = Mathf.RoundToInt(float.Parse(parts[1], CultureInfo.InvariantCulture) * 255);
            int b = Mathf.RoundToInt(float.Parse(parts[2], CultureInfo.InvariantCulture) * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        #endregion

        #region Welcome screen (cinematic)
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            sessionStart[player.userID] = Time.realtimeSinceStartup;
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
            // safety net: a sleeping/awake player is alive — clear any death UI
            if (deathSessions.Remove(player.userID))
                CuiHelper.DestroyUi(player, DeathName);
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
            if (ImageLibrary == null) return;
            // cache the round logo for the HUD (avoids per-rebuild flicker)
            if (!string.IsNullOrEmpty(config.LogoUrl))
                ImageLibrary.Call("AddImage", config.LogoUrl, "rift_logo", 0UL);

            if (config.SpinFrameCount <= 0 || string.IsNullOrEmpty(config.SpinFrameBaseUrl)) return;
            for (int i = 0; i < config.SpinFrameCount; i++)
                ImageLibrary.Call("AddImage", $"{config.SpinFrameBaseUrl}{i}.png", $"rift_spin_{i}", 0UL);
            Puts($"Queued logo + {config.SpinFrameCount} spinning-logo frames with ImageLibrary.");
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
            CommitSession(player);
            SavePlaytime();
            aliveSince.Remove(player.userID);
            deathSessions.Remove(player.userID);
            lastRadText.Remove(player.userID);
            radNextBeep.Remove(player.userID);
            radInside.Remove(player.userID);
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
            if (victim == null) return;

            // The death screen lives as long as the death SESSION exists.
            // (Don't use IsDead() here — at OnPlayerDeath time the lifestate
            // hasn't flipped yet, which would destroy the screen instantly.)
            // The session is cleared by OnPlayerRespawned / OnPlayerDisconnected.
            if (!deathSessions.TryGetValue(victim.userID, out var session))
            {
                CuiHelper.DestroyUi(victim, DeathName);
                return;
            }
            if (!victim.IsConnected)
            {
                deathSessions.Remove(victim.userID);
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
