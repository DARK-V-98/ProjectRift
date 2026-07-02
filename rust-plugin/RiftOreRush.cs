// RiftOreRush — weekly ore-mining boost event for PROJECT RIFT.
//
// Every Saturday & Sunday at 9:00 PM (server time) an "Ore Rush" runs for
// 30 minutes: all ore mined gives 10x. Players get a 15-minute warning, a
// "LIVE" notice, periodic ongoing notices, and an end notice.
//
//   /orerush start|stop|status   (admin)
//
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RiftOreRush", "ESYSTEMLK", "1.0.0")]
    [Description("Weekly Ore Rush: 10x ore mining every Sat & Sun at 9 PM for 30 minutes, with server notices.")]
    public class RiftOreRush : RustPlugin
    {
        [PluginReference] private Plugin ProjectRiftCore;

        private const string PermAdmin = "riftorerush.admin";

        private Configuration config;
        private bool active;
        private DateTime endsAt;
        private string ranDate = "";     // yyyy-MM-dd we last started
        private string preDate = "";     // yyyy-MM-dd we last sent the pre-notice
        private Timer ongoingTimer;
        private Timer hudTimer;
        private HashSet<string> oreSet;
        private List<int> oreItemIds = new List<int>();
        private int hudTick;

        private const string HudRoot = "riftorerush.hud";
        private const string HudDyn = "riftorerush.hud.dyn";

        public class Configuration
        {
            [JsonProperty("Event days")]
            public List<string> Days = new List<string> { "Saturday", "Sunday" };
            [JsonProperty("Start hour (0-23, server time)")] public int StartHour = 21;   // 9 PM
            [JsonProperty("Start minute")] public int StartMinute = 0;
            [JsonProperty("Duration (minutes)")] public int DurationMinutes = 30;
            [JsonProperty("Ore multiplier")] public int Multiplier = 10;
            [JsonProperty("Warning before start (minutes)")] public int PreNoticeMinutes = 15;
            [JsonProperty("Ongoing notice interval (minutes)")] public int OngoingNoticeMinutes = 10;
            [JsonProperty("Use ProjectRiftCore notification shower")] public bool UseCoreNotifications = true;
            [JsonProperty("Chat prefix")] public string Prefix = "<color=#B026FF>[ORE RUSH]</color>";
            [JsonProperty("Ore item shortnames")]
            public List<string> Ores = new List<string> { "metal.ore", "sulfur.ore", "hq.metal.ore", "stones" };
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

        private void Init() => permission.RegisterPermission(PermAdmin, this);

        private void OnServerInitialized()
        {
            oreSet = new HashSet<string>(config.Ores, StringComparer.OrdinalIgnoreCase);
            oreItemIds.Clear();
            foreach (var sn in config.Ores)
            {
                var def = ItemManager.FindItemDefinition(sn);
                if (def != null) oreItemIds.Add(def.itemid);
            }
            timer.Every(20f, Tick);   // schedule checker (also recovers after restarts)
            Tick();
        }

        private void Unload()
        {
            ongoingTimer?.Destroy();
            hudTimer?.Destroy();
            HideHudAll();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (active && player != null)
                timer.Once(3f, () => { if (player != null && player.IsConnected && active) { BuildHudBase(player); DrawHudDynamic(player); } });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null) CuiHelper.DestroyUi(player, HudRoot);
        }

        // ---- schedule --------------------------------------------------------
        private void Tick()
        {
            var now = DateTime.Now;

            if (active && now >= endsAt) StopEvent();

            bool isEventDay = config.Days.Exists(d => string.Equals(d, now.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase));
            if (!isEventDay) return;

            var start = new DateTime(now.Year, now.Month, now.Day, config.StartHour, config.StartMinute, 0);
            var end = start.AddMinutes(config.DurationMinutes);
            var pre = start.AddMinutes(-config.PreNoticeMinutes);
            string today = now.ToString("yyyy-MM-dd");

            // 15-min warning
            if (!active && preDate != today && now >= pre && now < start)
            {
                preDate = today;
                Announce($"{config.Prefix} Ore Rush starts in <color=#00e5ff>{config.PreNoticeMinutes} minutes</color>! Get ready for {config.Multiplier}x ore.");
            }

            // start (also fires if the server came up mid-window)
            if (!active && ranDate != today && now >= start && now < end)
            {
                ranDate = today;
                StartEvent(end);
            }
        }

        private void StartEvent(DateTime end)
        {
            active = true;
            endsAt = end;
            int mins = Mathf.Max(1, (int)Math.Round((end - DateTime.Now).TotalMinutes));
            Announce($"{config.Prefix} <color=#2bff88>ORE RUSH IS LIVE!</color> {config.Multiplier}x ore for the next {mins} minutes — go mine!");

            ongoingTimer?.Destroy();
            ongoingTimer = timer.Every(Mathf.Max(60f, config.OngoingNoticeMinutes * 60f), () =>
            {
                if (!active) return;
                int left = Mathf.Max(1, (int)Math.Round((endsAt - DateTime.Now).TotalMinutes));
                Announce($"{config.Prefix} Active now — <color=#2bff88>{config.Multiplier}x ore</color>! {left} min left.");
            });

            // animated HUD banner for everyone
            foreach (var pl in BasePlayer.activePlayerList) { BuildHudBase(pl); DrawHudDynamic(pl); }
            hudTimer?.Destroy();
            hudTimer = timer.Every(1f, () =>
            {
                if (!active) return;
                hudTick++;
                foreach (var pl in BasePlayer.activePlayerList)
                    if (pl != null && pl.IsConnected) DrawHudDynamic(pl);
            });
        }

        private void StopEvent()
        {
            if (!active) return;
            active = false;
            ongoingTimer?.Destroy();
            ongoingTimer = null;
            hudTimer?.Destroy();
            hudTimer = null;
            HideHudAll();
            Announce($"{config.Prefix} Ore Rush has ended. Thanks for mining!");
        }

        // ---- animated HUD ----------------------------------------------------
        private const string ColBg = "0.05 0.06 0.10 0.92";
        private const string ColAccent = "0.95 0.62 0.15 1";   // ore-gold accent

        private void BuildHudBase(BasePlayer pl)
        {
            var c = new CuiElementContainer();
            CuiHelper.DestroyUi(pl, HudRoot);

            c.Add(new CuiPanel
            {
                Image = { Color = ColBg, FadeIn = 0.5f },
                RectTransform = { AnchorMin = "0.35 0.895", AnchorMax = "0.65 0.96" },
                CursorEnabled = false
            }, "Hud", HudRoot);

            // animated-feel accent bar (fades in)
            c.Add(new CuiPanel { Image = { Color = ColAccent, FadeIn = 0.6f },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" } }, HudRoot);

            CuiHelper.AddUi(pl, c);
        }

        private void DrawHudDynamic(BasePlayer pl)
        {
            if (pl == null || !pl.IsConnected) return;
            CuiHelper.DestroyUi(pl, HudDyn);

            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.9" } }, HudRoot, HudDyn);

            // cycling ore icons (one highlighted each second → animated marquee)
            int n = oreItemIds.Count;
            if (n > 0)
            {
                int hi = hudTick % n;
                float slot = 0.30f / n;
                for (int i = 0; i < n; i++)
                {
                    float x0 = 0.03f + i * slot, x1 = x0 + slot - 0.005f;
                    bool lit = i == hi;
                    c.Add(new CuiElement
                    {
                        Parent = HudDyn,
                        Components =
                        {
                            new CuiImageComponent { ItemId = oreItemIds[i], Color = lit ? "1 1 1 1" : "1 1 1 0.45", FadeIn = 0.4f },
                            new CuiRectTransformComponent
                            {
                                // highlighted icon sits slightly taller (pseudo-bounce)
                                AnchorMin = $"{x0} {(lit ? 0.16f : 0.26f)}",
                                AnchorMax = $"{x1} {(lit ? 0.92f : 0.82f)}"
                            }
                        }
                    });
                }
            }

            // pulsing multiplier
            bool bright = hudTick % 2 == 0;
            string mulCol = bright ? "1 0.8 0.25 1" : "0.95 0.55 0.1 1";
            Text(c, HudDyn, $"<color={mulCol}>{config.Multiplier}x</color> ORE RUSH", 15,
                 "0.34 0.1", "0.66 0.95", TextAnchor.MiddleCenter, "1 1 1 1", 0.5f);

            // live countdown
            var span = endsAt - DateTime.Now;
            if (span.TotalSeconds < 0) span = TimeSpan.Zero;
            Text(c, HudDyn, $"<color=#00e5ff>{(int)span.TotalMinutes:00}:{span.Seconds:00}</color> left", 12,
                 "0.67 0.1", "0.98 0.95", TextAnchor.MiddleRight, "0.85 0.88 0.95 1", 0.3f);

            CuiHelper.AddUi(pl, c);
        }

        private void HideHudAll()
        {
            foreach (var pl in BasePlayer.activePlayerList) CuiHelper.DestroyUi(pl, HudRoot);
        }

        private void Text(CuiElementContainer c, string parent, string text, int size,
                          string min, string max, TextAnchor align, string color, float fade)
        {
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent { Text = text, FontSize = size, Align = align, Color = color,
                        Font = "robotocondensed-bold.ttf", FadeIn = fade },
                    new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max }
                }
            });
        }

        // ---- the boost -------------------------------------------------------
        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (!active || item == null) return;
            var player = entity as BasePlayer;
            if (player == null || player.IsNpc) return;
            if (oreSet.Contains(item.info.shortname))
                item.amount *= config.Multiplier;
        }

        private void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (!active || item == null || player == null || player.IsNpc) return;
            if (oreSet.Contains(item.info.shortname))
                item.amount *= config.Multiplier;
        }

        // ---- admin -----------------------------------------------------------
        [ChatCommand("orerush")]
        private void CmdOreRush(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin))
            { player.ChatMessage("No permission."); return; }

            string sub = args.Length > 0 ? args[0].ToLower() : "status";
            switch (sub)
            {
                case "start":
                    if (active) { player.ChatMessage("Ore Rush is already running."); return; }
                    StartEvent(DateTime.Now.AddMinutes(config.DurationMinutes));
                    break;
                case "stop":
                    if (!active) { player.ChatMessage("No Ore Rush running."); return; }
                    StopEvent();
                    break;
                default:
                    player.ChatMessage(active
                        ? $"{config.Prefix} ACTIVE — {config.Multiplier}x ore · {Mathf.Max(0, (int)Math.Round((endsAt - DateTime.Now).TotalMinutes))} min left."
                        : $"{config.Prefix} Not running. Scheduled {string.Join(" & ", config.Days)} at {config.StartHour:00}:{config.StartMinute:00} for {config.DurationMinutes} min.");
                    break;
            }
        }

        [ConsoleCommand("riftorerush.start")]
        private void CcStart(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            if (!active) StartEvent(DateTime.Now.AddMinutes(config.DurationMinutes));
            arg.ReplyWith("Ore Rush started.");
        }

        [ConsoleCommand("riftorerush.stop")]
        private void CcStop(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            StopEvent();
            arg.ReplyWith("Ore Rush stopped.");
        }

        // ---- helpers ---------------------------------------------------------
        private void Announce(string msg)
        {
            try
            {
                if (config.UseCoreNotifications && ProjectRiftCore != null && ProjectRiftCore.IsLoaded)
                    ProjectRiftCore.Call("PushNotification", msg, "info");
                else
                    PrintToChat(msg);
            }
            catch (Exception ex) { PrintError($"Announce failed: {ex.Message}"); }
        }
    }
}
