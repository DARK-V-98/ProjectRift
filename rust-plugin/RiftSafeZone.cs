// RiftSafeZone — owner-placed safe zone for PROJECT RIFT (e.g. a shop at your base).
//
// Creates an authentic Rust safe-zone trigger (weapons holster + safe-zone HUD,
// like Outpost) at a position you set, and backs it with damage protection so
// players, buildings and deployables inside can't be hurt or raided.
//
//   /safezone set [radius]   — place the zone at where you stand (admin)
//   /safezone remove         — delete the zone
//   /safezone info           — show current zone
//   /safezone tp             — teleport to the zone center
//
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RiftSafeZone", "ESYSTEMLK", "1.0.0")]
    [Description("Owner-placed safe zone (shop/base) with weapon-holster trigger + full damage protection.")]
    public class RiftSafeZone : RustPlugin
    {
        private const string PermAdmin = "riftsafezone.admin";
        private const string PermBypass = "riftsafezone.bypass";

        private Configuration config;
        private GameObject zoneGo;
        private float sqrRadius;

        public class Configuration
        {
            public bool Enabled = true;
            public bool HasZone = false;
            public float CenterX, CenterY, CenterZ;
            public float Radius = 40f;
            public bool ProtectPlayers = true;     // block PvP / NPC / animal damage inside
            public bool ProtectBuildings = true;   // block raiding of structures/deployables inside
            public bool NoDecay = true;            // block decay inside
            public bool HolsterWeapons = true;     // authentic safe-zone trigger (holster + HUD)

            // Authorized players keep their weapons out (not holstered) inside the zone.
            public bool BypassForBuildingAuthed = true; // anyone TC-authorized at the base
            public bool BypassAdmins = true;            // server admins
            // (permission "riftsafezone.bypass" always works)

            public Vector3 Center => new Vector3(CenterX, CenterY, CenterZ);
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
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermBypass, this);
        }

        // Authorized players (TC-authed at the base / bypass perm / admins) are NOT
        // affected by the holster trigger — they can carry weapons inside the zone.
        private bool IsExempt(BasePlayer p)
        {
            if (p == null || p.IsNpc) return false;
            if (config.BypassAdmins && p.IsAdmin) return true;
            if (permission.UserHasPermission(p.UserIDString, PermBypass)) return true;
            if (config.BypassForBuildingAuthed && p.IsBuildingAuthed()) return true;
            return false;
        }

        // Block exempt players from being registered to OUR safe-zone trigger so
        // their weapons stay out (the rest of the zone is unaffected).
        private object OnEntityEnter(TriggerBase trigger, BaseEntity entity)
        {
            if (zoneGo == null || trigger == null) return null;
            if (!(trigger is TriggerSafeZone) || trigger.gameObject != zoneGo) return null;
            if (entity is BasePlayer player && IsExempt(player)) return false; // cancel entry
            return null;
        }

        private void OnServerInitialized()
        {
            sqrRadius = config.Radius * config.Radius;
            if (config.Enabled && config.HasZone) CreateZone();
        }

        private void Unload() => DestroyZone();

        // ---- commands -------------------------------------------------------
        [ChatCommand("safezone")]
        private void CmdSafeZone(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin))
            { player.ChatMessage("You don't have permission."); return; }

            string sub = args.Length > 0 ? args[0].ToLower() : "info";
            switch (sub)
            {
                case "set":
                    if (args.Length > 1 && float.TryParse(args[1], out var r)) config.Radius = Mathf.Clamp(r, 5f, 300f);
                    var pos = player.transform.position;
                    config.CenterX = pos.x; config.CenterY = pos.y; config.CenterZ = pos.z;
                    config.HasZone = true; config.Enabled = true;
                    sqrRadius = config.Radius * config.Radius;
                    SaveConfig();
                    CreateZone();
                    player.ChatMessage($"<color=#2bff88>Safe zone set</color> here · radius {config.Radius:0}m. Players are now protected inside.");
                    break;

                case "remove":
                case "delete":
                    config.HasZone = false;
                    SaveConfig();
                    DestroyZone();
                    player.ChatMessage("Safe zone removed.");
                    break;

                case "tp":
                    if (!config.HasZone) { player.ChatMessage("No safe zone set."); return; }
                    player.Teleport(config.Center);
                    break;

                default:
                    if (config.HasZone)
                        player.ChatMessage($"Safe zone: <color=#00e5ff>{config.Center}</color> · radius {config.Radius:0}m · " +
                                           $"players {(config.ProtectPlayers ? "protected" : "off")} · buildings {(config.ProtectBuildings ? "protected" : "off")}");
                    else
                        player.ChatMessage("No safe zone set. Stand where you want it and type /safezone set [radius]");
                    break;
            }
        }

        // ---- zone trigger ---------------------------------------------------
        private void CreateZone()
        {
            DestroyZone();
            if (!config.HasZone) return;

            zoneGo = new GameObject("RiftSafeZone");
            zoneGo.transform.position = config.Center;
            zoneGo.layer = (int)Rust.Layer.Trigger;

            var sphere = zoneGo.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = config.Radius;

            if (config.HolsterWeapons)
                zoneGo.AddComponent<TriggerSafeZone>();   // authentic safe-zone behaviour

            Puts($"Safe zone active at {config.Center} (radius {config.Radius}m).");
        }

        private void DestroyZone()
        {
            if (zoneGo != null) { UnityEngine.Object.Destroy(zoneGo); zoneGo = null; }
        }

        private bool InZone(Vector3 pos)
        {
            if (!config.Enabled || !config.HasZone) return false;
            return (pos - config.Center).sqrMagnitude <= sqrRadius;
        }

        // ---- protection -----------------------------------------------------
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (!InZone(entity.transform.position)) return null;

            // authorized players are unaffected — their damage passes through
            var attacker = info.InitiatorPlayer;
            if (attacker != null && IsExempt(attacker)) return null;

            // players
            if (entity is BasePlayer victim && !victim.IsNpc)
            {
                if (config.ProtectPlayers && info.Initiator != null && info.Initiator != victim)
                    return CancelDamage(info);
                return null;
            }

            // structures / deployables
            if (config.ProtectBuildings)
            {
                if (info.InitiatorPlayer != null) return CancelDamage(info);              // raiding
                if (config.NoDecay && info.damageTypes.Get(Rust.DamageType.Decay) > 0f)   // decay
                    return CancelDamage(info);
            }
            return null;
        }

        private object CancelDamage(HitInfo info)
        {
            info.damageTypes.ScaleAll(0f);
            info.HitEntity = null;
            info.DoHitEffects = false;
            return true;
        }
    }
}
