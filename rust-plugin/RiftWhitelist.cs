// RiftWhitelist — website-driven whitelist for PROJECT RIFT.
//
// Only SteamIDs approved on the website can join the server. The plugin polls
// the site's approved feed, caches it to disk (so it still works if the site is
// down), and rejects anyone not on the list at login with a friendly message.
//
//   /wl add <steamid>      manually whitelist someone (owner)
//   /wl remove <steamid>   remove a manual entry
//   /wl sync               force a re-sync from the website now
//   /wl check <steamid>    is this id whitelisted?
//   /wl list               count of approved + manual
//   console: riftwl.sync / riftwl.on / riftwl.off
//
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("RiftWhitelist", "ESYSTEMLK", "1.0.0")]
    [Description("Only website-approved SteamIDs can join. Syncs the approved list from the Project Rift site.")]
    public class RiftWhitelist : RustPlugin
    {
        private const string PermBypass = "riftwhitelist.bypass";
        private const string PermAdmin = "riftwhitelist.admin";

        private Configuration config;
        private StoredData data;
        private HashSet<string> approved = new HashSet<string>();  // from website
        private bool everSynced;

        public class Configuration
        {
            public bool Enabled = true;
            public string ApprovedUrl = "https://projectrift.esystemlk.com/api/whitelist/approved";
            public string ApiKey = "change-me";
            public float SyncMinutes = 3f;
            public bool AllowServerAdmins = true;   // owners/moderators always allowed
            public string KickMessage =
                "You are not whitelisted on PROJECT RIFT.\n\nApply at projectrift.esystemlk.com and wait for staff approval.";
        }

        private class StoredData
        {
            public List<string> Approved = new List<string>();   // last synced (offline fallback)
            public List<string> Manual = new List<string>();     // added in-game by owner
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
            permission.RegisterPermission(PermBypass, this);
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
            approved = new HashSet<string>(data.Approved);
        }

        private void OnServerInitialized()
        {
            Sync();
            timer.Every(Math.Max(30f, config.SyncMinutes * 60f), Sync);
        }

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("RiftWhitelist") ?? new StoredData(); }
            catch { data = new StoredData(); }
        }
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("RiftWhitelist", data);

        // ---- the gate --------------------------------------------------------
        private object CanUserLogin(string name, string id, string ip)
        {
            if (!config.Enabled) return null;
            if (IsAllowed(id)) return null;   // allow
            return config.KickMessage;        // returning a string denies with this reason
        }

        private bool IsAllowed(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (approved.Contains(id)) return true;
            if (data.Manual.Contains(id)) return true;
            if (permission.UserHasPermission(id, PermBypass)) return true;
            if (config.AllowServerAdmins && ulong.TryParse(id, out var uid) &&
                (ServerUsers.Is(uid, ServerUsers.UserGroup.Owner) || ServerUsers.Is(uid, ServerUsers.UserGroup.Moderator)))
                return true;
            return false;
        }

        // ---- sync from website ----------------------------------------------
        private void Sync()
        {
            if (string.IsNullOrEmpty(config.ApprovedUrl)) return;
            var headers = new Dictionary<string, string> { ["x-api-key"] = config.ApiKey };
            webrequest.Enqueue(config.ApprovedUrl, "", (code, resp) =>
            {
                if (code != 200 || string.IsNullOrEmpty(resp))
                {
                    PrintWarning($"Whitelist sync failed (HTTP {code}). Using cached list ({approved.Count}).");
                    return;
                }
                try
                {
                    var payload = JsonConvert.DeserializeObject<ApprovedResponse>(resp);
                    if (payload?.steamIds == null) { PrintWarning("Whitelist sync: bad payload."); return; }
                    approved = new HashSet<string>(payload.steamIds);
                    data.Approved = new List<string>(payload.steamIds);
                    SaveData();
                    everSynced = true;
                    Puts($"Whitelist synced — {approved.Count} approved players.");
                }
                catch (Exception e) { PrintWarning($"Whitelist parse error: {e.Message}"); }
            }, this, RequestMethod.GET, headers, 20f);
        }

        private class ApprovedResponse
        {
            public bool ok;
            public int count;
            public List<string> steamIds;
        }

        // ---- admin commands --------------------------------------------------
        [ChatCommand("wl")]
        private void CmdWl(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin))
            { player.ChatMessage("No permission."); return; }
            player.ChatMessage(HandleWl(args));
        }

        [ConsoleCommand("riftwl.sync")]
        private void CcSync(ConsoleSystem.Arg arg) { Sync(); arg.ReplyWith("Whitelist sync requested."); }

        [ConsoleCommand("riftwl.on")]
        private void CcOn(ConsoleSystem.Arg arg) { config.Enabled = true; SaveConfig(); arg.ReplyWith("Whitelist ENABLED."); }

        [ConsoleCommand("riftwl.off")]
        private void CcOff(ConsoleSystem.Arg arg) { config.Enabled = false; SaveConfig(); arg.ReplyWith("Whitelist DISABLED."); }

        [ConsoleCommand("riftwl.add")]
        private void CcAdd(ConsoleSystem.Arg arg)
        {
            var id = arg.GetString(0, "");
            arg.ReplyWith(AddManual(id));
        }

        private string HandleWl(string[] args)
        {
            if (args.Length == 0)
                return "Usage: /wl <add|remove|sync|check|list> [steamid]";

            switch (args[0].ToLower())
            {
                case "add":    return args.Length > 1 ? AddManual(args[1]) : "Usage: /wl add <steamid>";
                case "remove": return args.Length > 1 ? RemoveManual(args[1]) : "Usage: /wl remove <steamid>";
                case "sync":   Sync(); return "Sync requested.";
                case "check":
                    if (args.Length < 2) return "Usage: /wl check <steamid>";
                    return IsAllowed(args[1]) ? $"{args[1]} is WHITELISTED." : $"{args[1]} is NOT whitelisted.";
                case "list":
                    return $"Approved (website): {approved.Count} · Manual: {data.Manual.Count} · Enabled: {config.Enabled} · Synced: {everSynced}";
                default: return "Usage: /wl <add|remove|sync|check|list> [steamid]";
            }
        }

        private string AddManual(string id)
        {
            if (!ulong.TryParse(id, out _) || id.Length != 17) return "Invalid SteamID64.";
            if (!data.Manual.Contains(id)) { data.Manual.Add(id); SaveData(); }
            return $"{id} manually whitelisted.";
        }

        private string RemoveManual(string id)
        {
            if (data.Manual.Remove(id)) { SaveData(); return $"{id} removed from manual whitelist."; }
            return $"{id} was not in the manual list (website-approved ids sync automatically).";
        }
    }
}
