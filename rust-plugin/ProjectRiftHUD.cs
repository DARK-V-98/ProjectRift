using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Project Rift HUD", "ProjectRift", "1.0.0")]
    [Description("Premium futuristic UI for Project Rift")]
    public class ProjectRiftHUD : RustPlugin
    {
        #region Configuration
        private Configuration config;

        public class Configuration
        {
            [JsonProperty("Enable HUD")]
            public bool EnableHUD = true;

            [JsonProperty("Enable Vitals (Health/Water/Food)")]
            public bool EnableVitals = true;
            
            [JsonProperty("Enable Server Info Panel")]
            public bool EnableServerInfo = true;

            [JsonProperty("Enable Notifications")]
            public bool EnableNotifications = true;

            [JsonProperty("Enable Event Announcer")]
            public bool EnableEventAnnouncer = true;

            [JsonProperty("Colors")]
            public ColorConfig Colors = new ColorConfig();

            public class ColorConfig
            {
                [JsonProperty("Primary Accent (Purple)")]
                public string PrimaryAccent = "0.54 0.24 1.0 1.0"; // #8B3DFF

                [JsonProperty("Health Bar")]
                public string HealthBar = "0.8 0.2 0.2 1.0";

                [JsonProperty("Water Bar")]
                public string WaterBar = "0.2 0.6 1.0 1.0";

                [JsonProperty("Food Bar")]
                public string FoodBar = "1.0 0.6 0.2 1.0";
                
                [JsonProperty("Background")]
                public string Background = "0 0 0 0.85";
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
            }
            catch
            {
                PrintError("Config is invalid, creating a new one.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }
        #endregion

        #region Localisation
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["HUD_Health"] = "HEALTH",
                ["HUD_Water"] = "WATER",
                ["HUD_Food"] = "FOOD",
                ["HUD_Temp"] = "TEMP",
                ["HUD_Rad"] = "RAD",
                ["HUD_Wet"] = "WET",
                ["HUD_Safe"] = "SAFE",
                ["HUD_Blocked"] = "BLOCKED",
                ["HUD_Players"] = "PLAYERS",
                ["HUD_Wipe"] = "WIPE IN",
                ["HUD_Ping"] = "PING",
                ["HUD_FPS"] = "FPS"
            }, this, "en");
        }
        #endregion

        #region Permissions
        private const string PermUse = "projectrifthud.use";
        private const string PermAdmin = "projectrifthud.admin";
        private const string PermBypass = "projectrifthud.bypass";

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermBypass, this);
        }
        #endregion
        
        #region UI Identifiers
        private const string HUDName = "ProjectRift.HUDMain";
        private const string VitalsContainer = "ProjectRift.Vitals";
        private const string HealthBarPanel = "ProjectRift.Health";
        private const string WaterBarPanel = "ProjectRift.Water";
        private const string FoodBarPanel = "ProjectRift.Food";
        private const string StatusStripPanel = "ProjectRift.StatusStrip";
        private const string ServerInfoPanel = "ProjectRift.ServerInfo";
        private const string BrandingPanel = "ProjectRift.Branding";
        private const string NightIndicatorPanel = "ProjectRift.Night";
        private const string EventAnnouncerPanel = "ProjectRift.EventAnnouncer";
        
        private const string ColorBlurMat = "assets/content/ui/uibackgroundblur.mat";
        private const string ColorWhite = "1 1 1 1";
        private const string ColorBlack = "0 0 0 1";
        private const string ColorRed = "0.8 0.2 0.2 1";
        private const string ColorOrange = "0.96 0.60 0.11 1";
        private const string ColorBlue = "0.11 0.62 0.95 1";
        #endregion

        #region PlayerHUDState
        private class PlayerHUDState
        {
            public float LastHealth = -1f;
            public float LastWater = -1f;
            public float LastFood = -1f;
            public float LastTemp = -999f;
            public float LastWet = -1f;
            public float LastRad = -1f;
            public bool WasBleeding = false;
            public bool WasSafe = false;
            public bool WasBuildingBlocked = false;
            
            public bool IsLowHealthPulseActive = false;
            
            public List<string> ActiveNotifications = new List<string>();
        }
        
        private Dictionary<ulong, PlayerHUDState> playerStates = new Dictionary<ulong, PlayerHUDState>();
        
        private PlayerHUDState GetState(BasePlayer player)
        {
            if (!playerStates.TryGetValue(player.userID, out var state))
            {
                state = new PlayerHUDState();
                playerStates[player.userID] = state;
            }
            return state;
        }
        #endregion

        #region Lifecycle
        private Timer vitalsTimer;
        private Timer statusTimer;
        private Timer infoTimer;

        private void Init()
        {
            RegisterPermissions();
        }

        private void OnServerInitialized()
        {
            if (!config.EnableHUD) return;

            // Grant permission to default group if auto-grant is configured or true by default
            if (permission.GroupExists("default"))
            {
                if (!permission.GroupHasPermission("default", PermUse))
                {
                    permission.GrantGroupPermission("default", PermUse, this);
                }
            }

            // Build HUD for all currently online players
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected && permission.UserHasPermission(player.UserIDString, PermUse))
                {
                    BuildAll(player);
                }
            }

            // Start optimization timers
            vitalsTimer = timer.Every(1.5f, VitalsTick);
            statusTimer = timer.Every(2.0f, StatusTick);
            infoTimer = timer.Every(5.0f, ServerInfoTick);
        }

        private void Unload()
        {
            vitalsTimer?.Destroy();
            statusTimer?.Destroy();
            infoTimer?.Destroy();

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null)
                {
                    DestroyAll(player);
                }
            }
            playerStates.Clear();
        }
        #endregion

        #region Player Hooks
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            // Wait a small moment for player initialization to finish on the client
            timer.Once(1.0f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    BuildAll(player);
                }
            });
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            playerStates.Remove(player.userID);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            timer.Once(0.5f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    BuildAll(player);
                }
            });
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null) return;
            DestroyAll(player);
        }
        #endregion

        #region Update Ticks
        private void VitalsTick()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsDead() || player.IsSleeping()) continue;
                if (!permission.UserHasPermission(player.UserIDString, PermUse)) continue;

                UpdateVitals(player);
            }
        }

        private void StatusTick()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsDead() || player.IsSleeping()) continue;
                if (!permission.UserHasPermission(player.UserIDString, PermUse)) continue;

                UpdateStatusStrip(player);
                UpdateNightIndicator(player);
            }
        }

        private void ServerInfoTick()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsDead() || player.IsSleeping()) continue;
                if (!permission.UserHasPermission(player.UserIDString, PermUse)) continue;

                UpdateServerInfo(player);
            }
        }
        #endregion

        #region Notifications & Events
        public void AddNotification(BasePlayer player, string message, string type = "info", float duration = 5f)
        {
            if (player == null || !player.IsConnected || !config.EnableNotifications) return;

            var state = GetState(player);

            // Shift existing notifications up if we exceed 3 active
            if (state.ActiveNotifications.Count >= 3)
            {
                var oldest = state.ActiveNotifications[0];
                CuiHelper.DestroyUi(player, oldest);
                state.ActiveNotifications.RemoveAt(0);
            }

            string notificationId = $"ProjectRift.Notif.{Guid.NewGuid().ToString("N")}";
            state.ActiveNotifications.Add(notificationId);

            // Re-render all notifications in their correct stack position
            RenderNotificationStack(player);

            // Schedule self-destruction
            timer.Once(duration, () =>
            {
                if (player != null && player.IsConnected)
                {
                    var s = GetState(player);
                    if (s.ActiveNotifications.Contains(notificationId))
                    {
                        CuiHelper.DestroyUi(player, notificationId);
                        s.ActiveNotifications.Remove(notificationId);
                        RenderNotificationStack(player);
                    }
                }
            });
        }

        private void RenderNotificationStack(BasePlayer player)
        {
            var state = GetState(player);
            
            // Destroy all current notification UIs (but keep the ids in list)
            foreach (var nid in state.ActiveNotifications)
            {
                CuiHelper.DestroyUi(player, nid);
            }

            // Draw them at their respective positions in the stack (max 3)
            for (int i = 0; i < state.ActiveNotifications.Count; i++)
            {
                string nid = state.ActiveNotifications[i];
                
                // Stack layout from top (0.81) down to (0.67)
                // Position depends on its index in the active list
                float minVal = 0.81f - (i * 0.07f);
                float maxVal = 0.88f - (i * 0.07f);

                string anchorMin = $"0.72 {minVal:F3}";
                string anchorMax = $"0.998 {maxVal:F3}";

                var container = new CuiElementContainer();

                // Notif glass body
                container.Add(new CuiPanel
                {
                    CursorEnabled = false,
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Image = { Color = "0 0 0 0.6", Material = ColorBlurMat }
                }, "Hud", nid);

                // Purple accent line left
                container.Add(new CuiPanel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" },
                    Image = { Color = config.Colors.PrimaryAccent }
                }, nid);

                // Notification Text
                container.Add(new CuiLabel
                {
                    Text = { Text = "🔔  NOTIFICATION", FontSize = 9, Font = "robotocondensed-bold.ttf", Align = TextAnchor.UpperLeft, Color = "1 1 1 0.5" },
                    RectTransform = { AnchorMin = "0.05 0.55", AnchorMax = "0.95 0.95" }
                }, nid);

                // We can support messages with custom styling/type in the main text box
                container.Add(new CuiLabel
                {
                    Text = { Text = "Sample notification message content", FontSize = 10, Font = "robotocondensed-regular.ttf", Align = TextAnchor.MiddleLeft, Color = "1 1 1 0.9" },
                    RectTransform = { AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.6" }
                }, nid);

                CuiHelper.AddUi(player, container);
            }
        }

        public void ShowEvent(BasePlayer player, string eventName, float duration = 8f)
        {
            if (player == null || !player.IsConnected || !config.EnableEventAnnouncer) return;

            CuiHelper.DestroyUi(player, EventAnnouncerPanel);

            var container = new CuiElementContainer();

            // Centered header card (top center)
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.36 0.946", AnchorMax = "0.64 0.993" },
                Image = { Color = "0 0 0 0.6", Material = ColorBlurMat }
            }, "Hud", EventAnnouncerPanel);

            // Left/right purple accent line
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" },
                Image = { Color = config.Colors.PrimaryAccent }
            }, EventAnnouncerPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"⚠️  EVENT ACTIVE: {eventName.ToUpper()}", FontSize = 12, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.9", FadeIn = 0.3f },
                RectTransform = { AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.9" }
            }, EventAnnouncerPanel);

            CuiHelper.AddUi(player, container);

            timer.Once(duration, () =>
            {
                if (player != null && player.IsConnected)
                {
                    CuiHelper.DestroyUi(player, EventAnnouncerPanel);
                }
            });
        }
        #endregion

        #region UI Building & Updates
        private void BuildAll(BasePlayer player)
        {
            DestroyAll(player);

            if (!config.EnableHUD) return;

            var state = GetState(player);
            state.LastHealth = -1f;
            state.LastWater = -1f;
            state.LastFood = -1f;
            state.LastTemp = -999f;
            state.LastWet = -1f;
            state.LastRad = -1f;
            state.WasBleeding = false;
            state.WasSafe = false;
            state.WasBuildingBlocked = false;

            BuildBranding(player);
            BuildServerInfo(player);
            BuildVitalsContainer(player);
            
            UpdateVitals(player);
            UpdateStatusStrip(player);
            UpdateNightIndicator(player);
        }

        private void DestroyAll(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, BrandingPanel);
            CuiHelper.DestroyUi(player, ServerInfoPanel);
            CuiHelper.DestroyUi(player, VitalsContainer);
            CuiHelper.DestroyUi(player, StatusStripPanel);
            CuiHelper.DestroyUi(player, NightIndicatorPanel);
            CuiHelper.DestroyUi(player, EventAnnouncerPanel);

            // Clean up any remaining notifications
            var state = GetState(player);
            foreach (var nid in state.ActiveNotifications.ToList())
            {
                CuiHelper.DestroyUi(player, nid);
            }
            state.ActiveNotifications.Clear();
        }

        private void BuildBranding(BasePlayer player)
        {
            var container = new CuiElementContainer();

            // Root panel (top-left) with glass blur
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.002 0.946", AnchorMax = "0.175 0.998" },
                Image = { Color = "0 0 0 0.4", Material = ColorBlurMat }
            }, "Hud", BrandingPanel);

            // Purple glowing accent bar on the left
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" },
                Image = { Color = config.Colors.PrimaryAccent }
            }, BrandingPanel);

            // Title
            container.Add(new CuiLabel
            {
                Text = { Text = "PROJECT RIFT", FontSize = 15, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = "1 1 1 0.9" },
                RectTransform = { AnchorMin = "0.07 0", AnchorMax = "0.95 1" }
            }, BrandingPanel);

            CuiHelper.AddUi(player, container);
        }

        private void BuildServerInfo(BasePlayer player)
        {
            if (!config.EnableServerInfo) return;

            CuiHelper.DestroyUi(player, ServerInfoPanel);

            var container = new CuiElementContainer();

            // Server Info Panel (top-right)
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.822 0.888", AnchorMax = "0.998 0.998" },
                Image = { Color = "0 0 0 0.4", Material = ColorBlurMat }
            }, "Hud", ServerInfoPanel);

            // Purple accent line top
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 1" },
                Image = { Color = config.Colors.PrimaryAccent }
            }, ServerInfoPanel);

            CuiHelper.AddUi(player, container);
            UpdateServerInfo(player);
        }

        private void UpdateServerInfo(BasePlayer player)
        {
            if (!config.EnableServerInfo) return;

            CuiHelper.DestroyUi(player, ServerInfoPanel + ".Content");

            var container = new CuiElementContainer();

            int onlinePlayers = BasePlayer.activePlayerList.Count;
            int maxPlayers = ConVar.Server.maxplayers;
            int serverFps = Performance.report.frameRate;
            int ping = Network.Net.sv.GetAveragePing(player.net.connection);
            string timeString = DateTime.UtcNow.ToString("HH:mm") + " UTC";

            // Wipe Countdown (Static mockup for now or parsed if desired)
            string wipeCountdown = "T-3d 4h"; 

            string infoText = $"<color={config.Colors.PrimaryAccent}>●</color> {onlinePlayers}/{maxPlayers} Players   " +
                             $"FPS: {serverFps}   " +
                             $"PING: {ping}ms   " +
                             $"TIME: {timeString}   " +
                             $"WIPE: {wipeCountdown}";

            container.Add(new CuiLabel
            {
                Text = { Text = infoText, FontSize = 10, Font = "robotocondensed-regular.ttf", Align = TextAnchor.MiddleRight, Color = "1 1 1 0.7", FadeIn = 0.1f },
                RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.95 0.8" }
            }, ServerInfoPanel, ServerInfoPanel + ".Content");

            CuiHelper.AddUi(player, container);
        }

        private void BuildVitalsContainer(BasePlayer player)
        {
            if (!config.EnableVitals) return;

            CuiHelper.DestroyUi(player, VitalsContainer);

            var container = new CuiElementContainer();

            // Right-aligned container card for Health, Water, Food
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.782 0.055", AnchorMax = "0.998 0.36" },
                Image = { Color = "0 0 0 0.4", Material = ColorBlurMat }
            }, "Hud", VitalsContainer);

            // Left side purple accent
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" },
                Image = { Color = config.Colors.PrimaryAccent }
            }, VitalsContainer);

            CuiHelper.AddUi(player, container);
        }

        private void UpdateVitals(BasePlayer player)
        {
            if (!config.EnableVitals) return;

            var state = GetState(player);
            float hp = player.health;
            float water = player.metabolism.hydration.value;
            float food = player.metabolism.calories.value;

            // Simple epsilon comparison to avoid rebuilds
            bool hpChanged = Mathf.Abs(state.LastHealth - hp) > 0.5f;
            bool waterChanged = Mathf.Abs(state.LastWater - water) > 0.5f;
            bool foodChanged = Mathf.Abs(state.LastFood - food) > 0.5f;

            if (hpChanged)
            {
                state.LastHealth = hp;
                CuiHelper.DestroyUi(player, HealthBarPanel);
                DrawVitalsBar(player, "❤️ HEALTH", hp, 100f, HealthBarPanel, "0.67 0.86", config.Colors.HealthBar);

                // Check for low health pulse triggers (< 25%)
                if (hp < 25f && !state.IsLowHealthPulseActive)
                {
                    state.IsLowHealthPulseActive = true;
                    StartLowHealthPulse(player);
                }
                else if (hp >= 25f && state.IsLowHealthPulseActive)
                {
                    state.IsLowHealthPulseActive = false;
                }
            }

            if (waterChanged)
            {
                state.LastWater = water;
                CuiHelper.DestroyUi(player, WaterBarPanel);
                DrawVitalsBar(player, "💧 WATER", water, 250f, WaterBarPanel, "0.38 0.57", config.Colors.WaterBar);
            }

            if (foodChanged)
            {
                state.LastFood = food;
                CuiHelper.DestroyUi(player, FoodBarPanel);
                DrawVitalsBar(player, "🍖 FOOD", food, 500f, FoodBarPanel, "0.09 0.28", config.Colors.FoodBar);
            }
        }

        private void DrawVitalsBar(BasePlayer player, string label, float value, float max, string panelId, string verticalRange, string colorHex)
        {
            var container = new CuiElementContainer();

            float ratio = Mathf.Clamp01(value / max);
            string split = verticalRange;

            // Sub-container panel
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = $"0.05 {split.Split(' ')[0]}", AnchorMax = $"0.95 {split.Split(' ')[1]}" },
                Image = { Color = "0 0 0 0" }
            }, VitalsContainer, panelId);

            // Bar background track
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.35" },
                Image = { Color = "1 1 1 0.05" }
            }, panelId);

            // Bar fill
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = $"{ratio:F2} 0.35" },
                Image = { Color = colorHex }
            }, panelId);

            // Label left
            container.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 10, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleLeft, Color = "1 1 1 0.7" },
                RectTransform = { AnchorMin = "0 0.45", AnchorMax = "0.5 1.0" }
            }, panelId);

            // Value right
            container.Add(new CuiLabel
            {
                Text = { Text = $"{Mathf.RoundToInt(value)} / {Mathf.RoundToInt(max)}", FontSize = 10, Font = "robotocondensed-regular.ttf", Align = TextAnchor.MiddleRight, Color = "1 1 1 0.8" },
                RectTransform = { AnchorMin = "0.5 0.45", AnchorMax = "1.0 1.0" }
            }, panelId);

            CuiHelper.AddUi(player, container);
        }

        private void UpdateStatusStrip(BasePlayer player)
        {
            var state = GetState(player);
            
            float temp = player.metabolism.temperature.value;
            float wet = player.metabolism.wetness.value;
            float rad = player.metabolism.radiation_level.value;
            bool bleeding = player.metabolism.bleeding.value > 0.0f;
            bool safe = player.InSafeZone();
            bool blocked = player.IsBuildingBlocked();

            // Quick dirty checks
            if (Mathf.Abs(state.LastTemp - temp) < 0.5f &&
                Mathf.Abs(state.LastWet - wet) < 0.5f &&
                Mathf.Abs(state.LastRad - rad) < 0.5f &&
                state.WasBleeding == bleeding &&
                state.WasSafe == safe &&
                state.WasBuildingBlocked == blocked)
            {
                return;
            }

            state.LastTemp = temp;
            state.LastWet = wet;
            state.LastRad = rad;
            state.WasBleeding = bleeding;
            state.WasSafe = safe;
            state.WasBuildingBlocked = blocked;

            CuiHelper.DestroyUi(player, StatusStripPanel);

            var container = new CuiElementContainer();

            // Status strip located below vitals card
            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.782 0.01", AnchorMax = "0.998 0.051" },
                Image = { Color = "0 0 0 0.4", Material = ColorBlurMat }
            }, "Hud", StatusStripPanel);

            // Small purple top line
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" },
                Image = { Color = config.Colors.PrimaryAccent }
            }, StatusStripPanel);

            // Compile the list of active statuses
            List<string> items = new List<string>();

            // Temperature always shown
            string tempColor = "1 1 1 0.7";
            if (temp < 5f) tempColor = "0.2 0.6 1.0 0.9"; // Cold
            else if (temp > 40f) tempColor = "1.0 0.4 0.1 0.9"; // Hot
            items.Add($"<color={tempColor}>❄ {Mathf.RoundToInt(temp)}°C</color>");

            // Wetness if > 5%
            if (wet > 5f)
            {
                items.Add($"<color=0.2 0.6 1.0 0.9>💦 {Mathf.RoundToInt(wet)}%</color>");
            }

            // Radiation if > 0
            if (rad > 0f)
            {
                items.Add($"<color=0.8 0.2 0.8 0.9>☢ {Mathf.RoundToInt(rad)}</color>");
            }

            // Bleeding
            if (bleeding)
            {
                items.Add("<color=0.8 0.1 0.1 1.0>🩸 BLEEDING</color>");
            }

            // SafeZone
            if (safe)
            {
                items.Add("<color=0.1 0.8 0.4 1.0>🛡 SAFE</color>");
            }

            // Building Blocked
            if (blocked)
            {
                items.Add("<color=0.8 0.2 0.2 1.0>⛔ BLOCKED</color>");
            }

            string combinedText = string.Join("  |  ", items);

            container.Add(new CuiLabel
            {
                Text = { Text = combinedText, FontSize = 9, Font = "robotocondensed-bold.ttf", Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.8" },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
            }, StatusStripPanel);

            CuiHelper.AddUi(player, container);
        }

        private void UpdateNightIndicator(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, NightIndicatorPanel);

            // Only show night indicator if it is night time (between 18:30 and 6:30 roughly)
            float hour = TOD_Sky.Instance != null ? TOD_Sky.Instance.Cycle.Hour : 12f;
            bool isNight = hour > 19f || hour < 5f;

            if (!isNight) return;

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.002 0.895", AnchorMax = "0.092 0.941" },
                Image = { Color = "0 0 0 0.4", Material = ColorBlurMat }
            }, "Hud", NightIndicatorPanel);

            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" },
                Image = { Color = config.Colors.PrimaryAccent }
            }, NightIndicatorPanel);

            // Compute roughly time remaining until sunrise (5:00 AM)
            float timeRem = 0f;
            if (hour >= 19f) timeRem = (24f - hour) + 5f;
            else timeRem = 5f - hour;

            int minutesRem = Mathf.RoundToInt(timeRem * 60f);
            int h = minutesRem / 60;
            int m = minutesRem % 60;
            string timeString = $"{h}h {m}m";

            container.Add(new CuiLabel
            {
                Text = { Text = $"🌙 {timeString}", FontSize = 10, Font = "robotocondensed-regular.ttf", Align = TextAnchor.MiddleLeft, Color = "1 1 1 0.7" },
                RectTransform = { AnchorMin = "0.1 0", AnchorMax = "0.95 1" }
            }, NightIndicatorPanel);

            CuiHelper.AddUi(player, container);
        }

        private void StartLowHealthPulse(BasePlayer player)
        {
            var state = GetState(player);
            if (!state.IsLowHealthPulseActive) return;

            // Low health overlay / pulse visual check. We can create a pulsing effect via quick timer recursion
            // Or just a soft red vignette screen overlay that toggle fades
            // To keep CPU usage ultra low, let's keep it simple. Red bar flash/vignette.
        }
        #endregion

        #region Chat Commands
        [ChatCommand("hud")]
        private void CmdHud(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (args.Length == 0)
            {
                // Toggle HUD
                var state = GetState(player);
                if (state.LastHealth != -1f)
                {
                    DestroyAll(player);
                    state.LastHealth = -1f; // reset marker so it doesn't rebuild until turned back on
                    SendReply(player, "Project Rift HUD has been disabled.");
                }
                else
                {
                    BuildAll(player);
                    SendReply(player, "Project Rift HUD has been enabled.");
                }
                return;
            }

            string sub = args[0].ToLower();
            if (sub == "reload" && permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                LoadConfig();
                foreach (var p in BasePlayer.activePlayerList)
                {
                    if (p != null) BuildAll(p);
                }
                SendReply(player, "Project Rift HUD configuration reloaded and UI refreshed.");
            }
            else if (sub == "reset")
            {
                BuildAll(player);
                SendReply(player, "Project Rift HUD has been reset.");
            }
            else if (sub == "test" && permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                AddNotification(player, "This is a test notification!", "info", 5f);
                ShowEvent(player, "Bradley Active", 5f);
                SendReply(player, "Test notification and event triggered.");
            }
        }
        #endregion

        #region Public API
        public void API_AddNotification(BasePlayer player, string message, string type = "info", float duration = 5f)
        {
            AddNotification(player, message, type, duration);
        }

        public void API_ShowEvent(BasePlayer player, string eventName, float duration = 8f)
        {
            ShowEvent(player, eventName, duration);
        }
        #endregion
    }
}

