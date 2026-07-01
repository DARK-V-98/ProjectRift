// RiftAdminGive — silent admin item spawner for PROJECT RIFT.
//
// Gives items straight to your inventory through the plugin's GiveItem path,
// which (unlike the F1 give menu / inventory.give console commands) does NOT
// broadcast the "SERVER X gave themselves ..." chat notice and does NOT tag
// your name on the item. Admin-only.
//
//   /i <item> [amount] [skinId]      (alias: /give)
//   /igive <steamid|name> <item> [amount]   give to another player, silently
//   console:  riftgive <item> [amount]
//
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RiftAdminGive", "ESYSTEMLK", "1.0.0")]
    [Description("Silent admin item spawner — no server-wide give message, no name tag. Includes an in-game item UI.")]
    public class RiftAdminGive : RustPlugin
    {
        private const string PermUse = "riftadmingive.use";

        [PluginReference] private Plugin ImageLibrary;

        private Configuration config;

        // cached once so opening / paging the menu never re-queries + re-sorts.
        private List<ItemDefinition> allItems;
        private List<KeyValuePair<int, string>> categories;

        public class Configuration
        {
            public string LogoUrl = "https://projectrift.esystemlk.com/logo.png";
            public int DefaultQty = 100;
            public bool RequireConfirm = true;   // ask Confirm/Cancel before giving
            public float ClearDropRadius = 12f;  // radius for the "clear drops" button
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

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
        }

        private void OnServerInitialized()
        {
            BuildCaches();
            if (ImageLibrary != null && ImageLibrary.IsLoaded && !string.IsNullOrEmpty(config.LogoUrl))
                ImageLibrary.Call("AddImage", config.LogoUrl, "riftgive_logo", 0UL);
        }

        private void BuildCaches()
        {
            allItems = ItemManager.GetItemDefinitions().OrderBy(d => d.displayName.english).ToList();
            categories = new List<KeyValuePair<int, string>> { new KeyValuePair<int, string>(-1, "ALL") };
            foreach (ItemCategory cat in Enum.GetValues(typeof(ItemCategory)))
                categories.Add(new KeyValuePair<int, string>((int)cat, cat.ToString()));
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, UiRoot);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null) menu.Remove(player.userID);
        }

        private bool HasPerm(BasePlayer p) =>
            p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermUse));

        // ---- /i  and  /give -------------------------------------------------
        [ChatCommand("i")]
        private void CmdI(BasePlayer player, string command, string[] args) => DoGive(player, args);

        [ChatCommand("give")]
        private void CmdGive(BasePlayer player, string command, string[] args) => DoGive(player, args);

        private void DoGive(BasePlayer player, string[] args)
        {
            if (!HasPerm(player)) { player.ChatMessage("You don't have permission."); return; }
            if (args == null || args.Length == 0)
            {
                player.ChatMessage("Usage: /i <item shortname or id> [amount] [skinId]");
                return;
            }

            var def = ResolveItem(args[0]);
            if (def == null) { player.ChatMessage($"Unknown item: <color=#ff5470>{args[0]}</color>"); return; }

            int amount = args.Length > 1 && int.TryParse(args[1], out var a) ? Mathf.Max(1, a) : 1;
            ulong skin = args.Length > 2 && ulong.TryParse(args[2], out var s) ? s : 0UL;

            GiveSilently(player, def, amount, skin);
            player.ChatMessage($"<color=#B026FF>+{amount}</color> {def.displayName.english}");
        }

        // ---- /igive <target> <item> [amount] --------------------------------
        [ChatCommand("igive")]
        private void CmdIGive(BasePlayer player, string command, string[] args)
        {
            if (!HasPerm(player)) { player.ChatMessage("You don't have permission."); return; }
            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /igive <steamid|name> <item> [amount] [skinId]");
                return;
            }

            var target = FindPlayer(args[0]);
            if (target == null) { player.ChatMessage($"Player not found: {args[0]}"); return; }

            var def = ResolveItem(args[1]);
            if (def == null) { player.ChatMessage($"Unknown item: {args[1]}"); return; }

            int amount = args.Length > 2 && int.TryParse(args[2], out var a) ? Mathf.Max(1, a) : 1;
            ulong skin = args.Length > 3 && ulong.TryParse(args[3], out var s) ? s : 0UL;

            GiveSilently(target, def, amount, skin);
            player.ChatMessage($"Gave <color=#B026FF>{amount}</color> {def.displayName.english} to {target.displayName}");
        }

        // ---- console: riftgive <item> [amount] ------------------------------
        [ConsoleCommand("riftgive")]
        private void CcGive(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !HasPerm(player)) return; // server console (player==null) allowed
            if (player == null) { arg.ReplyWith("Run this in-game (needs a player inventory)."); return; }

            var def = ResolveItem(arg.GetString(0, ""));
            if (def == null) { arg.ReplyWith("Unknown item."); return; }
            int amount = Mathf.Max(1, arg.GetInt(1, 1));
            GiveSilently(player, def, amount, 0UL);
            arg.ReplyWith($"Gave {amount} x {def.displayName.english}");
        }

        // ---- helpers --------------------------------------------------------
        private ItemDefinition ResolveItem(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var def = ItemManager.FindItemDefinition(token);
            if (def == null && int.TryParse(token, out var id)) def = ItemManager.FindItemDefinition(id);
            return def;
        }

        // Splits into stacks and gives via the inventory — no broadcast, no name tag.
        private void GiveSilently(BasePlayer player, ItemDefinition def, int amount, ulong skin)
        {
            int max = def.stackable > 0 ? def.stackable : amount;
            int remaining = amount;
            while (remaining > 0)
            {
                int give = Mathf.Min(remaining, max);
                var item = ItemManager.Create(def, give, skin);
                if (item == null) break;
                player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                remaining -= give;
            }
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
            {
                if (p.displayName != null && p.displayName.ToLower().Contains(token.ToLower()))
                {
                    if (match != null) return null; // ambiguous
                    match = p;
                }
            }
            return match;
        }

        // ====================================================================
        //  IN-GAME ITEM SPAWNER UI  (F1-style)
        // ====================================================================
        private const string UiRoot = "riftgiveui";
        private const string UiGrid = "riftgiveui.grid";
        private const string UiFb = "riftgiveui.fb";
        private const int PerPage = 20;     // 5 cols x 4 rows
        private const int Cols = 5;
        private const int Rows = 4;
        private static readonly int[] QtyOptions = { 1, 10, 100, 1000, 10000 };

        private const string ColPanel = "0.05 0.06 0.10 0.99";
        private const string ColBar = "0.10 0.12 0.20 0.95";
        private const string ColCell = "1 1 1 0.04";
        private const string ColAccent = "0.69 0.15 1 1";
        private const string ColAccentDim = "0.69 0.15 1 0.30";
        private const string ColCyan = "0 0.9 1 1";

        private class MenuState
        {
            public int Page;
            public int Qty = 100;
            public int Cat = -1;          // -1 = all
            public string Search = "";
            public bool Open;
            public int PendingItem;       // item awaiting Confirm/Cancel (0 = none)
        }

        private readonly Dictionary<ulong, MenuState> menu = new Dictionary<ulong, MenuState>();

        private MenuState State(BasePlayer p)
        {
            if (!menu.TryGetValue(p.userID, out var s))
            {
                s = new MenuState { Qty = config?.DefaultQty ?? 100 };
                menu[p.userID] = s;
            }
            return s;
        }

        // open commands
        [ChatCommand("iui")]
        private void CmdIui(BasePlayer player, string cmd, string[] args) => OpenMenu(player);
        [ChatCommand("imenu")]
        private void CmdImenu(BasePlayer player, string cmd, string[] args) => OpenMenu(player);

        private void OpenMenu(BasePlayer player)
        {
            if (!HasPerm(player)) { player.ChatMessage("You don't have permission."); return; }
            BuildMenu(player);
        }

        private List<ItemDefinition> Filtered(MenuState st)
        {
            if (allItems == null) BuildCaches();
            var search = (st.Search ?? "").ToLower();
            if (st.Cat < 0 && search.Length == 0) return allItems;   // fast path
            return allItems
                .Where(d =>
                    (st.Cat < 0 || (int)d.category == st.Cat) &&
                    (search.Length == 0 ||
                     d.displayName.english.ToLower().Contains(search) ||
                     d.shortname.ToLower().Contains(search)))
                .ToList();
        }

        private void BuildMenu(BasePlayer player)
        {
            if (allItems == null || categories == null) BuildCaches();
            var st = State(player);
            var items = Filtered(st);
            int pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)PerPage));
            st.Page = Mathf.Clamp(st.Page, 0, pages - 1);

            var c = new CuiElementContainer();
            CuiHelper.DestroyUi(player, UiRoot);

            // root
            c.Add(new CuiPanel
            {
                Image = { Color = ColPanel, Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0.08 0.08", AnchorMax = "0.92 0.92" },
                CursorEnabled = true
            }, "Overlay", UiRoot);

            // accent top border
            c.Add(new CuiPanel { Image = { Color = ColAccent },
                RectTransform = { AnchorMin = "0 0.965", AnchorMax = "1 0.97" } }, UiRoot);

            // header — server logo
            string logoPng = ImageLibrary?.Call("GetImage", "riftgive_logo", 0UL) as string;
            var logo = new CuiRawImageComponent { Color = "1 1 1 1" };
            if (!string.IsNullOrEmpty(logoPng)) logo.Png = logoPng;
            else logo.Url = config.LogoUrl;
            c.Add(new CuiElement
            {
                Parent = UiRoot,
                Components = { logo, new CuiRectTransformComponent { AnchorMin = "0.014 0.925", AnchorMax = "0.05 0.998" } }
            });

            // header — title + live feedback
            Label(c, UiRoot, "RIFT ADMIN · ITEM SPAWNER", 15, "0.055 0.93", "0.42 0.99", TextAnchor.MiddleLeft, ColAccent);
            AddFeedback(c, UiRoot, $"{items.Count} items  ·  giving x{st.Qty}");

            // search box
            c.Add(new CuiElement
            {
                Parent = UiRoot,
                Name = UiRoot + ".search",
                Components =
                {
                    new CuiImageComponent { Color = ColBar },
                    new CuiRectTransformComponent { AnchorMin = "0.71 0.93", AnchorMax = "0.93 0.99" }
                }
            });
            c.Add(new CuiElement
            {
                Parent = UiRoot + ".search",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = st.Search ?? "",
                        FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1",
                        CharsLimit = 32, Command = "riftgiveui.search",
                        Font = "robotocondensed-regular.ttf", NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.98 1" }
                }
            });

            // close
            c.Add(new CuiButton
            {
                Button = { Command = "riftgiveui.close", Color = "0.8 0.2 0.3 0.9" },
                Text = { Text = "X", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.945 0.93", AnchorMax = "0.985 0.99" }
            }, UiRoot);

            // clear-drops button (top of the left column)
            c.Add(new CuiButton
            {
                Button = { Command = "riftgiveui.cleardrops", Color = "0.78 0.3 0.2 0.95" },
                Text = { Text = "CLEAR DROPS", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.012 0.862", AnchorMax = "0.19 0.912" }
            }, UiRoot);

            // category column
            BuildCategories(c, st);

            // grid
            c.Add(new CuiPanel { Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.2 0.10", AnchorMax = "0.99 0.91" } }, UiRoot, UiGrid);

            int start = st.Page * PerPage;
            for (int i = 0; i < PerPage && start + i < items.Count; i++)
                AddCell(c, items[start + i], i);

            // footer: quantity + paging
            BuildFooter(c, st, pages);

            // confirm dialog (modal) when an item is pending
            if (st.PendingItem != 0) BuildConfirm(c, st);

            CuiHelper.AddUi(player, c);
            st.Open = true;
        }

        private void BuildConfirm(CuiElementContainer c, MenuState st)
        {
            var def = ItemManager.FindItemDefinition(st.PendingItem);
            if (def == null) { st.PendingItem = 0; return; }

            // dimmed backdrop blocks the grid behind it
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.65" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, UiRoot, UiRoot + ".confirm");

            string box = UiRoot + ".confirmbox";
            c.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.08 0.13 0.99", FadeIn = 0.2f },
                RectTransform = { AnchorMin = "0.3 0.34", AnchorMax = "0.7 0.66" }
            }, UiRoot + ".confirm", box);
            c.Add(new CuiPanel { Image = { Color = ColAccent },
                RectTransform = { AnchorMin = "0 0.94", AnchorMax = "1 0.97" } }, box);

            // item icon
            c.Add(new CuiElement
            {
                Parent = box,
                Components =
                {
                    new CuiImageComponent { ItemId = def.itemid, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.43 0.55", AnchorMax = "0.57 0.88" }
                }
            });
            Label(c, box, "CONFIRM", 11, "0 0.86", "1 0.93", TextAnchor.MiddleCenter, ColAccent);
            Label(c, box, $"Give <color=#00e5ff>x{st.Qty}</color>", 13, "0 0.40", "1 0.52", TextAnchor.MiddleCenter, "0.85 0.88 0.95 1");
            Label(c, box, def.displayName.english, 16, "0 0.28", "1 0.42", TextAnchor.MiddleCenter, "1 1 1 1");

            c.Add(new CuiButton
            {
                Button = { Command = "riftgiveui.confirm", Color = "0.17 0.75 0.38 0.95" },
                Text = { Text = "CONFIRM", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.1 0.08", AnchorMax = "0.48 0.22" }
            }, box);
            c.Add(new CuiButton
            {
                Button = { Command = "riftgiveui.cancel", Color = "0.8 0.25 0.32 0.95" },
                Text = { Text = "CANCEL", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.52 0.08", AnchorMax = "0.9 0.22" }
            }, box);
        }

        private void CloseMenu(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiRoot);
            if (menu.TryGetValue(player.userID, out var s)) { s.Open = false; s.PendingItem = 0; }
        }

        private void BuildCategories(CuiElementContainer c, MenuState st)
        {
            var cats = categories;
            float top = 0.85f, h = (top - 0.02f) / cats.Count;
            for (int i = 0; i < cats.Count; i++)
            {
                float y1 = top - i * h, y0 = y1 - h + 0.004f;
                bool active = st.Cat == cats[i].Key;
                c.Add(new CuiButton
                {
                    Button = { Command = $"riftgiveui.cat {cats[i].Key}", Color = active ? ColAccentDim : ColCell },
                    Text = { Text = cats[i].Value, FontSize = 10, Align = TextAnchor.MiddleCenter,
                             Color = active ? "1 1 1 1" : "0.7 0.74 0.85 1" },
                    RectTransform = { AnchorMin = $"0.012 {y0}", AnchorMax = $"0.19 {y1}" }
                }, UiRoot);
            }
        }

        private void AddCell(CuiElementContainer c, ItemDefinition def, int index)
        {
            int col = index % Cols, row = index / Cols;
            float cw = 1f / Cols, ch = 1f / Rows, pad = 0.008f;
            float x0 = col * cw + pad, x1 = (col + 1) * cw - pad;
            float y1 = 1f - row * ch - pad, y0 = 1f - (row + 1) * ch + pad;

            string cell = $"{UiGrid}.{index}";
            c.Add(new CuiPanel { Image = { Color = ColCell },
                RectTransform = { AnchorMin = $"{x0} {y0}", AnchorMax = $"{x1} {y1}" } }, UiGrid, cell);

            // item icon
            c.Add(new CuiElement
            {
                Parent = cell,
                Components =
                {
                    new CuiImageComponent { ItemId = def.itemid, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.16 0.28", AnchorMax = "0.84 0.94" }
                }
            });

            // name
            Label(c, cell, def.displayName.english, 8, "0.04 0.02", "0.96 0.26", TextAnchor.MiddleCenter, "0.82 0.85 0.95 1");

            // full-cell click → give
            c.Add(new CuiButton
            {
                Button = { Command = $"riftgiveui.give {def.itemid}", Color = "0 0 0 0" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, cell);
        }

        private void BuildFooter(CuiElementContainer c, MenuState st, int pages)
        {
            // quantity buttons (left)
            Label(c, UiRoot, "QTY", 10, "0.2 0.025", "0.25 0.075", TextAnchor.MiddleLeft, "0.6 0.65 0.8 1");
            for (int i = 0; i < QtyOptions.Length; i++)
            {
                bool active = st.Qty == QtyOptions[i];
                float x0 = 0.25f + i * 0.075f, x1 = x0 + 0.07f;
                c.Add(new CuiButton
                {
                    Button = { Command = $"riftgiveui.qty {QtyOptions[i]}", Color = active ? ColAccent : ColCell },
                    Text = { Text = QtyOptions[i].ToString(), FontSize = 11, Align = TextAnchor.MiddleCenter,
                             Color = active ? "1 1 1 1" : "0.7 0.74 0.85 1" },
                    RectTransform = { AnchorMin = $"{x0} 0.02", AnchorMax = $"{x1} 0.08" }
                }, UiRoot);
            }

            // free-type quantity box
            c.Add(new CuiElement
            {
                Parent = UiRoot,
                Name = UiRoot + ".qtyin",
                Components =
                {
                    new CuiImageComponent { Color = ColBar },
                    new CuiRectTransformComponent { AnchorMin = "0.63 0.02", AnchorMax = "0.72 0.08" }
                }
            });
            c.Add(new CuiElement
            {
                Parent = UiRoot + ".qtyin",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = st.Qty.ToString(), FontSize = 11, Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1", CharsLimit = 7, Command = "riftgiveui.qty",
                        Font = "robotocondensed-regular.ttf"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.05 0", AnchorMax = "0.95 1" }
                }
            });

            // paging (right)
            c.Add(new CuiButton
            {
                Button = { Command = $"riftgiveui.page {st.Page - 1}", Color = ColCell },
                Text = { Text = "< PREV", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.8 0.84 0.95 1" },
                RectTransform = { AnchorMin = "0.74 0.02", AnchorMax = "0.83 0.08" }
            }, UiRoot);
            Label(c, UiRoot, $"PAGE {st.Page + 1}/{pages}", 11, "0.83 0.02", "0.91 0.08", TextAnchor.MiddleCenter, ColCyan);
            c.Add(new CuiButton
            {
                Button = { Command = $"riftgiveui.page {st.Page + 1}", Color = ColCell },
                Text = { Text = "NEXT >", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.8 0.84 0.95 1" },
                RectTransform = { AnchorMin = "0.91 0.02", AnchorMax = "0.99 0.08" }
            }, UiRoot);
        }

        private void AddFeedback(CuiElementContainer c, string parent, string text)
        {
            c.Add(new CuiElement
            {
                Parent = parent,
                Name = UiFb,
                Components =
                {
                    new CuiTextComponent { Text = text, FontSize = 11, Align = TextAnchor.MiddleRight,
                        Color = ColCyan, Font = "robotocondensed-bold.ttf" },
                    new CuiRectTransformComponent { AnchorMin = "0.42 0.93", AnchorMax = "0.70 0.99" }
                }
            });
        }

        // updates just the feedback line (no full rebuild → no flicker on give)
        private void UpdateFeedback(BasePlayer player, string text)
        {
            CuiHelper.DestroyUi(player, UiFb);
            var c = new CuiElementContainer();
            AddFeedback(c, UiRoot, text);
            CuiHelper.AddUi(player, c);
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

        // ---- UI console commands -------------------------------------------
        private bool UiGuard(ConsoleSystem.Arg arg, out BasePlayer player)
        {
            player = arg.Player();
            return player != null && HasPerm(player);
        }

        [ConsoleCommand("riftgiveui.give")]
        private void CcUiGive(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            var def = ItemManager.FindItemDefinition(arg.GetInt(0, 0));
            if (def == null) return;
            var st = State(player);

            if (config.RequireConfirm)
            {
                st.PendingItem = def.itemid;   // ask first — prevents misclick gives
                BuildMenu(player);
                return;
            }

            GiveSilently(player, def, st.Qty, 0UL);
            UpdateFeedback(player, $"<color=#2bff88>+{st.Qty}</color> {def.displayName.english}");
        }

        [ConsoleCommand("riftgiveui.confirm")]
        private void CcUiConfirm(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            var st = State(player);
            var def = st.PendingItem != 0 ? ItemManager.FindItemDefinition(st.PendingItem) : null;
            st.PendingItem = 0;
            BuildMenu(player);
            if (def != null)
            {
                GiveSilently(player, def, st.Qty, 0UL);
                UpdateFeedback(player, $"<color=#2bff88>+{st.Qty}</color> {def.displayName.english}");
            }
        }

        [ConsoleCommand("riftgiveui.cancel")]
        private void CcUiCancel(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            State(player).PendingItem = 0;
            BuildMenu(player);
        }

        // Removes dropped items / item bags on the ground near the admin — quick
        // undo for a wrong spawn you dropped.
        [ConsoleCommand("riftgiveui.cleardrops")]
        private void CcUiClearDrops(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            int n = ClearDropsNear(player);
            UpdateFeedback(player, n > 0
                ? $"<color=#ffb020>Cleared {n} dropped items</color>"
                : "<color=#9aa3c4>No drops nearby</color>");
        }

        private int ClearDropsNear(BasePlayer player)
        {
            float r = Mathf.Clamp(config.ClearDropRadius, 2f, 50f);
            var pos = player.transform.position;
            int count = 0;

            var drops = new List<DroppedItem>();
            Vis.Entities(pos, r, drops);
            foreach (var d in drops)
                if (d != null && !d.IsDestroyed) { d.Kill(); count++; }

            var bags = new List<DroppedItemContainer>();
            Vis.Entities(pos, r, bags);
            foreach (var b in bags)
                if (b != null && !b.IsDestroyed) { b.Kill(); count++; }

            return count;
        }

        [ConsoleCommand("riftgiveui.qty")]
        private void CcUiQty(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            State(player).Qty = Mathf.Max(1, arg.GetInt(0, 1));
            BuildMenu(player);
        }

        [ConsoleCommand("riftgiveui.cat")]
        private void CcUiCat(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            var st = State(player); st.Cat = arg.GetInt(0, -1); st.Page = 0;
            BuildMenu(player);
        }

        [ConsoleCommand("riftgiveui.page")]
        private void CcUiPage(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            State(player).Page = arg.GetInt(0, 0);
            BuildMenu(player);
        }

        [ConsoleCommand("riftgiveui.search")]
        private void CcUiSearch(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            var st = State(player);
            st.Search = arg.Args != null ? string.Join(" ", arg.Args).Trim() : "";
            st.Page = 0;
            BuildMenu(player);
        }

        [ConsoleCommand("riftgiveui.close")]
        private void CcUiClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CloseMenu(player);
        }

        // Bindable: open the menu if closed, close it if open.
        //   bind <key> riftgiveui.toggle
        [ConsoleCommand("riftgiveui.toggle")]
        private void CcUiToggle(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            if (State(player).Open) CloseMenu(player);
            else BuildMenu(player);
        }

        // Bindable: always open.
        [ConsoleCommand("riftgiveui.open")]
        private void CcUiOpen(ConsoleSystem.Arg arg)
        {
            if (!UiGuard(arg, out var player)) return;
            BuildMenu(player);
        }
    }
}
