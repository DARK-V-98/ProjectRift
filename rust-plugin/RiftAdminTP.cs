// RiftAdminTP — admin teleport tool for PROJECT RIFT.
//
// In-game UI to see every online player (name + map grid + distance + HP) and:
//   • TP TO   — teleport yourself to that player
//   • BRING   — teleport that player to you
//
// Open:  /tpui   (or bind a key:  bind keypad7 riftadmintp.toggle)
// Chat:  /tpto <name|id>   /bring <name|id>
//
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RiftAdminTP", "ESYSTEMLK", "1.0.0")]
    [Description("Admin teleport tool — TP to players, bring players, with a live player list (name, grid, distance).")]
    public class RiftAdminTP : RustPlugin
    {
        private const string PermUse = "riftadmintp.use";
        private const string UiRoot = "riftadmintp";

        [PluginReference] private Plugin ImageLibrary;

        private Configuration config;
        private readonly Dictionary<ulong, int> page = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> open = new HashSet<ulong>();
        private readonly Dictionary<ulong, ulong> selected = new Dictionary<ulong, ulong>(); // admin -> reviewed player
        private readonly HashSet<ulong> god = new HashSet<ulong>();                            // players in godmode

        private const int PerPage = 9;
        private const string ColPanel = "0.05 0.06 0.10 0.99";
        private const string ColRow = "1 1 1 0.04";
        private const string ColAccent = "0.69 0.15 1 1";
        private const string ColCyan = "0 0.9 1 1";
        private const string ColGreen = "0.17 0.85 0.4 0.95";
        private const string ColBlue = "0.2 0.5 0.95 0.95";
        private const string ColRed = "0.85 0.22 0.3 0.95";
        private const string ColAmber = "0.95 0.62 0.15 0.95";

        public class Configuration
        {
            public string LogoUrl = "https://projectrift.esystemlk.com/logo.png";
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

        private void Init() => permission.RegisterPermission(PermUse, this);

        private void OnServerInitialized()
        {
            if (ImageLibrary != null && ImageLibrary.IsLoaded && !string.IsNullOrEmpty(config.LogoUrl))
                ImageLibrary.Call("AddImage", config.LogoUrl, "rifttp_logo", 0UL);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, UiRoot);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            page.Remove(player.userID);
            open.Remove(player.userID);
            selected.Remove(player.userID);
        }

        private bool HasPerm(BasePlayer p) =>
            p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermUse));

        // ---- chat commands --------------------------------------------------
        [ChatCommand("tpui")]
        private void CmdTpUi(BasePlayer player, string cmd, string[] args)
        {
            if (!HasPerm(player)) { player.ChatMessage("You don't have permission."); return; }
            BuildMenu(player);
        }

        [ChatCommand("tpto")]
        private void CmdTpTo(BasePlayer player, string cmd, string[] args)
        {
            if (!HasPerm(player) || args.Length == 0) return;
            var t = FindPlayer(args[0]);
            if (t == null) { player.ChatMessage("Player not found."); return; }
            Teleport(player, t.transform.position);
            player.ChatMessage($"Teleported to {t.displayName}.");
        }

        [ChatCommand("bring")]
        private void CmdBring(BasePlayer player, string cmd, string[] args)
        {
            if (!HasPerm(player) || args.Length == 0) return;
            var t = FindPlayer(args[0]);
            if (t == null) { player.ChatMessage("Player not found."); return; }
            Teleport(t, player.transform.position);
            player.ChatMessage($"Brought {t.displayName} to you.");
        }

        // ---- UI -------------------------------------------------------------
        private List<BasePlayer> Others(BasePlayer admin) =>
            BasePlayer.activePlayerList
                .Where(p => p != null && p != admin && !p.IsNpc)
                .OrderBy(p => Vector3.Distance(admin.transform.position, p.transform.position))
                .ToList();

        // dispatcher: show the review card if a player is selected, else the list
        private void BuildMenu(BasePlayer admin)
        {
            if (selected.TryGetValue(admin.userID, out var sel))
            {
                var t = BasePlayer.FindByID(sel) ?? BasePlayer.FindSleeping(sel);
                if (t != null) { BuildDetail(admin, t); return; }
                selected.Remove(admin.userID);
            }
            BuildList(admin);
        }

        private void Header(CuiElementContainer c, string title, int count, string countLabel)
        {
            c.Add(new CuiPanel { Image = { Color = ColAccent, FadeIn = 0.3f },
                RectTransform = { AnchorMin = "0 0.972", AnchorMax = "1 0.978" } }, UiRoot);

            string logoPng = ImageLibrary?.Call("GetImage", "rifttp_logo", 0UL) as string;
            var logo = new CuiRawImageComponent { Color = "1 1 1 1", FadeIn = 0.4f };
            if (!string.IsNullOrEmpty(logoPng)) logo.Png = logoPng; else logo.Url = config.LogoUrl;
            c.Add(new CuiElement { Parent = UiRoot,
                Components = { logo, new CuiRectTransformComponent { AnchorMin = "0.03 0.94", AnchorMax = "0.1 0.99" } } });

            Label(c, UiRoot, title, 15, "0.12 0.94", "0.7 0.99", TextAnchor.MiddleLeft, ColAccent);
            if (count >= 0) Label(c, UiRoot, $"{count} {countLabel}", 11, "0.55 0.94", "0.86 0.99", TextAnchor.MiddleRight, ColCyan);
            c.Add(new CuiButton
            {
                Button = { Command = "riftadmintp.close", Color = "0.8 0.2 0.3 0.9" },
                Text = { Text = "X", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.9 0.94", AnchorMax = "0.97 0.99" }
            }, UiRoot);
        }

        private void BuildList(BasePlayer admin)
        {
            var list = Others(admin);
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            if (!page.TryGetValue(admin.userID, out var pg)) pg = 0;
            pg = Mathf.Clamp(pg, 0, pages - 1);
            page[admin.userID] = pg;

            var c = new CuiElementContainer();
            CuiHelper.DestroyUi(admin, UiRoot);
            c.Add(new CuiPanel
            {
                Image = { Color = ColPanel, Material = "assets/content/ui/uibackgroundblur.mat", FadeIn = 0.35f },
                RectTransform = { AnchorMin = "0.3 0.16", AnchorMax = "0.7 0.86" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            Header(c, "RIFT ADMIN · PLAYERS", list.Count, "online");

            float top = 0.92f, rh = (top - 0.08f) / PerPage;
            int start = pg * PerPage;
            for (int i = 0; i < PerPage && start + i < list.Count; i++)
            {
                var t = list[start + i];
                float y1 = top - i * rh, y0 = y1 - rh + 0.006f;
                AddRow(c, admin, t, y0, y1);
            }

            c.Add(new CuiButton
            {
                Button = { Command = "riftadmintp.refresh", Color = ColRow },
                Text = { Text = "REFRESH", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColCyan },
                RectTransform = { AnchorMin = "0.03 0.02", AnchorMax = "0.2 0.07" }
            }, UiRoot);
            c.Add(new CuiButton
            {
                Button = { Command = $"riftadmintp.page {pg - 1}", Color = ColRow },
                Text = { Text = "< PREV", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.8 0.84 0.95 1" },
                RectTransform = { AnchorMin = "0.55 0.02", AnchorMax = "0.68 0.07" }
            }, UiRoot);
            Label(c, UiRoot, $"PAGE {pg + 1}/{pages}", 11, "0.68 0.02", "0.82 0.07", TextAnchor.MiddleCenter, ColCyan);
            c.Add(new CuiButton
            {
                Button = { Command = $"riftadmintp.page {pg + 1}", Color = ColRow },
                Text = { Text = "NEXT >", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.8 0.84 0.95 1" },
                RectTransform = { AnchorMin = "0.82 0.02", AnchorMax = "0.97 0.07" }
            }, UiRoot);

            CuiHelper.AddUi(admin, c);
            open.Add(admin.userID);
        }

        private void AddRow(CuiElementContainer c, BasePlayer admin, BasePlayer t, float y0, float y1)
        {
            string row = $"{UiRoot}.row.{t.userID}";
            c.Add(new CuiPanel { Image = { Color = ColRow, FadeIn = 0.3f },
                RectTransform = { AnchorMin = $"0.03 {y0}", AnchorMax = $"0.97 {y1}" } }, UiRoot, row);

            float dist = Vector3.Distance(admin.transform.position, t.transform.position);
            string grid = MapGrid(t.transform.position);
            int hp = Mathf.RoundToInt(t.health);
            bool sleeping = t.IsSleeping();
            bool godded = god.Contains(t.userID);

            Label(c, row, t.displayName + (godded ? "  <color=#ffd24a>[GOD]</color>" : ""), 13, "0.02 0.5", "0.55 1", TextAnchor.LowerLeft, "0.92 0.94 1 1");
            string sub = $"<color=#00e5ff>{grid}</color>  ·  {dist:0}m  ·  {hp}hp{(sleeping ? "  ·  <color=#ffb020>sleeping</color>" : "")}";
            Label(c, row, sub, 10, "0.02 0", "0.55 0.5", TextAnchor.UpperLeft, "0.7 0.74 0.85 1");

            // REVIEW (opens the action card)
            c.Add(new CuiButton
            {
                Button = { Command = $"riftadmintp.sel {t.userID}", Color = "0.69 0.15 1 0.85" },
                Text = { Text = "REVIEW", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.56 0.18", AnchorMax = "0.69 0.82" }
            }, row);
            c.Add(new CuiButton
            {
                Button = { Command = $"riftadmintp.to {t.userID}", Color = ColGreen },
                Text = { Text = "TP", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.705 0.18", AnchorMax = "0.83 0.82" }
            }, row);
            c.Add(new CuiButton
            {
                Button = { Command = $"riftadmintp.bring {t.userID}", Color = ColBlue },
                Text = { Text = "BRING", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.845 0.18", AnchorMax = "0.98 0.82" }
            }, row);
        }

        // ---- the animated review / action card ------------------------------
        private void BuildDetail(BasePlayer admin, BasePlayer t)
        {
            var c = new CuiElementContainer();
            CuiHelper.DestroyUi(admin, UiRoot);
            c.Add(new CuiPanel
            {
                Image = { Color = ColPanel, Material = "assets/content/ui/uibackgroundblur.mat", FadeIn = 0.4f },
                RectTransform = { AnchorMin = "0.3 0.16", AnchorMax = "0.7 0.86" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            Header(c, "RIFT ADMIN · REVIEW", -1, "");

            // name + god tag
            bool godded = god.Contains(t.userID);
            Label(c, UiRoot, t.displayName + (godded ? "  <color=#ffd24a>[GOD]</color>" : ""), 18, "0.05 0.86", "0.95 0.92", TextAnchor.MiddleLeft, "0.95 0.96 1 1");

            // review stats block
            var m = t.metabolism;
            int ping = t.Connection != null ? Network.Net.sv.GetAveragePing(t.Connection) : 0;
            string activeItem = t.GetActiveItem()?.info?.displayName?.english ?? "—";
            string[] rows =
            {
                $"SteamID|{t.UserIDString}",
                $"Grid|<color=#00e5ff>{MapGrid(t.transform.position)}</color>   ({Vector3.Distance(admin.transform.position, t.transform.position):0}m away)",
                $"Health|{Mathf.RoundToInt(t.health)} / {Mathf.RoundToInt(t.MaxHealth())}",
                $"Hydration|{Mathf.RoundToInt(m.hydration.value)}",
                $"Calories|{Mathf.RoundToInt(m.calories.value)}",
                $"Radiation|{Mathf.RoundToInt(m.radiation_poison.value)}",
                $"Bleeding|{Mathf.RoundToInt(m.bleeding.value)}",
                $"Ping|{ping} ms",
                $"Holding|{activeItem}",
                $"State|{(t.IsDead() ? "DEAD" : t.IsWounded() ? "WOUNDED" : t.IsSleeping() ? "SLEEPING" : "AWAKE")}",
            };
            float ry = 0.84f, rh2 = 0.045f;
            for (int i = 0; i < rows.Length; i++)
            {
                var parts = rows[i].Split('|');
                float y1 = ry - i * rh2, y0 = y1 - rh2 + 0.004f;
                Label(c, UiRoot, parts[0], 11, "0.06 " + y0, "0.32 " + y1, TextAnchor.MiddleLeft, "0.6 0.65 0.8 1");
                Label(c, UiRoot, parts[1], 11, "0.33 " + y0, "0.95 " + y1, TextAnchor.MiddleLeft, "0.9 0.92 1 1");
            }

            // action buttons (2 rows of 4) with fade-in
            string id = t.userID.ToString();
            ActBtn(c, "HEAL", ColGreen, $"riftadmintp.heal {id}", 0, 0);
            ActBtn(c, "FEED", ColCyan, $"riftadmintp.feed {id}", 1, 0);
            ActBtn(c, "REVIVE", ColAmber, $"riftadmintp.revive {id}", 2, 0);
            ActBtn(c, godded ? "GOD: ON" : "GOD: OFF", godded ? "0.85 0.7 0.2 0.95" : ColRow, $"riftadmintp.god {id}", 3, 0);
            ActBtn(c, "TP TO", ColGreen, $"riftadmintp.to {id}", 0, 1);
            ActBtn(c, "BRING", ColBlue, $"riftadmintp.bring {id}", 1, 1);
            ActBtn(c, "STRIP", "0.5 0.4 0.2 0.95", $"riftadmintp.strip {id}", 2, 1);
            ActBtn(c, "KILL", ColRed, $"riftadmintp.kill {id}", 3, 1);

            // back
            c.Add(new CuiButton
            {
                Button = { Command = "riftadmintp.back", Color = ColRow },
                Text = { Text = "< BACK", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.8 0.84 0.95 1" },
                RectTransform = { AnchorMin = "0.03 0.02", AnchorMax = "0.22 0.07" }
            }, UiRoot);

            CuiHelper.AddUi(admin, c);
            open.Add(admin.userID);
        }

        private void ActBtn(CuiElementContainer c, string text, string color, string cmd, int col, int rowIdx)
        {
            float w = 0.225f, gap = 0.013f, x0 = 0.05f + col * (w + gap);
            float y1 = rowIdx == 0 ? 0.27f : 0.165f, y0 = y1 - 0.085f;
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = color, FadeIn = 0.3f },
                Text = { Text = text, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FadeIn = 0.3f },
                RectTransform = { AnchorMin = $"{x0} {y0}", AnchorMax = $"{x0 + w} {y1}" }
            }, UiRoot);
        }

        private void Label(CuiElementContainer c, string parent, string text, int size,
                           string min, string max, TextAnchor align, string color)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        // ---- console commands ----------------------------------------------
        private bool Guard(ConsoleSystem.Arg arg, out BasePlayer player)
        {
            player = arg.Player();
            return player != null && HasPerm(player);
        }

        [ConsoleCommand("riftadmintp.toggle")]
        private void CcToggle(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            if (open.Contains(p.userID)) { CuiHelper.DestroyUi(p, UiRoot); open.Remove(p.userID); }
            else BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            CuiHelper.DestroyUi(p, UiRoot);
            open.Remove(p.userID);
        }

        [ConsoleCommand("riftadmintp.refresh")]
        private void CcRefresh(ConsoleSystem.Arg arg)
        {
            if (Guard(arg, out var p)) BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.page")]
        private void CcPage(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            page[p.userID] = arg.GetInt(0, 0);
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.to")]
        private void CcTo(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = BasePlayer.FindByID(arg.GetULong(0, 0));
            if (t == null) return;
            Teleport(p, t.transform.position);
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.bring")]
        private void CcBring(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = BasePlayer.FindByID(arg.GetULong(0, 0)) ?? BasePlayer.FindSleeping(arg.GetULong(0, 0));
            if (t == null) return;
            Teleport(t, p.transform.position);
            BuildMenu(p);
        }

        // open the review card for a player
        [ConsoleCommand("riftadmintp.sel")]
        private void CcSel(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            selected[p.userID] = arg.GetULong(0, 0);
            BuildMenu(p);
        }

        // back to the list
        [ConsoleCommand("riftadmintp.back")]
        private void CcBack(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            selected.Remove(p.userID);
            BuildMenu(p);
        }

        private BasePlayer Target(ConsoleSystem.Arg arg) =>
            BasePlayer.FindByID(arg.GetULong(0, 0)) ?? BasePlayer.FindSleeping(arg.GetULong(0, 0));

        [ConsoleCommand("riftadmintp.heal")]
        private void CcHeal(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = Target(arg); if (t == null) return;
            if (t.IsWounded()) t.StopWounded();
            t.health = t.MaxHealth();
            t.metabolism.bleeding.value = 0;
            t.SendNetworkUpdate();
            p.ChatMessage($"Healed {t.displayName} to full.");
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.feed")]
        private void CcFeed(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = Target(arg); if (t == null) return;
            var m = t.metabolism;
            m.calories.value = m.calories.max;
            m.hydration.value = m.hydration.max;
            m.radiation_poison.value = 0;
            m.bleeding.value = 0;
            m.SendChangesToClient();
            p.ChatMessage($"Fed {t.displayName} (food + water topped up).");
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.revive")]
        private void CcRevive(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = Target(arg); if (t == null) return;
            if (t.IsWounded()) t.RecoverFromWounded();
            t.health = t.MaxHealth();
            var m = t.metabolism;
            m.calories.value = m.calories.max;
            m.hydration.value = m.hydration.max;
            m.bleeding.value = 0; m.radiation_poison.value = 0;
            m.SendChangesToClient();
            t.SendNetworkUpdate();
            p.ChatMessage($"Revived {t.displayName}.");
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.god")]
        private void CcGod(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = Target(arg); if (t == null) return;
            if (god.Contains(t.userID)) { god.Remove(t.userID); p.ChatMessage($"Godmode OFF for {t.displayName}."); }
            else { god.Add(t.userID); t.health = t.MaxHealth(); t.SendNetworkUpdate(); p.ChatMessage($"Godmode ON for {t.displayName}."); }
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.strip")]
        private void CcStrip(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = Target(arg); if (t == null) return;
            t.inventory.Strip();
            p.ChatMessage($"Stripped {t.displayName}'s inventory.");
            BuildMenu(p);
        }

        [ConsoleCommand("riftadmintp.kill")]
        private void CcKill(ConsoleSystem.Arg arg)
        {
            if (!Guard(arg, out var p)) return;
            var t = Target(arg); if (t == null) return;
            string n = t.displayName;
            t.Die();
            p.ChatMessage($"Killed {n}.");
            selected.Remove(p.userID);
            BuildMenu(p);
        }

        // godmode enforcement
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is BasePlayer pl && god.Contains(pl.userID))
            {
                info?.damageTypes?.ScaleAll(0f);
                return true;
            }
            return null;
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            // keep godded players topped up after respawn
            if (player != null && god.Contains(player.userID)) { player.health = player.MaxHealth(); player.SendNetworkUpdate(); }
        }

        // ---- helpers --------------------------------------------------------
        private void Teleport(BasePlayer player, Vector3 destination)
        {
            if (player == null) return;
            var mounted = player.GetMounted();
            if (mounted != null) mounted.DismountPlayer(player, true);
            if (player.HasParent()) player.SetParent(null, true, true);
            player.Teleport(destination);
            player.ForceUpdateTriggers();
            if (player.IsConnected) player.SendNetworkUpdateImmediate();
        }

        private BasePlayer FindPlayer(string token)
        {
            if (ulong.TryParse(token, out var id))
            {
                var byId = BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);
                if (byId != null) return byId;
            }
            BasePlayer match = null;
            foreach (var p in BasePlayer.activePlayerList)
                if (p.displayName != null && p.displayName.ToLower().Contains(token.ToLower()))
                {
                    if (match != null) return null;
                    match = p;
                }
            return match;
        }

        // world position → Rust map grid (e.g. "G12")
        private string MapGrid(Vector3 pos)
        {
            float worldSize = TerrainMeta.Size.x;
            const float cell = 146.3f;
            int max = Mathf.Max(1, Mathf.FloorToInt(worldSize / cell));
            float half = worldSize / 2f;
            int col = Mathf.Clamp(Mathf.FloorToInt((pos.x + half) / cell), 0, max);
            int rowNum = Mathf.Clamp(max - Mathf.FloorToInt((pos.z + half) / cell), 0, max);
            return $"{ColLetter(col)}{rowNum}";
        }

        private string ColLetter(int n)
        {
            string s = "";
            n++;
            while (n > 0) { int r = (n - 1) % 26; s = (char)('A' + r) + s; n = (n - 1) / 26; }
            return s;
        }
    }
}
