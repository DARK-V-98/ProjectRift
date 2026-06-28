// ============================================================================
//  Project Rift Core  —  Carbon / Oxide plugin for Rust
//  Author: ESYSTEMLK   |   https://projectrift.esystemlk.com
//
//  What it does:
//    1. Sends a live "heartbeat" (player count, max, hostname) to the website
//       API so the site + /loading screen show REAL server data.
//    2. Sets the native Rust loading-screen header image + website URL.
//    3. Shows an in-game CUI welcome/loading overlay when players connect.
//    4. Adds /discord, /website and /loading chat commands.
//
//  NOTE: Rust cannot embed a live webpage inside its native loading screen.
//  The web-based loading screen lives at <website>/loading (browser/overlay).
//  This plugin provides the in-game equivalent (CUI) + feeds live data.
//
//  Drop this file in:  carbon/plugins/   (or oxide/plugins/)
//  Then edit the config:  carbon/configs/ProjectRiftCore.json
// ============================================================================
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ProjectRiftCore", "ESYSTEMLK", "1.0.0")]
    [Description("Live server heartbeat + in-game welcome/loading screen for Project Rift.")]
    public class ProjectRiftCore : RustPlugin
    {
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

            [JsonProperty("In-game welcome overlay background image URL")]
            public string WelcomeImageUrl = "https://projectrift.esystemlk.com/bgmobile.png";

            [JsonProperty("Heartbeat interval (seconds)")]
            public float HeartbeatInterval = 30f;

            [JsonProperty("Show in-game welcome overlay on connect")]
            public bool ShowWelcomeScreen = true;

            [JsonProperty("Welcome overlay auto-close (seconds, 0 = manual)")]
            public float WelcomeDuration = 12f;

            [JsonProperty("Loading tips (one is shown at random)")]
            public string[] Tips =
            {
                "Protect your Tool Cupboard — it controls your whole base.",
                "Raid weekends are dangerous. Upgrade to stone before Friday.",
                "Join the Discord for free starter kits and giveaways.",
                "Recyclers turn junk into scrap. Recycle before you log off.",
                "Teamwork wins raids. Find squads in the Discord #lfg channel."
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

        private const string UiName = "ProjectRift.Welcome";

        #region Lifecycle
        private void OnServerInitialized()
        {
            Puts("Project Rift Core loaded — heartbeat every " + config.HeartbeatInterval + "s.");

            // Customise the native Rust loading screen.
            if (!string.IsNullOrEmpty(config.HeaderImageUrl))
                ConVar.Server.headerimage = config.HeaderImageUrl;
            if (!string.IsNullOrEmpty(config.WebsiteUrl))
                ConVar.Server.url = config.WebsiteUrl;

            // Start the live-stats heartbeat. (Oxide/Carbon auto-cancels this
            // plugin's timers on unload, so no manual cleanup is needed.)
            SendHeartbeat();
            timer.Every(config.HeartbeatInterval, SendHeartbeat);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(p, UiName);
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

            webrequest.Enqueue(
                config.ApiUrl,
                JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"Heartbeat failed (HTTP {code}). Check ApiUrl / ApiKey.");
                },
                this, RequestMethod.POST, headers, 8f);
        }
        #endregion

        #region In-game welcome overlay (CUI)
        private void OnPlayerConnected(BasePlayer player)
        {
            if (!config.ShowWelcomeScreen || player == null) return;
            // brief delay so the player is fully connected
            timer.Once(2f, () =>
            {
                if (player != null && player.IsConnected) ShowWelcome(player);
            });
        }

        private void ShowWelcome(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiName);

            string tip = config.Tips.Length > 0
                ? config.Tips[UnityEngine.Random.Range(0, config.Tips.Length)]
                : string.Empty;
            int players = BasePlayer.activePlayerList.Count;
            int max = ConVar.Server.maxplayers;

            var c = new CuiElementContainer();

            string root = c.Add(new CuiPanel
            {
                Image = { Color = "0.02 0.03 0.05 0.97" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UiName);

            if (!string.IsNullOrEmpty(config.WelcomeImageUrl))
            {
                c.Add(new CuiElement
                {
                    Parent = root,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.WelcomeImageUrl, Color = "1 1 1 0.35" },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });
            }

            c.Add(new CuiLabel
            {
                Text = { Text = "PROJECT RIFT", FontSize = 54, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0.62", AnchorMax = "1 0.74" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = "SURVIVE  ·  BUILD  ·  RAID  ·  DOMINATE", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "0.69 0.15 1 1" },
                RectTransform = { AnchorMin = "0 0.555", AnchorMax = "1 0.61" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = $"{players} / {max} players online", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "0 0.9 1 1" },
                RectTransform = { AnchorMin = "0 0.46", AnchorMax = "1 0.52" }
            }, root);

            c.Add(new CuiLabel
            {
                Text = { Text = "TIP:  " + tip, FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.7 0.74 0.85 1" },
                RectTransform = { AnchorMin = "0.12 0.38", AnchorMax = "0.88 0.44" }
            }, root);

            c.Add(new CuiButton
            {
                Button = { Color = "0.69 0.15 1 1", Command = "projectrift.close" },
                Text = { Text = "ENTER", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.42 0.26", AnchorMax = "0.58 0.315" }
            }, root);

            CuiHelper.AddUi(player, c);

            if (config.WelcomeDuration > 0f)
                timer.Once(config.WelcomeDuration, () =>
                {
                    if (player != null) CuiHelper.DestroyUi(player, UiName);
                });
        }

        [ConsoleCommand("projectrift.close")]
        private void CmdCloseUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, UiName);
        }
        #endregion

        #region Chat commands
        [ChatCommand("discord")]
        private void CmdDiscord(BasePlayer player) =>
            player.ChatMessage($"<color=#00e5ff>Join our Discord:</color> {config.DiscordUrl}");

        [ChatCommand("website")]
        private void CmdWebsite(BasePlayer player) =>
            player.ChatMessage($"<color=#00e5ff>Project Rift:</color> {config.WebsiteUrl}");

        [ChatCommand("loading")]
        private void CmdLoading(BasePlayer player) => ShowWelcome(player);
        #endregion
    }
}
