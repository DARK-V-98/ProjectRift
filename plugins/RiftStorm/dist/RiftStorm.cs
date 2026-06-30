// ============================================================================
//  RIFT STORM  —  Project Rift signature world event  (Carbon / uMod-Oxide)
//  Author: ESYSTEMLK   |   https://projectrift.esystemlk.com
//
//  A modular, multi-phase world event:
//    Detection → Storm → Rift → NPC Waves → Energy Crystals → Rift Overlord → Victory
//
//  This is the SHIPPABLE single-file build (see plugins/RiftStorm/docs for the
//  full step-by-step design). Managers are nested classes wired through a small
//  DI container (RiftContext) built once in OnServerInitialized.
//
//  NOTE: Rust's runtime API changes across forced wipes. Calls that touch game
//  internals (entity spawn/health, NPC gear, effect prefabs) are written to
//  common plugin idioms and wrapped defensively, but COMPILE + FIELD-TEST on a
//  staging Carbon server before going live.
//
//  Install:  carbon/plugins/RiftStorm.cs   (config → carbon/configs/RiftStorm.json)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    [Info("RiftStorm", "ESYSTEMLK", "1.0.0")]
    [Description("Project Rift signature world event: detection, storm, rift, NPC waves, energy crystals, the Rift Overlord boss, and rewards.")]
    public class RiftStorm : RustPlugin
    {
        // optional integrations — null-safe
        [PluginReference] private Plugin ProjectRiftCore;
        [PluginReference] private Plugin ImageLibrary;

        private Configuration config;
        private RiftContext ctx;
        private RiftEventManager events;

        #region Oxide lifecycle

        private void Init()
        {
            permission.RegisterPermission(config.Permissions.Admin, this);
            if (!string.IsNullOrEmpty(config.Permissions.Reward))
                permission.RegisterPermission(config.Permissions.Reward, this);
        }

        private void OnServerInitialized()
        {
            ValidateConfig();
            BuildContainer();
            events.ScheduleNext();
            ctx.Logger.Info("RiftStorm loaded.");
        }

        private void Unload()
        {
            try { events?.StopEvent(false); } catch (Exception ex) { PrintError($"Unload stop: {ex.Message}"); }
            try { ctx?.Pool?.CleanupAll(null); } catch { /* ignore */ }
            foreach (var pl in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(pl, UiManager.HudRoot);
                CuiHelper.DestroyUi(pl, UiManager.BossRoot);
                CuiHelper.DestroyUi(pl, WeatherController.FilterName);
            }
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
            catch (Exception ex)
            {
                PrintWarning($"Config invalid, regenerating defaults: {ex.Message}");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        private void ValidateConfig()
        {
            var c = config;
            if (c.Schedule.MinHours > c.Schedule.MaxHours)
            {
                var t = c.Schedule.MinHours; c.Schedule.MinHours = c.Schedule.MaxHours; c.Schedule.MaxHours = t;
                PrintWarning("Schedule Min/Max hours swapped (min was greater than max).");
            }
            c.Performance.TickHz = Mathf.Clamp(c.Performance.TickHz, 0.5f, 10f);
            c.Crystals.Count = Mathf.Max(1, c.Crystals.Count);
            c.Performance.MaxNpcs = Mathf.Clamp(c.Performance.MaxNpcs, 1, 200);
            if (c.Locations.FindAll(l => l.Enabled).Count == 0)
                PrintWarning("No locations enabled — auto events cannot start.");
            SaveConfig();
        }

        #endregion

        #region Composition root

        private void BuildContainer()
        {
            var logger = new RiftLogger(this, config);
            var pool = new EntityPool(logger);

            ctx = new RiftContext(this, config, logger, pool, BroadcastMessage);

            ctx.Weather = new WeatherController(ctx);
            ctx.Rift    = new RiftController(ctx);
            ctx.Npcs    = new NpcManager(ctx);
            ctx.Boss    = new BossController(ctx);
            ctx.Loot    = new LootManager(ctx);
            ctx.Ui      = new UiManager(ctx);
            ctx.Discord = new DiscordService(ctx);
            ctx.Api     = new ApiService(ctx);

            events = new RiftEventManager(ctx);
        }

        // Server-wide announce — routes through ProjectRiftCore's notification
        // shower if present + enabled, otherwise plain chat broadcast.
        private void BroadcastMessage(string msg)
        {
            try
            {
                if (config.Announce.UseCoreNotifications && ProjectRiftCore != null && ProjectRiftCore.IsLoaded)
                    ProjectRiftCore.Call("PushNotification", msg, "alert");
                else
                    PrintToChat(msg);
            }
            catch (Exception ex) { PrintError($"Broadcast failed: {ex.Message}"); }
        }

        #endregion

        #region Hooks

        // Crystal / boss destruction + NPC loot.
        private object OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (events == null || !events.IsRunning || entity == null) return null;
            var ev = events.Active;

            if (entity.GetComponent<RiftCrystalTag>() != null && ev.Crystals.Contains(entity))
                ctx.Rift.NotifyCrystalDestroyed(ev, entity);
            else if (entity.GetComponent<RiftBossTag>() != null)
            {
                ctx.Loot.FillCorpse(entity, config.Boss.Profile.Loot);
                ctx.Boss.NotifyBossDeath();
            }
            else if (entity.GetComponent<RiftNpcTag>() is RiftNpcTag tag)
                ctx.Npcs.OnNpcDeath(entity, tag);

            return null;
        }

        // Damage scaling for our NPCs/boss + participant damage tracking.
        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (events == null || !events.IsRunning || entity == null || info == null) return;
            var ev = events.Active;

            // scale outgoing damage from our attackers
            var attacker = info.Initiator as BaseCombatEntity;
            if (attacker != null)
            {
                var npcTag = attacker.GetComponent<RiftNpcTag>();
                if (npcTag != null && npcTag.DamageScale != 1f)
                    info.damageTypes.ScaleAll(npcTag.DamageScale);
                else if (attacker.GetComponent<RiftBossTag>() != null)
                    info.damageTypes.ScaleAll(ev.BossRaged ? config.Boss.RageDamageMult * 1.6f : 1.6f);
            }

            // track player damage onto boss / crystals for reward scoring
            var player = info.InitiatorPlayer;
            if (player != null && !player.IsNpc &&
                (entity == ev.Boss || entity.GetComponent<RiftCrystalTag>() != null))
                ev.AddDamage(player.userID, info.damageTypes.Total());
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (events == null || !events.IsRunning) return;
            ctx.Ui.ForceRedraw();
            if (ctx.Weather.IsActive)
                timer.Once(4f, () => { if (player != null && player.IsConnected) ctx.Weather.ShowFilter(player); });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiManager.HudRoot);
            CuiHelper.DestroyUi(player, UiManager.BossRoot);
            CuiHelper.DestroyUi(player, WeatherController.FilterName);
        }

        #endregion

        #region Commands

        [ChatCommand("admin")]
        private void CmdAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdmin(player)) { Reply(player, "You do not have permission."); return; }
            if (args.Length == 0)
            {
                Reply(player, "Usage: /admin <startrift|stoprift|nextrift|riftstatus|debugrift>");
                return;
            }

            switch (args[0].ToLower())
            {
                case "startrift":
                    var name = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : null;
                    var loc = events.PickByNameOrRandom(name);
                    if (loc == null) { Reply(player, "No valid location found."); return; }
                    Reply(player, events.StartEvent(loc) ? $"Rift Storm started at {loc.Name}." : "An event is already running.");
                    break;
                case "stoprift":
                    events.StopEvent(false);
                    Reply(player, "Rift Storm stopped.");
                    break;
                case "nextrift":
                    events.ScheduleNext();
                    Reply(player, "Next Rift Storm rescheduled.");
                    break;
                case "riftstatus":
                    Reply(player, events.StatusLine());
                    break;
                case "debugrift":
                    Reply(player, ctx.Pool.DebugSummary());
                    Reply(player, ctx.Logger.PerfSummary());
                    Reply(player, events.StatusLine());
                    break;
                default:
                    Reply(player, "Unknown subcommand.");
                    break;
            }
        }

        [ConsoleCommand("riftstorm.start")]
        private void CcStart(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p != null && !HasAdmin(p)) return;
            var loc = events.PickByNameOrRandom(arg.GetString(0, null));
            arg.ReplyWith(loc != null && events.StartEvent(loc) ? "Started." : "Could not start.");
        }

        [ConsoleCommand("riftstorm.stop")]
        private void CcStop(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p != null && !HasAdmin(p)) return;
            events.StopEvent(false);
            arg.ReplyWith("Stopped.");
        }

        private bool HasAdmin(BasePlayer p) =>
            p == null || p.IsAdmin || permission.UserHasPermission(p.UserIDString, config.Permissions.Admin);

        private void Reply(BasePlayer p, string msg) =>
            p?.ChatMessage($"{config.Announce.Prefix} {msg}");

        #endregion

        // ====================================================================
        //  CONFIGURATION
        // ====================================================================
        #region Configuration

        public class Configuration
        {
            [JsonProperty("Schedule")] public ScheduleConfig Schedule = new ScheduleConfig();
            [JsonProperty("Weather")] public WeatherConfig Weather = new WeatherConfig();
            [JsonProperty("Zone")] public ZoneConfig Zone = new ZoneConfig();
            [JsonProperty("Waves (NPC)")] public WavesConfig Waves = new WavesConfig();
            [JsonProperty("Objectives (Crystals)")] public CrystalConfig Crystals = new CrystalConfig();
            [JsonProperty("Boss (Rift Overlord)")] public BossConfig Boss = new BossConfig();
            [JsonProperty("Rewards")] public RewardConfig Rewards = new RewardConfig();
            [JsonProperty("Locations")] public List<RiftLocation> Locations = RiftLocation.Defaults();
            [JsonProperty("UI")] public UiConfig Ui = new UiConfig();
            [JsonProperty("Discord")] public DiscordConfig Discord = new DiscordConfig();
            [JsonProperty("Website API")] public ApiConfig Api = new ApiConfig();
            [JsonProperty("Announcements")] public AnnounceConfig Announce = new AnnounceConfig();
            [JsonProperty("Permissions")] public PermissionConfig Permissions = new PermissionConfig();
            [JsonProperty("Performance")] public PerfConfig Performance = new PerfConfig();
        }

        public class ScheduleConfig
        {
            [JsonProperty("Enabled (auto-run)")] public bool Enabled = true;
            [JsonProperty("Min hours between events")] public float MinHours = 2f;
            [JsonProperty("Max hours between events")] public float MaxHours = 4f;
            [JsonProperty("Detection countdown (seconds)")] public float CountdownSeconds = 180f;
            [JsonProperty("Min online players to start")] public int MinPlayers = 2;
        }

        public class WeatherConfig
        {
            [JsonProperty("Override weather")] public bool Enabled = true;
            [JsonProperty("Rain (0-1)")] public float Rain = 1f;
            [JsonProperty("Fog (0-1)")] public float Fog = 1f;
            [JsonProperty("Clouds (0-1)")] public float Clouds = 1f;
            [JsonProperty("Wind (0-1)")] public float Wind = 0.9f;
            [JsonProperty("Thunder enabled")] public bool Thunder = true;
            [JsonProperty("Thunder interval min (s)")] public float ThunderMin = 6f;
            [JsonProperty("Thunder interval max (s)")] public float ThunderMax = 14f;
            [JsonProperty("Lightning FX prefab")] public string LightningPrefab = "assets/bundled/prefabs/fx/tesla/electrocute_arc.prefab";
            [JsonProperty("Purple smoke prefab")] public string SmokePrefab = "assets/bundled/prefabs/fx/smoke_signal_full.prefab";
            [JsonProperty("Purple light count")] public int LightCount = 12;
            [JsonProperty("Atmosphere ring radius")] public float RingRadius = 40f;
            [JsonProperty("Server-wide purple screen filter")] public bool ScreenFilter = true;
            [JsonProperty("Screen filter color (hex)")] public string FilterColor = "#8A2BE2";
            [JsonProperty("Screen filter strength (0-1)")] public float FilterStrength = 0.3f;
        }

        public class ZoneConfig
        {
            [JsonProperty("Play area radius (m)")] public float Radius = 80f;
            [JsonProperty("Min seconds inside to earn loot")] public float MinParticipationSeconds = 30f;
        }

        public class WavesConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Spawn radius from center")] public float SpawnRadius = 35f;
            [JsonProperty("Delay between waves (s)")] public float WaveDelay = 8f;
            [JsonProperty("Scale NPC count with players")] public bool ScaleWithPlayers = true;
            [JsonProperty("Players per extra NPC")] public int PlayersPerExtraNpc = 4;
            [JsonProperty("Profiles")] public List<NpcProfile> Profiles = NpcProfile.DefaultWaves();
        }

        public class CrystalConfig
        {
            [JsonProperty("Count")] public int Count = 4;
            [JsonProperty("Health each")] public float Health = 1500f;
            [JsonProperty("Spawn radius from center")] public float Radius = 18f;
            [JsonProperty("Stability per crystal (%)")] public float StabilityPerCrystal = 25f;
            [JsonProperty("Explosion prefab on destroy")] public string ExplosionPrefab = "assets/prefabs/tools/c4/effects/c4_explosion.prefab";
            [JsonProperty("Purple particle prefab")] public string ParticlePrefab = "assets/bundled/prefabs/fx/player/beartrap_scream.prefab";
            [JsonProperty("Crystal entity prefab")] public string EntityPrefab = "assets/prefabs/deployable/research table/researchtable_deployed.prefab";
        }

        public class BossConfig
        {
            [JsonProperty("Display name")] public string Name = "Rift Overlord";
            [JsonProperty("Health")] public float Health = 8000f;
            [JsonProperty("Profile")] public NpcProfile Profile = NpcProfile.DefaultBoss();
            [JsonProperty("EMP pulse cooldown (s)")] public float EmpCooldown = 25f;
            [JsonProperty("Lightning cooldown (s)")] public float LightningCooldown = 12f;
            [JsonProperty("Area damage cooldown (s)")] public float AreaCooldown = 18f;
            [JsonProperty("Summon cooldown (s)")] public float SummonCooldown = 30f;
            [JsonProperty("Summon count")] public int SummonCount = 3;
            [JsonProperty("Rage HP threshold (%)")] public float RageThreshold = 30f;
            [JsonProperty("Rage damage multiplier")] public float RageDamageMult = 1.5f;
            [JsonProperty("Rage cooldown multiplier")] public float RageCdMult = 0.6f;
        }

        public class RewardConfig
        {
            [JsonProperty("Rift Crate prefab")] public string CratePrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
            [JsonProperty("Crate loot table")] public LootTable CrateLoot = LootTable.DefaultCrate();
            [JsonProperty("Tokens for participants")] public int ParticipantTokens = 25;
            [JsonProperty("Bonus tokens for top damage")] public int TopDamageTokens = 100;
            [JsonProperty("Cosmetic skin ids")] public List<ulong> CosmeticSkins = new List<ulong>();
            [JsonProperty("Title group (oxide usergroup)")] public string TitleGroup = "rift_survivor";
            [JsonProperty("Token grant command ({steamid} {amount})")] public string TokenGrantCmd = "addtokens {steamid} {amount}";
        }

        public class UiConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Refresh interval (s)")] public float RefreshSeconds = 1f;
            [JsonProperty("Show reward preview")] public bool ShowRewardPreview = true;
        }

        public class DiscordConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = false;
            [JsonProperty("Webhook URL")] public string Webhook = "";
            [JsonProperty("Embed color (decimal)")] public int Color = 11544575; // #B026FF
            [JsonProperty("Mention role id (blank = none)")] public string MentionRole = "";
        }

        public class ApiConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Push URL")] public string PushUrl = "https://projectrift.esystemlk.com/api/rift";
            [JsonProperty("API key (x-api-key)")] public string ApiKey = "change-me";
            [JsonProperty("Push interval (s)")] public float PushSeconds = 5f;
        }

        public class AnnounceConfig
        {
            [JsonProperty("Chat prefix")] public string Prefix = "<color=#B026FF>[RIFT STORM]</color>";
            [JsonProperty("Use ProjectRiftCore notification shower")] public bool UseCoreNotifications = true;
            [JsonProperty("Warning sound (detection)")] public string WarnSound = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
            [JsonProperty("Storm sound")] public string StormSound = "assets/bundled/prefabs/fx/player/howl.prefab";
        }

        public class PermissionConfig
        {
            [JsonProperty("Admin permission")] public string Admin = "riftstorm.admin";
            [JsonProperty("Reward permission (blank = everyone)")] public string Reward = "";
        }

        public class PerfConfig
        {
            [JsonProperty("Event tick rate (Hz)")] public float TickHz = 2f;
            [JsonProperty("Use object pooling")] public bool Pooling = true;
            [JsonProperty("Cleanup grace after victory (s)")] public float CleanupGrace = 20f;
            [JsonProperty("Hard cap concurrent NPCs")] public int MaxNpcs = 60;
            [JsonProperty("Log performance samples")] public bool LogPerf = true;
        }

        #endregion

        // ====================================================================
        //  DATA MODELS
        // ====================================================================
        #region Models

        public enum RiftPhaseId { Idle = 0, Detection = 1, Storm = 2, Rift = 3, Waves = 4, Objectives = 5, Boss = 6, Victory = 7, Cleanup = 8 }

        public class RiftEvent
        {
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime StartedUtc { get; } = DateTime.UtcNow;
            public DateTime PhaseStartedUtc { get; set; } = DateTime.UtcNow;

            public RiftLocation Location { get; set; }
            public Vector3 Center => Location?.Position ?? Vector3.zero;
            public RiftZone Zone { get; set; }

            public RiftPhaseId Phase { get; set; } = RiftPhaseId.Idle;
            public float CountdownRemaining { get; set; }

            public float Stability { get; set; } = 100f;
            public int CrystalsTotal { get; set; }
            public int CrystalsAlive { get; set; }
            public List<BaseCombatEntity> Crystals { get; } = new List<BaseCombatEntity>();

            public int WaveIndex { get; set; } = -1;
            public int WaveCount { get; set; }

            public BaseCombatEntity Boss { get; set; }
            public bool BossRaged { get; set; }
            public float BossMaxHp { get; set; }

            public HashSet<ulong> Participants { get; } = new HashSet<ulong>();
            public Dictionary<ulong, float> DamageByPlayer { get; } = new Dictionary<ulong, float>();
            public Dictionary<ulong, float> SecondsInZone { get; } = new Dictionary<ulong, float>();

            public float BossHpFraction =>
                Boss != null && !Boss.IsDestroyed && BossMaxHp > 0f
                    ? Mathf.Clamp01(Boss.Health() / BossMaxHp) : 0f;

            public void AddDamage(ulong id, float amount)
            {
                DamageByPlayer.TryGetValue(id, out var cur);
                DamageByPlayer[id] = cur + amount;
            }
        }

        public class RiftLocation
        {
            [JsonProperty("Name")] public string Name = "Launch Site";
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Mode (monument|fixed|wilderness)")] public string Mode = "monument";
            [JsonProperty("Monument match")] public string MonumentMatch = "launch_site";
            [JsonProperty("Fixed X")] public float X;
            [JsonProperty("Fixed Y")] public float Y;
            [JsonProperty("Fixed Z")] public float Z;
            [JsonIgnore] public Vector3 Position;

            public static List<RiftLocation> Defaults() => new List<RiftLocation>
            {
                new RiftLocation { Name = "Launch Site",      MonumentMatch = "launch_site" },
                new RiftLocation { Name = "Military Tunnels", MonumentMatch = "military_tunnel" },
                new RiftLocation { Name = "Power Plant",      MonumentMatch = "powerplant" },
                new RiftLocation { Name = "Airfield",         MonumentMatch = "airfield" },
                new RiftLocation { Name = "Harbor",           MonumentMatch = "harbor" },
                new RiftLocation { Name = "Train Yard",       MonumentMatch = "trainyard" },
                new RiftLocation { Name = "Arctic Base",      MonumentMatch = "arctic_base" },
                new RiftLocation { Name = "Random Wilderness", Mode = "wilderness" },
            };
        }

        public class NpcProfile
        {
            [JsonProperty("Display name")] public string Name = "Rift Scientist";
            [JsonProperty("Count per wave")] public int Count = 4;
            [JsonProperty("Health")] public float Health = 150f;
            [JsonProperty("Prefab")] public string Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
            [JsonProperty("Weapon shortname")] public string Weapon = "rifle.ak";
            [JsonProperty("Wear item shortnames")] public List<string> Wear = new List<string> { "metal.facemask", "metal.plate.torso" };
            [JsonProperty("Damage scale")] public float DamageScale = 1f;
            [JsonProperty("Loot table")] public LootTable Loot = LootTable.DefaultScientist();

            public static List<NpcProfile> DefaultWaves() => new List<NpcProfile>
            {
                new NpcProfile { Name = "Rift Scientist", Count = 5, Health = 150f },
                new NpcProfile { Name = "Rift Heavy Scientist", Count = 4, Health = 350f, DamageScale = 1.2f,
                    Wear = new List<string> { "heavy.plate.helmet", "heavy.plate.jacket", "heavy.plate.pants" } },
                new NpcProfile { Name = "Rift Elite Scientist", Count = 3, Health = 600f, DamageScale = 1.4f, Weapon = "rifle.l96" },
            };

            public static NpcProfile DefaultBoss() => new NpcProfile
            {
                Name = "Rift Overlord", Count = 1, Health = 8000f, Weapon = "rifle.ak",
                DamageScale = 1.6f, Loot = LootTable.DefaultBoss(),
            };
        }

        public class LootEntry
        {
            [JsonProperty("Item shortname")] public string Shortname;
            [JsonProperty("Min amount")] public int Min = 1;
            [JsonProperty("Max amount")] public int Max = 1;
            [JsonProperty("Chance (0-1)")] public float Chance = 1f;
            [JsonProperty("Skin id")] public ulong Skin = 0;
            [JsonProperty("Custom name")] public string CustomName = "";
        }

        public class LootTable
        {
            [JsonProperty("Min rolls")] public int MinRolls = 2;
            [JsonProperty("Max rolls")] public int MaxRolls = 4;
            [JsonProperty("Entries")] public List<LootEntry> Entries = new List<LootEntry>();

            public static LootTable DefaultScientist() => new LootTable
            {
                MinRolls = 1, MaxRolls = 2, Entries = new List<LootEntry>
                {
                    new LootEntry { Shortname = "scrap", Min = 20, Max = 60, Chance = 1f },
                    new LootEntry { Shortname = "rifle.ak", Chance = 0.05f },
                    new LootEntry { Shortname = "metal.refined", Min = 5, Max = 15, Chance = 0.5f },
                }
            };

            public static LootTable DefaultBoss() => new LootTable
            {
                MinRolls = 4, MaxRolls = 6, Entries = new List<LootEntry>
                {
                    new LootEntry { Shortname = "metal.refined", Min = 100, Max = 250, Chance = 1f },
                    new LootEntry { Shortname = "explosive.timed", Min = 2, Max = 5, Chance = 0.6f },
                    new LootEntry { Shortname = "rifle.l96", Chance = 0.25f },
                }
            };

            public static LootTable DefaultCrate() => new LootTable
            {
                MinRolls = 5, MaxRolls = 8, Entries = new List<LootEntry>
                {
                    new LootEntry { Shortname = "metal.refined", Min = 250, Max = 500, Chance = 1f },
                    new LootEntry { Shortname = "rifle.ak", Min = 1, Max = 1, Chance = 0.5f },
                    new LootEntry { Shortname = "techparts", Min = 5, Max = 20, Chance = 0.8f },
                    new LootEntry { Shortname = "metal.fragments", Min = 2000, Max = 5000, Chance = 1f },
                    new LootEntry { Shortname = "supply.signal", Min = 1, Max = 2, Chance = 0.4f },
                }
            };
        }

        public enum NoticeType { Detected, StormActive, RiftOpen, WaveStart, BossSpawned, BossDefeated, Finished, Error }

        public class RiftNotice
        {
            public NoticeType Type;
            public string Title;
            public string Message;
            public string LocationName;
            public DateTime TimestampUtc = DateTime.UtcNow;
            public Dictionary<string, string> Fields = new Dictionary<string, string>();
        }

        #endregion

        // ====================================================================
        //  CORE  (context / logger / pool / zone)
        // ====================================================================
        #region Core

        // Lightweight DI container — built once, passed to every manager.
        public class RiftContext
        {
            public RiftStorm Plugin { get; }
            public Configuration Config { get; }
            public RiftLogger Logger { get; }
            public EntityPool Pool { get; }
            public Action<string> Broadcast { get; }

            public WeatherController Weather;
            public RiftController Rift;
            public NpcManager Npcs;
            public BossController Boss;
            public LootManager Loot;
            public UiManager Ui;
            public DiscordService Discord;
            public ApiService Api;

            public RiftContext(RiftStorm plugin, Configuration config, RiftLogger logger,
                               EntityPool pool, Action<string> broadcast)
            {
                Plugin = plugin; Config = config; Logger = logger; Pool = pool; Broadcast = broadcast;
            }

            // one-shot timer convenience (managers self-rearm + guard with flags)
            public void After(float seconds, Action cb) => Plugin.timer.Once(seconds, cb);
            public float TickDelta => 1f / Mathf.Max(0.5f, Config.Performance.TickHz);
        }

        public class RiftLogger
        {
            private readonly RiftStorm plugin;
            private readonly Configuration cfg;
            private readonly Dictionary<string, double> perf = new Dictionary<string, double>();

            public RiftLogger(RiftStorm plugin, Configuration cfg) { this.plugin = plugin; this.cfg = cfg; }

            public void Info(string m) => plugin.Puts($"[INFO] {m}");
            public void Warn(string m) => plugin.PrintWarning($"[WARN] {m}");
            public void Error(string m) => plugin.PrintError($"[ERROR] {m}");
            public void Reward(string m) => plugin.Puts($"[REWARD] {m}");
            public void Kill(string m) => plugin.Puts($"[KILL] {m}");

            public IDisposable Perf(string label) => new PerfScope(this, label);
            internal void Record(string label, double ms)
            {
                perf[label] = ms;
                if (cfg.Performance.LogPerf && ms > 8.0) plugin.Puts($"[PERF] {label} took {ms:0.0}ms");
            }

            public string PerfSummary()
            {
                var sb = new StringBuilder("[PERF] ");
                foreach (var kv in perf) sb.Append($"{kv.Key}={kv.Value:0.0}ms ");
                return sb.ToString();
            }

            private sealed class PerfScope : IDisposable
            {
                private readonly RiftLogger log; private readonly string label;
                private readonly System.Diagnostics.Stopwatch sw;
                public PerfScope(RiftLogger log, string label) { this.log = log; this.label = label; sw = System.Diagnostics.Stopwatch.StartNew(); }
                public void Dispose() { sw.Stop(); log.Record(label, sw.Elapsed.TotalMilliseconds); }
            }
        }

        // Authoritative registry of everything we spawned → guaranteed cleanup.
        public class EntityPool
        {
            private readonly RiftLogger log;
            private readonly HashSet<BaseEntity> tracked = new HashSet<BaseEntity>();
            public EntityPool(RiftLogger log) { this.log = log; }

            public void Track(BaseEntity e) { if (e != null) tracked.Add(e); }

            public void CleanupAll(RiftEvent ev)
            {
                int killed = 0;
                foreach (var e in tracked)
                {
                    try { if (e != null && !e.IsDestroyed) { e.Kill(); killed++; } }
                    catch (Exception ex) { log.Error($"cleanup kill failed: {ex.Message}"); }
                }
                tracked.Clear();
                ev?.Crystals.Clear();
                log.Info($"Cleanup complete — {killed} entities removed.");
            }

            public string DebugSummary() => $"[POOL] tracked entities: {tracked.Count}";
        }

        public class RiftZone
        {
            public Vector3 Center { get; }
            public float Radius { get; }
            private readonly float sqr;

            public RiftZone(Vector3 center, float radius) { Center = center; Radius = radius; sqr = radius * radius; }
            public bool Contains(Vector3 p) => (p - Center).sqrMagnitude <= sqr;

            public List<BasePlayer> PlayersInside()
            {
                var list = new List<BasePlayer>();
                foreach (var pl in BasePlayer.activePlayerList)
                    if (pl != null && !pl.IsNpc && !pl.IsDead() && Contains(pl.transform.position))
                        list.Add(pl);
                return list;
            }
        }

        #endregion

        // ====================================================================
        //  INTERFACES
        // ====================================================================
        #region Interfaces

        public interface IRiftPhase
        {
            RiftPhaseId Id { get; }
            void Enter(RiftEvent ev, RiftContext ctx);
            void Tick(RiftEvent ev, RiftContext ctx);
            void Exit(RiftEvent ev, RiftContext ctx);
            bool IsComplete(RiftEvent ev);
        }

        public interface INotifier { void Notify(RiftNotice notice); }
        public interface IRewardGrant { void Grant(BasePlayer player, RiftContext ctx, bool topDamage); }

        #endregion

        // ====================================================================
        //  EVENT MANAGER  (scheduler + phase state machine)
        // ====================================================================
        #region EventManager

        public class RiftEventManager
        {
            private readonly RiftContext ctx;
            private readonly List<IRiftPhase> phases;
            private int phaseIndex = -1;
            private int generation;          // bumped each event → stale callbacks no-op

            public RiftEvent Active { get; private set; }
            public bool IsRunning => Active != null && Active.Phase != RiftPhaseId.Idle;

            public RiftEventManager(RiftContext ctx)
            {
                this.ctx = ctx;
                phases = new List<IRiftPhase>
                {
                    new DetectionPhase(),
                    new StormPhase(),
                    new RiftSpawnPhase(),
                    new WavesPhase(),
                    new ObjectivesPhase(),
                    new BossPhase(),
                    new VictoryPhase(),
                };

                ctx.Rift.CrystalDestroyed += OnCrystalDestroyed;
            }

            public void ScheduleNext()
            {
                if (!ctx.Config.Schedule.Enabled) return;
                float hrs = UnityEngine.Random.Range(ctx.Config.Schedule.MinHours, ctx.Config.Schedule.MaxHours);
                ctx.Logger.Info($"Next Rift Storm scheduled in {hrs:0.00}h");
                int gen = ++generation;
                ctx.After(hrs * 3600f, () => { if (gen == generation) TryAutoStart(); });
            }

            private void TryAutoStart()
            {
                if (IsRunning) { ScheduleNext(); return; }
                if (BasePlayer.activePlayerList.Count < ctx.Config.Schedule.MinPlayers)
                {
                    ctx.Logger.Info("Not enough players online; rescheduling.");
                    ScheduleNext();
                    return;
                }
                var loc = PickByNameOrRandom(null);
                if (loc == null) { ctx.Logger.Warn("No valid location; rescheduling."); ScheduleNext(); return; }
                StartEvent(loc);
            }

            public bool StartEvent(RiftLocation loc)
            {
                if (IsRunning) return false;
                generation++;                       // invalidate any pending schedule
                Active = new RiftEvent { Location = loc, Zone = new RiftZone(loc.Position, ctx.Config.Zone.Radius) };
                phaseIndex = -1;
                ctx.Logger.Info($"Rift Storm starting at {loc.Name} {loc.Position}");
                ctx.Api.Push(Active);
                StartTick();
                Advance();
                return true;
            }

            public void StopEvent(bool victory)
            {
                if (Active == null) return;
                ctx.Logger.Info($"Rift Storm ending (victory={victory}) after {(DateTime.UtcNow - Active.StartedUtc).TotalMinutes:0.0}m");
                try { CurrentPhase?.Exit(Active, ctx); } catch (Exception ex) { ctx.Logger.Error($"phase exit: {ex.Message}"); }
                Cleanup();
                Active.Phase = RiftPhaseId.Idle;
                Active = null;
                generation++;                        // stop the tick re-arm
                ScheduleNext();
            }

            private IRiftPhase CurrentPhase =>
                (phaseIndex >= 0 && phaseIndex < phases.Count) ? phases[phaseIndex] : null;

            private void Advance()
            {
                try { CurrentPhase?.Exit(Active, ctx); } catch (Exception ex) { ctx.Logger.Error($"exit: {ex.Message}"); }
                phaseIndex++;
                if (phaseIndex >= phases.Count) { StopEvent(true); return; }
                var next = phases[phaseIndex];
                Active.Phase = next.Id;
                Active.PhaseStartedUtc = DateTime.UtcNow;
                ctx.Logger.Info($"-> Phase {next.Id}");
                try { next.Enter(Active, ctx); } catch (Exception ex) { ctx.Logger.Error($"enter {next.Id}: {ex.Message}"); }
                ctx.Api.Push(Active);
            }

            private void StartTick()
            {
                int gen = generation;
                void Loop()
                {
                    if (Active == null || gen != generation) return;
                    Tick();
                    ctx.After(ctx.TickDelta, Loop);
                }
                ctx.After(ctx.TickDelta, Loop);
            }

            private void Tick()
            {
                if (Active == null) return;
                using (ctx.Logger.Perf("tick"))
                {
                    var phase = CurrentPhase;
                    if (phase == null) return;
                    UpdateParticipation();
                    try { phase.Tick(Active, ctx); } catch (Exception ex) { ctx.Logger.Error($"tick {phase.Id}: {ex.Message}"); }
                    ctx.Ui.Refresh(Active);
                    if (phase.IsComplete(Active)) Advance();
                }
            }

            private void OnCrystalDestroyed(BaseCombatEntity crystal)
            {
                if (Active == null) return;
                Active.CrystalsAlive = Mathf.Max(0, Active.CrystalsAlive - 1);
                Active.Stability = Mathf.Max(0f, Active.Stability - ctx.Config.Crystals.StabilityPerCrystal);
                ctx.Logger.Info($"Crystal destroyed — stability {Active.Stability}%");
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} A crystal shatters — portal stability {Active.Stability:0}%");
            }

            private void UpdateParticipation()
            {
                float dt = ctx.TickDelta;
                foreach (var pl in Active.Zone.PlayersInside())
                {
                    Active.Participants.Add(pl.userID);
                    Active.SecondsInZone.TryGetValue(pl.userID, out var s);
                    Active.SecondsInZone[pl.userID] = s + dt;
                }
            }

            private void Cleanup()
            {
                try { ctx.Weather.Restore(); } catch { }
                try { ctx.Npcs.ClearAll(); } catch { }
                try { ctx.Rift.Despawn(); } catch { }
                try { ctx.Boss.Reset(); } catch { }
                try { ctx.Ui.DestroyAll(); } catch { }
                ctx.Pool.CleanupAll(Active);
            }

            // ---- helpers ------------------------------------------------------
            public RiftLocation PickByNameOrRandom(string name)
            {
                var enabled = ctx.Config.Locations.FindAll(l => l.Enabled);
                if (enabled.Count == 0) return null;
                RiftLocation loc;
                if (!string.IsNullOrEmpty(name))
                {
                    loc = enabled.Find(l => l.Name.ToLower().Contains(name.ToLower()));
                    if (loc == null) return null;
                }
                else loc = enabled[UnityEngine.Random.Range(0, enabled.Count)];
                return ResolveLocation(loc) ? loc : null;
            }

            private bool ResolveLocation(RiftLocation loc)
            {
                switch (loc.Mode)
                {
                    case "fixed":
                        loc.Position = new Vector3(loc.X, loc.Y, loc.Z);
                        return true;
                    case "wilderness":
                        return TryRandomWilderness(out loc.Position);
                    default:
                        if (TerrainMeta.Path?.Monuments != null)
                            foreach (var mon in TerrainMeta.Path.Monuments)
                            {
                                var n = ((mon.displayPhrase.english ?? "") + (mon.name ?? "")).ToLower();
                                if (n.Contains(loc.MonumentMatch.ToLower()))
                                { loc.Position = mon.transform.position; return true; }
                            }
                        return false;
                }
            }

            private bool TryRandomWilderness(out Vector3 pos)
            {
                for (int i = 0; i < 30; i++)
                {
                    float size = TerrainMeta.Size.x * 0.5f * 0.9f;
                    var p = new Vector3(UnityEngine.Random.Range(-size, size), 0, UnityEngine.Random.Range(-size, size));
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    if (p.y > 1f) { pos = p; return true; }
                }
                pos = Vector3.zero; return false;
            }

            public string StatusLine()
            {
                if (!IsRunning) return "No active Rift Storm.";
                var ev = Active;
                return $"Phase: {ev.Phase} | Loc: {ev.Location.Name} | Stability: {ev.Stability:0}% | " +
                       $"Crystals: {ev.CrystalsAlive}/{ev.CrystalsTotal} | Wave: {ev.WaveIndex + 1}/{ev.WaveCount} | " +
                       $"Boss: {ev.BossHpFraction * 100f:0}% | Players: {ev.Zone.PlayersInside().Count}";
            }
        }

        #endregion

        // ====================================================================
        //  PHASES
        // ====================================================================
        #region Phases

        public class DetectionPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Detection;
            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                ev.CountdownRemaining = ctx.Config.Schedule.CountdownSeconds;
                ctx.Broadcast($"⚡ <b>RIFT STORM DETECTED</b>\nAn unstable dimensional rift has appeared.\nLocation: <b>{ev.Location.Name}</b>\nStarts in {Mathf.RoundToInt(ev.CountdownRemaining)}s.");
                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.Detected, Title = "⚡ Rift Storm Detected",
                    Message = "An unstable dimensional rift has appeared.", LocationName = ev.Location.Name });
                foreach (var pl in BasePlayer.activePlayerList)
                    Effect.server.Run(ctx.Config.Announce.WarnSound, pl.transform.position);
            }
            public void Tick(RiftEvent ev, RiftContext ctx) => ev.CountdownRemaining -= ctx.TickDelta;
            public bool IsComplete(RiftEvent ev) => ev.CountdownRemaining <= 0f;
            public void Exit(RiftEvent ev, RiftContext ctx) =>
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} The Rift Storm is here. Brace yourselves.");
        }

        public class StormPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Storm;
            private const float Duration = 12f;
            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                ctx.Weather.RampUp(ev);
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} The storm intensifies...");
                foreach (var pl in BasePlayer.activePlayerList)
                    Effect.server.Run(ctx.Config.Announce.StormSound, pl.transform.position);
                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.StormActive, Title = "🌩️ Storm Active",
                    Message = "The dimensional storm is raging.", LocationName = ev.Location.Name });
            }
            public void Tick(RiftEvent ev, RiftContext ctx) { }
            public bool IsComplete(RiftEvent ev) => (DateTime.UtcNow - ev.PhaseStartedUtc).TotalSeconds >= Duration;
            public void Exit(RiftEvent ev, RiftContext ctx) { }
        }

        public class RiftSpawnPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Rift;
            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                ctx.Rift.SpawnRift(ev.Center);
                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.RiftOpen, Title = "🌀 Rift Open",
                    Message = "A massive rift has torn open.", LocationName = ev.Location.Name });
            }
            public void Tick(RiftEvent ev, RiftContext ctx) { }
            public bool IsComplete(RiftEvent ev) => (DateTime.UtcNow - ev.PhaseStartedUtc).TotalSeconds >= 5;
            public void Exit(RiftEvent ev, RiftContext ctx) { }
        }

        public class WavesPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Waves;
            private int nextWave;
            private bool finished;
            private Action<int> handler;

            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                nextWave = 0; finished = false;
                handler = idx =>
                {
                    if (nextWave >= ctx.Config.Waves.Profiles.Count) { finished = true; return; }
                    ctx.After(ctx.Config.Waves.WaveDelay, () => SpawnNext(ev, ctx));
                };
                ctx.Npcs.WaveCleared += handler;
                SpawnNext(ev, ctx);
            }
            private void SpawnNext(RiftEvent ev, RiftContext ctx)
            {
                if (ev.Phase != RiftPhaseId.Waves) return;
                ctx.Npcs.SpawnWave(ev, nextWave);
                nextWave++;
            }
            public void Tick(RiftEvent ev, RiftContext ctx) { }
            public bool IsComplete(RiftEvent ev) => finished;
            public void Exit(RiftEvent ev, RiftContext ctx)
            {
                if (handler != null) ctx.Npcs.WaveCleared -= handler;
                ctx.Npcs.ClearAll();
            }
        }

        public class ObjectivesPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Objectives;
            private bool spawned;
            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                ctx.Rift.SpawnCrystals(ev);
                spawned = true;
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} Destroy the {ev.CrystalsTotal} Energy Crystals to collapse the portal!");
            }
            public void Tick(RiftEvent ev, RiftContext ctx) { }
            public bool IsComplete(RiftEvent ev) => spawned && ev.CrystalsAlive <= 0;
            public void Exit(RiftEvent ev, RiftContext ctx)
            {
                ev.Stability = 0f;
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} Portal stability collapsed — the Overlord emerges!");
            }
        }

        public class BossPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Boss;
            private bool dead;
            private Action handler;
            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                dead = false;
                handler = () => dead = true;
                ctx.Boss.BossDefeated += handler;
                ctx.Boss.SpawnBoss(ev);
                ctx.Ui.ForceRedraw();
            }
            public void Tick(RiftEvent ev, RiftContext ctx) { }
            public bool IsComplete(RiftEvent ev) => dead;
            public void Exit(RiftEvent ev, RiftContext ctx)
            {
                if (handler != null) ctx.Boss.BossDefeated -= handler;
                ctx.Ui.HideBossBarAll();
            }
        }

        public class VictoryPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Victory;
            private bool done;
            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                done = false;
                ctx.Rift.Explode(ev.Center);
                Fireworks(ev.Center, ctx);
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} <color=#00E5FF>THE RIFT OVERLORD HAS FALLEN!</color> The portal collapses!");
                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.BossDefeated, Title = "🏆 Victory",
                    Message = "The Rift Overlord has been defeated!", LocationName = ev.Location.Name });

                ctx.Npcs.ClearAll();
                ctx.Loot.SpawnRiftCrate(ev.Center);
                ctx.Loot.GrantRewards(ev);

                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.Finished, Title = "✅ Event Finished",
                    Message = "Rift Crate has dropped. Rewards distributed.", LocationName = ev.Location.Name });

                ctx.After(ctx.Config.Performance.CleanupGrace, () => done = true);
            }
            private void Fireworks(Vector3 c, RiftContext ctx)
            {
                for (int i = 0; i < 8; i++)
                {
                    var off = UnityEngine.Random.insideUnitCircle * 15f;
                    Effect.server.Run("assets/prefabs/deployable/fireworks/volcanofirework.prefab", c + new Vector3(off.x, 12f, off.y));
                }
            }
            public void Tick(RiftEvent ev, RiftContext ctx) { }
            public bool IsComplete(RiftEvent ev) => done;
            public void Exit(RiftEvent ev, RiftContext ctx) { }
        }

        #endregion

        // ====================================================================
        //  WEATHER CONTROLLER
        // ====================================================================
        #region WeatherController

        public class WeatherController
        {
            public const string FilterName = "riftstorm.purplefilter";

            private readonly RiftContext ctx;
            private bool active;
            private Vector3 center;

            public WeatherController(RiftContext ctx) { this.ctx = ctx; }

            public bool IsActive => active;

            public void RampUp(RiftEvent ev)
            {
                if (!ctx.Config.Weather.Enabled) return;
                active = true; center = ev.Center;
                var w = ctx.Config.Weather;
                Set("weather.rain", w.Rain);
                Set("weather.fog", w.Fog);
                Set("weather.clouds", w.Clouds);
                Set("weather.wind", w.Wind);
                ctx.Logger.Info("Weather override applied (storm).");
                if (w.Thunder) StartThunder();
                SpawnAtmosphere(ev.Center);
                ShowFilterAll();
            }

            // Server-wide purple screen tint — a full-screen translucent overlay on
            // every player's HUD so the whole world reads as purple during the storm.
            public void ShowFilterAll()
            {
                if (!ctx.Config.Weather.ScreenFilter) return;
                foreach (var pl in BasePlayer.activePlayerList) ShowFilter(pl);
            }

            public void ShowFilter(BasePlayer pl)
            {
                if (pl == null || !ctx.Config.Weather.ScreenFilter) return;
                var w = ctx.Config.Weather;
                var rgb = HexToColor(w.FilterColor);
                float a = Mathf.Clamp01(w.FilterStrength);
                var ic = CultureInfo.InvariantCulture;
                string F(float v) => v.ToString("0.###", ic);
                string baseCol = $"{F(rgb.r)} {F(rgb.g)} {F(rgb.b)} {F(a)}";
                string glowCol = $"0.69 0.15 1.0 {F(a * 0.6f)}";

                var c = new CuiElementContainer();
                // full-screen base tint
                c.Add(new CuiPanel
                {
                    Image = { Color = baseCol },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = false
                }, "Overlay", FilterName);
                // stronger glow band across the top for a stormy mood
                c.Add(new CuiPanel
                {
                    Image = { Color = glowCol },
                    RectTransform = { AnchorMin = "0 0.55", AnchorMax = "1 1" },
                    CursorEnabled = false
                }, FilterName);

                CuiHelper.DestroyUi(pl, FilterName);
                CuiHelper.AddUi(pl, c);
            }

            public void HideFilterAll()
            {
                foreach (var pl in BasePlayer.activePlayerList) CuiHelper.DestroyUi(pl, FilterName);
            }

            private void Set(string convar, float value) =>
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), convar, value);

            private void StartThunder()
            {
                void Strike()
                {
                    if (!active) return;
                    var w = ctx.Config.Weather;
                    foreach (var pl in BasePlayer.activePlayerList)
                        Effect.server.Run(w.LightningPrefab, pl.transform.position + Vector3.up * 30f);
                    ctx.After(UnityEngine.Random.Range(w.ThunderMin, w.ThunderMax), Strike);
                }
                ctx.After(ctx.Config.Weather.ThunderMin, Strike);
            }

            public void SpawnAtmosphere(Vector3 c)
            {
                var w = ctx.Config.Weather;
                int n = Mathf.Max(0, w.LightCount);
                for (int i = 0; i < n; i++)
                {
                    float a = (360f / n) * i * Mathf.Deg2Rad;
                    var pos = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * w.RingRadius;
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 1f;
                    Effect.server.Run(w.SmokePrefab, pos);

                    try
                    {
                        var light = GameManager.server.CreateEntity(
                            "assets/prefabs/deployable/playerioents/lights/simplelight/electric.simplelight.deployed.prefab",
                            pos + Vector3.up * 1.2f);
                        if (light != null)
                        {
                            light.Spawn();
                            var comp = light.gameObject.GetComponent<Light>() ?? light.gameObject.AddComponent<Light>();
                            comp.color = HexToColor("#B026FF");
                            comp.range = 15f; comp.intensity = 3f;
                            ctx.Pool.Track(light);
                        }
                    }
                    catch (Exception ex) { ctx.Logger.Warn($"atmosphere light: {ex.Message}"); }
                }
            }

            public void Restore()
            {
                if (!active) return;
                active = false;
                Set("weather.rain", -1);
                Set("weather.fog", -1);
                Set("weather.clouds", -1);
                Set("weather.wind", -1);
                HideFilterAll();
                ctx.Logger.Info("Weather restored.");
            }

            public static Color HexToColor(string hex)
            {
                ColorUtility.TryParseHtmlString(hex, out var c);
                return c;
            }
        }

        #endregion

        // ====================================================================
        //  RIFT CONTROLLER  (rift visuals + crystals)
        // ====================================================================
        #region RiftController

        public class RiftController
        {
            private readonly RiftContext ctx;
            private Vector3 center;
            private bool pulsing;

            public event Action<BaseCombatEntity> CrystalDestroyed;

            public RiftController(RiftContext ctx) { this.ctx = ctx; }

            public void SpawnRift(Vector3 at)
            {
                center = at;
                Effect.server.Run("assets/bundled/prefabs/fx/tesla/discharge_setup.prefab", at + Vector3.up * 2f);
                pulsing = true;
                StartPulse();
                ctx.Logger.Info("Rift spawned + pulsing.");
            }

            private void StartPulse()
            {
                void Pulse()
                {
                    if (!pulsing) return;
                    Effect.server.Run(ctx.Config.Weather.LightningPrefab, center + Vector3.up * 3f);
                    Effect.server.Run(ctx.Config.Crystals.ParticlePrefab, center + Vector3.up * 1.5f);
                    ctx.After(3f, Pulse);
                }
                ctx.After(3f, Pulse);
            }

            public void SpawnCrystals(RiftEvent ev)
            {
                int count = ctx.Config.Crystals.Count;
                ev.CrystalsTotal = count;
                ev.CrystalsAlive = count;
                ev.Stability = 100f;
                for (int i = 0; i < count; i++)
                {
                    float a = (360f / count) * i * Mathf.Deg2Rad;
                    var pos = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ctx.Config.Crystals.Radius;
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.5f;
                    try
                    {
                        var ent = GameManager.server.CreateEntity(ctx.Config.Crystals.EntityPrefab, pos) as BaseCombatEntity;
                        if (ent == null) continue;
                        ent.Spawn();
                        ent.InitializeHealth(ctx.Config.Crystals.Health, ctx.Config.Crystals.Health);
                        ent.gameObject.AddComponent<RiftCrystalTag>();
                        Effect.server.Run(ctx.Config.Crystals.ParticlePrefab, pos);
                        ev.Crystals.Add(ent);
                        ctx.Pool.Track(ent);
                    }
                    catch (Exception ex) { ctx.Logger.Error($"crystal spawn: {ex.Message}"); }
                }
                ctx.Logger.Info($"{count} Energy Crystals spawned.");
            }

            public void NotifyCrystalDestroyed(RiftEvent ev, BaseCombatEntity crystal)
            {
                Effect.server.Run(ctx.Config.Crystals.ExplosionPrefab, crystal.transform.position);
                Effect.server.Run(ctx.Config.Crystals.ParticlePrefab, crystal.transform.position);
                ev.Crystals.Remove(crystal);
                CrystalDestroyed?.Invoke(crystal);
            }

            public void Explode(Vector3 at)
            {
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", at);
                Effect.server.Run(ctx.Config.Crystals.ParticlePrefab, at);
            }

            public void Despawn() => pulsing = false;
        }

        #endregion

        // ====================================================================
        //  NPC MANAGER  (waves)
        // ====================================================================
        #region NpcManager

        public class NpcManager
        {
            private readonly RiftContext ctx;
            private readonly HashSet<BaseCombatEntity> alive = new HashSet<BaseCombatEntity>();
            private int currentWave = -1;
            private bool watching;

            public event Action<int> WaveCleared;

            public NpcManager(RiftContext ctx) { this.ctx = ctx; }

            public int AliveCount() { alive.RemoveWhere(n => n == null || n.IsDestroyed); return alive.Count; }

            public void SpawnWave(RiftEvent ev, int waveIndex)
            {
                var profiles = ctx.Config.Waves.Profiles;
                if (waveIndex < 0 || waveIndex >= profiles.Count) return;
                currentWave = waveIndex;
                ev.WaveIndex = waveIndex;
                ev.WaveCount = profiles.Count;
                var profile = profiles[waveIndex];

                int count = ComputeCount(profile);
                for (int i = 0; i < count && AliveCount() < ctx.Config.Performance.MaxNpcs; i++)
                    SpawnOne(ev, profile, profile.DamageScale, false);

                ctx.Broadcast($"{ctx.Config.Announce.Prefix} Wave {waveIndex + 1}/{profiles.Count}: {profile.Name} x{count}");
                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.WaveStart, Title = $"⚔️ Wave {waveIndex + 1}",
                    Message = $"{profile.Name} x{count}", LocationName = ev.Location.Name });
                StartWatch(waveIndex);
            }

            private int ComputeCount(NpcProfile p)
            {
                if (!ctx.Config.Waves.ScaleWithPlayers) return p.Count;
                int online = BasePlayer.activePlayerList.Count;
                int extra = online / Mathf.Max(1, ctx.Config.Waves.PlayersPerExtraNpc);
                return Mathf.Min(p.Count + extra, ctx.Config.Performance.MaxNpcs);
            }

            private void SpawnOne(RiftEvent ev, NpcProfile profile, float dmgScale, bool isSummon)
            {
                var pos = RandomRing(ev.Center, ctx.Config.Waves.SpawnRadius);
                try
                {
                    var npc = GameManager.server.CreateEntity(profile.Prefab, pos) as BaseCombatEntity;
                    if (npc == null) return;
                    npc.Spawn();
                    npc.InitializeHealth(profile.Health, profile.Health);
                    var tag = npc.gameObject.AddComponent<RiftNpcTag>();
                    tag.WaveIndex = currentWave;
                    tag.DamageScale = dmgScale;
                    tag.Loot = profile.Loot;
                    EquipNpc(npc as BasePlayer, profile);
                    alive.Add(npc);
                    ctx.Pool.Track(npc);
                }
                catch (Exception ex) { ctx.Logger.Error($"npc spawn: {ex.Message}"); }
            }

            // Best-effort gear. Scientists carry their own loadout; this layers ours on top.
            private void EquipNpc(BasePlayer npc, NpcProfile p)
            {
                if (npc == null) return;
                try
                {
                    if (!string.IsNullOrEmpty(p.Weapon))
                    {
                        var item = ItemManager.CreateByName(p.Weapon, 1);
                        item?.MoveToContainer(npc.inventory.containerBelt);
                    }
                    foreach (var w in p.Wear)
                    {
                        var wi = ItemManager.CreateByName(w, 1);
                        wi?.MoveToContainer(npc.inventory.containerWear);
                    }
                }
                catch (Exception ex) { ctx.Logger.Warn($"equip npc: {ex.Message}"); }
            }

            private void StartWatch(int waveIndex)
            {
                watching = true;
                void Check()
                {
                    if (!watching) return;
                    if (AliveCount() > 0) { ctx.After(2f, Check); return; }
                    watching = false;
                    WaveCleared?.Invoke(waveIndex);
                }
                ctx.After(2f, Check);
            }

            public void Summon(RiftEvent ev, NpcProfile profile, int n)
            {
                for (int i = 0; i < n && AliveCount() < ctx.Config.Performance.MaxNpcs; i++)
                    SpawnOne(ev, profile, profile.DamageScale, true);
            }

            // called from OnEntityDeath
            public void OnNpcDeath(BaseCombatEntity npc, RiftNpcTag tag)
            {
                alive.Remove(npc);
                ctx.Loot.FillCorpse(npc, tag.Loot);
                ctx.Logger.Kill($"NPC down (wave {tag.WaveIndex + 1}), {AliveCount()} remain");
            }

            public void ClearAll()
            {
                watching = false;
                foreach (var n in alive) { try { if (n != null && !n.IsDestroyed) n.Kill(); } catch { } }
                alive.Clear();
            }

            private Vector3 RandomRing(Vector3 c, float r)
            {
                var a = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                p.y = TerrainMeta.HeightMap.GetHeight(p) + 0.5f;
                return p;
            }
        }

        #endregion

        // ====================================================================
        //  BOSS CONTROLLER  (Rift Overlord)
        // ====================================================================
        #region BossController

        public class BossController
        {
            private readonly RiftContext ctx;
            private RiftEvent ev;
            private BaseCombatEntity boss;
            private readonly Dictionary<string, float> cd = new Dictionary<string, float>();
            private bool running;
            private bool raged;

            public event Action BossDefeated;

            public BossController(RiftContext ctx) { this.ctx = ctx; }

            public void SpawnBoss(RiftEvent ev)
            {
                this.ev = ev;
                var p = ctx.Config.Boss.Profile;
                var pos = ev.Center; pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.5f;
                try
                {
                    boss = GameManager.server.CreateEntity(p.Prefab, pos) as BaseCombatEntity;
                    if (boss == null) { ctx.Logger.Error("Boss prefab failed to spawn"); return; }
                    boss.Spawn();
                    boss.InitializeHealth(ctx.Config.Boss.Health, ctx.Config.Boss.Health);
                    boss.gameObject.AddComponent<RiftBossTag>();
                    ev.Boss = boss;
                    ev.BossMaxHp = ctx.Config.Boss.Health;
                    ctx.Pool.Track(boss);
                }
                catch (Exception ex) { ctx.Logger.Error($"boss spawn: {ex.Message}"); return; }

                ctx.Broadcast($"{ctx.Config.Announce.Prefix} <color=#B026FF>{ctx.Config.Boss.Name}</color> has emerged!");
                ctx.Discord.Notify(new RiftNotice { Type = NoticeType.BossSpawned, Title = "👑 Rift Overlord",
                    Message = $"{ctx.Config.Boss.Name} has emerged with {ctx.Config.Boss.Health:0} HP.", LocationName = ev.Location.Name });

                running = true; raged = false; cd.Clear();
                StartAbilities();
            }

            private void StartAbilities()
            {
                void Loop()
                {
                    if (!running || boss == null || boss.IsDestroyed) return;
                    CheckRage();
                    TryAbility("emp", ctx.Config.Boss.EmpCooldown, EmpPulse);
                    TryAbility("lightning", ctx.Config.Boss.LightningCooldown, LightningStrike);
                    TryAbility("area", ctx.Config.Boss.AreaCooldown, AreaDamage);
                    TryAbility("summon", ctx.Config.Boss.SummonCooldown, SummonReinforcements);
                    ctx.After(1f, Loop);
                }
                ctx.After(2f, Loop);
            }

            private void TryAbility(string key, float baseCd, Action act)
            {
                float mult = raged ? ctx.Config.Boss.RageCdMult : 1f;
                cd.TryGetValue(key, out var ready);
                if (Time.realtimeSinceStartup < ready) return;
                try { act(); } catch (Exception ex) { ctx.Logger.Error($"ability {key}: {ex.Message}"); }
                cd[key] = Time.realtimeSinceStartup + baseCd * mult;
            }

            private void EmpPulse()
            {
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", boss.transform.position);
                foreach (var pl in ev.Zone.PlayersInside())
                {
                    var held = pl.GetActiveItem();
                    if (held?.GetHeldEntity() is BaseProjectile gun) gun.primaryMagazine.contents = 0;
                    pl.ChatMessage("⚡ EMP PULSE — weapons disrupted!");
                }
            }

            private void LightningStrike()
            {
                var players = ev.Zone.PlayersInside();
                if (players.Count == 0) return;
                var target = players[UnityEngine.Random.Range(0, players.Count)];
                Effect.server.Run(ctx.Config.Weather.LightningPrefab, target.transform.position + Vector3.up * 20f);
                ApplyAoe(target.transform.position, 4f, 60f);
            }

            private void AreaDamage()
            {
                Effect.server.Run("assets/bundled/prefabs/fx/tesla/discharge_setup.prefab", boss.transform.position);
                ApplyAoe(boss.transform.position, 10f, 45f);
            }

            private void SummonReinforcements()
            {
                var profiles = ctx.Config.Waves.Profiles;
                if (profiles.Count == 0) return;
                ctx.Npcs.Summon(ev, profiles[profiles.Count - 1], ctx.Config.Boss.SummonCount);
                ctx.Broadcast($"{ctx.Config.Announce.Prefix} The Overlord summons reinforcements!");
            }

            private void ApplyAoe(Vector3 c, float radius, float maxDamage)
            {
                float mult = raged ? ctx.Config.Boss.RageDamageMult : 1f;
                foreach (var pl in ev.Zone.PlayersInside())
                {
                    float d = Vector3.Distance(c, pl.transform.position);
                    if (d > radius) continue;
                    float dmg = Mathf.Lerp(maxDamage, 0f, d / radius) * mult;
                    pl.Hurt(dmg, Rust.DamageType.ElectricShock, boss);
                }
            }

            private void CheckRage()
            {
                if (raged) return;
                if (ev.BossHpFraction * 100f <= ctx.Config.Boss.RageThreshold)
                {
                    raged = true; ev.BossRaged = true;
                    Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", boss.transform.position);
                    ctx.Broadcast($"{ctx.Config.Announce.Prefix} <color=#ff3366>THE OVERLORD ENTERS RAGE MODE!</color>");
                }
            }

            public void NotifyBossDeath() { running = false; BossDefeated?.Invoke(); }
            public void Reset() { running = false; boss = null; raged = false; cd.Clear(); }
        }

        #endregion

        // ====================================================================
        //  LOOT MANAGER
        // ====================================================================
        #region LootManager

        public class LootManager
        {
            private readonly RiftContext ctx;
            private readonly List<IRewardGrant> grants;

            public LootManager(RiftContext ctx)
            {
                this.ctx = ctx;
                grants = new List<IRewardGrant> { new TokenGrant(), new CosmeticGrant(), new TitleGrant() };
            }

            // The NPC corpse entity is not created until just after OnEntityDeath,
            // so defer the search a beat, then fill the nearest fresh corpse.
            public void FillCorpse(BaseCombatEntity npc, LootTable table)
            {
                if (npc == null) return;
                var pos = npc.transform.position;
                ctx.After(0.4f, () =>
                {
                    try
                    {
                        var list = new List<LootableCorpse>();
                        Vis.Entities(pos, 3f, list);
                        LootableCorpse corpse = list.Count > 0 ? list[0] : null;
                        if (corpse?.containers == null || corpse.containers.Length == 0) return;
                        var container = corpse.containers[0];
                        container.Clear();
                        RollInto(container, table);
                    }
                    catch (Exception ex) { ctx.Logger.Warn($"fill corpse: {ex.Message}"); }
                });
            }

            private void RollInto(ItemContainer container, LootTable table)
            {
                if (table?.Entries == null || table.Entries.Count == 0) return;
                int rolls = UnityEngine.Random.Range(table.MinRolls, table.MaxRolls + 1);
                for (int i = 0; i < rolls; i++)
                {
                    var entry = table.Entries[UnityEngine.Random.Range(0, table.Entries.Count)];
                    if (UnityEngine.Random.value > entry.Chance) continue;
                    int amt = UnityEngine.Random.Range(entry.Min, entry.Max + 1);
                    var item = ItemManager.CreateByName(entry.Shortname, amt, entry.Skin);
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(entry.CustomName)) item.name = entry.CustomName;
                    if (!item.MoveToContainer(container)) item.Remove();
                }
            }

            public void SpawnRiftCrate(Vector3 at)
            {
                try
                {
                    at.y = TerrainMeta.HeightMap.GetHeight(at) + 0.5f;
                    var crate = GameManager.server.CreateEntity(ctx.Config.Rewards.CratePrefab, at) as HackableLockedCrate;
                    if (crate == null) { ctx.Logger.Error("Rift Crate prefab failed"); return; }
                    crate.Spawn();
                    crate.inventory.Clear();
                    RollInto(crate.inventory, ctx.Config.Rewards.CrateLoot);
                    crate.StartHacking();
                    ctx.Logger.Info("Rift Crate spawned + filled.");
                    // intentionally NOT pooled — reward persists past cleanup
                }
                catch (Exception ex) { ctx.Logger.Error($"rift crate: {ex.Message}"); }
            }

            public void GrantRewards(RiftEvent ev)
            {
                ulong topId = TopDamage(ev);
                int granted = 0;
                foreach (var id in ev.Participants)
                {
                    if (!Qualifies(ev, id)) continue;
                    var pl = BasePlayer.FindByID(id);
                    if (pl == null) continue;       // offline → extend with data-file queue
                    bool top = id == topId;
                    foreach (var g in grants) { try { g.Grant(pl, ctx, top); } catch (Exception ex) { ctx.Logger.Warn($"grant: {ex.Message}"); } }
                    granted++;
                }
                ctx.Logger.Reward($"Rewards granted to {granted} participants (top={topId}).");
            }

            private bool Qualifies(RiftEvent ev, ulong id)
            {
                ev.SecondsInZone.TryGetValue(id, out var s);
                return s >= ctx.Config.Zone.MinParticipationSeconds;
            }

            private ulong TopDamage(RiftEvent ev)
            {
                ulong best = 0; float max = -1f;
                foreach (var kv in ev.DamageByPlayer) if (kv.Value > max) { max = kv.Value; best = kv.Key; }
                return best;
            }
        }

        public class TokenGrant : IRewardGrant
        {
            public void Grant(BasePlayer pl, RiftContext ctx, bool top)
            {
                int amt = ctx.Config.Rewards.ParticipantTokens + (top ? ctx.Config.Rewards.TopDamageTokens : 0);
                var cmd = ctx.Config.Rewards.TokenGrantCmd
                    .Replace("{steamid}", pl.UserIDString)
                    .Replace("{amount}", amt.ToString());
                if (!string.IsNullOrWhiteSpace(cmd))
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cmd, new object[0]);
                pl.ChatMessage($"<color=#B026FF>+{amt} Project Rift Tokens</color>");
            }
        }

        public class CosmeticGrant : IRewardGrant
        {
            public void Grant(BasePlayer pl, RiftContext ctx, bool top)
            {
                foreach (var skin in ctx.Config.Rewards.CosmeticSkins)
                {
                    var item = ItemManager.CreateByName("box.wooden", 1, skin);
                    if (item != null && !item.MoveToContainer(pl.inventory.containerMain))
                        item.Drop(pl.transform.position, Vector3.zero);
                }
            }
        }

        public class TitleGrant : IRewardGrant
        {
            public void Grant(BasePlayer pl, RiftContext ctx, bool top)
            {
                var grp = ctx.Config.Rewards.TitleGroup;
                if (string.IsNullOrEmpty(grp)) return;
                string cmd = "oxide.usergroup add " + pl.UserIDString + " " + grp;
                ConsoleSystem.Option opt = ConsoleSystem.Option.Server.Quiet();
                ConsoleSystem.Run(opt, cmd, new object[0]);
            }
        }

        #endregion

        // ====================================================================
        //  UI MANAGER  (CUI, purple theme)
        // ====================================================================
        #region UiManager

        public class UiManager
        {
            public const string HudRoot = "riftstorm.hud";
            public const string BossRoot = "riftstorm.bossbar";

            private const string Panel = "0.02 0.03 0.05 0.85";
            private const string Accent = "0.69 0.15 1.0 1.0";
            private const string Cyan = "0.0 0.90 1.0 1.0";

            private readonly RiftContext ctx;
            private float lastRefresh;
            private string lastSig = "";

            public UiManager(RiftContext ctx) { this.ctx = ctx; }

            public void ForceRedraw() => lastSig = "";

            public void Refresh(RiftEvent ev)
            {
                if (!ctx.Config.Ui.Enabled || ev == null) return;
                if (Time.realtimeSinceStartup - lastRefresh < ctx.Config.Ui.RefreshSeconds) return;
                lastRefresh = Time.realtimeSinceStartup;

                var sig = Signature(ev);
                if (sig == lastSig) return;
                lastSig = sig;

                foreach (var pl in BasePlayer.activePlayerList) DrawHud(pl, ev);
                if (ev.Phase == RiftPhaseId.Boss)
                    foreach (var pl in BasePlayer.activePlayerList) DrawBossBar(pl, ev);
            }

            private string Signature(RiftEvent ev) =>
                $"{ev.Phase}|{Mathf.CeilToInt(ev.CountdownRemaining)}|{ev.WaveIndex}|{ev.Stability:0}|" +
                $"{ev.CrystalsAlive}|{Mathf.RoundToInt(ev.BossHpFraction * 100)}|{ev.Zone.PlayersInside().Count}";

            private void DrawHud(BasePlayer pl, RiftEvent ev)
            {
                var c = new CuiElementContainer();
                var panel = c.Add(new CuiPanel
                {
                    Image = { Color = Panel, Material = "assets/content/ui/uibackgroundblur.mat" },
                    RectTransform = { AnchorMin = "0.32 0.88", AnchorMax = "0.68 0.985" },
                    CursorEnabled = false
                }, "Hud", HudRoot);

                c.Add(new CuiPanel { Image = { Color = Accent },
                    RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" } }, panel);

                Label(c, panel, $"⚡ RIFT STORM — {ev.Location.Name.ToUpper()}", 14, "0.04 0.55", "0.96 0.95", TextAnchor.MiddleLeft, Accent);
                Label(c, panel, PhaseLine(ev), 12, "0.04 0.08", "0.77 0.5", TextAnchor.MiddleLeft, "1 1 1 0.9");
                Label(c, panel, $"👥 {ev.Zone.PlayersInside().Count}", 12, "0.78 0.08", "0.97 0.5", TextAnchor.MiddleRight, Cyan);

                CuiHelper.DestroyUi(pl, HudRoot);
                CuiHelper.AddUi(pl, c);
            }

            private string PhaseLine(RiftEvent ev)
            {
                switch (ev.Phase)
                {
                    case RiftPhaseId.Detection: return $"Opens in {Mathf.CeilToInt(ev.CountdownRemaining)}s";
                    case RiftPhaseId.Storm: return "Storm intensifying...";
                    case RiftPhaseId.Rift: return "The rift is open!";
                    case RiftPhaseId.Waves: return $"Wave {ev.WaveIndex + 1}/{ev.WaveCount} — clear the scientists";
                    case RiftPhaseId.Objectives: return $"Stability {ev.Stability:0}%  •  Crystals {ev.CrystalsAlive}/{ev.CrystalsTotal}";
                    case RiftPhaseId.Boss: return $"RIFT OVERLORD  •  {ev.BossHpFraction * 100:0}% HP";
                    case RiftPhaseId.Victory: return "VICTORY — Rift Crate incoming!";
                    default: return "";
                }
            }

            private void DrawBossBar(BasePlayer pl, RiftEvent ev)
            {
                float frac = ev.BossHpFraction;
                var c = new CuiElementContainer();
                var bar = c.Add(new CuiPanel { Image = { Color = Panel },
                    RectTransform = { AnchorMin = "0.3 0.12", AnchorMax = "0.7 0.16" } }, "Hud", BossRoot);
                c.Add(new CuiPanel { Image = { Color = ev.BossRaged ? "1 0.2 0.4 0.95" : Accent },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = $"{frac.ToString(System.Globalization.CultureInfo.InvariantCulture)} 1" } }, bar);
                Label(c, bar, $"{ctx.Config.Boss.Name}   {frac * 100:0}%", 13, "0 0", "1 1", TextAnchor.MiddleCenter, "1 1 1 1");
                CuiHelper.DestroyUi(pl, BossRoot);
                CuiHelper.AddUi(pl, c);
            }

            public void HideBossBarAll() { foreach (var pl in BasePlayer.activePlayerList) CuiHelper.DestroyUi(pl, BossRoot); }

            public void DestroyAll()
            {
                foreach (var pl in BasePlayer.activePlayerList)
                {
                    CuiHelper.DestroyUi(pl, HudRoot);
                    CuiHelper.DestroyUi(pl, BossRoot);
                }
                lastSig = "";
            }

            private void Label(CuiElementContainer c, string parent, string text, int size, string min, string max, TextAnchor align, string color)
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = text, FontSize = size, Align = align, Color = color, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                }, parent);
            }
        }

        #endregion

        // ====================================================================
        //  INTEGRATIONS  (Discord + website API)
        // ====================================================================
        #region Integrations

        public class DiscordService : INotifier
        {
            private readonly RiftContext ctx;
            public DiscordService(RiftContext ctx) { this.ctx = ctx; }

            public void Notify(RiftNotice n)
            {
                var cfg = ctx.Config.Discord;
                if (!cfg.Enabled || string.IsNullOrEmpty(cfg.Webhook)) return;
                try
                {
                    var fields = new JArray();
                    if (!string.IsNullOrEmpty(n.LocationName)) fields.Add(Field("Location", n.LocationName, true));
                    foreach (var kv in n.Fields) fields.Add(Field(kv.Key, kv.Value, true));

                    var embed = new JObject
                    {
                        ["title"] = n.Title,
                        ["description"] = n.Message,
                        ["color"] = cfg.Color,
                        ["timestamp"] = n.TimestampUtc.ToString("o"),
                        ["footer"] = new JObject { ["text"] = "PROJECT RIFT • Rift Storm" },
                        ["fields"] = fields
                    };
                    var payload = new JObject { ["embeds"] = new JArray { embed } };
                    if (!string.IsNullOrEmpty(cfg.MentionRole) && (n.Type == NoticeType.Detected || n.Type == NoticeType.BossSpawned))
                        payload["content"] = $"<@&{cfg.MentionRole}>";

                    ctx.Plugin.webrequest.Enqueue(cfg.Webhook, payload.ToString(),
                        (code, resp) => { if (code != 204 && code != 200) ctx.Logger.Warn($"Discord webhook {code}"); },
                        ctx.Plugin, RequestMethod.POST,
                        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, 8f);
                }
                catch (Exception ex) { ctx.Logger.Warn($"discord notify: {ex.Message}"); }
            }

            private static JObject Field(string name, string value, bool inline) =>
                new JObject { ["name"] = name, ["value"] = value, ["inline"] = inline };
        }

        public class ApiService
        {
            private readonly RiftContext ctx;
            private float lastPush;
            public ApiService(RiftContext ctx) { this.ctx = ctx; }

            public string BuildStatusJson(RiftEvent ev)
            {
                if (ev == null)
                    return new JObject { ["status"] = "idle", ["updatedUtc"] = DateTime.UtcNow.ToString("o") }.ToString();

                return new JObject
                {
                    ["status"] = MapStatus(ev.Phase),
                    ["phase"] = ev.Phase.ToString(),
                    ["location"] = ev.Location?.Name ?? "",
                    ["startedUtc"] = ev.StartedUtc.ToString("o"),
                    ["remainingSeconds"] = Mathf.Max(0, Mathf.CeilToInt(ev.CountdownRemaining)),
                    ["stability"] = Mathf.RoundToInt(ev.Stability),
                    ["crystals"] = new JObject { ["alive"] = ev.CrystalsAlive, ["total"] = ev.CrystalsTotal },
                    ["wave"] = new JObject { ["index"] = ev.WaveIndex, ["count"] = ev.WaveCount },
                    ["boss"] = new JObject
                    {
                        ["name"] = ctx.Config.Boss.Name,
                        ["hpPercent"] = Mathf.RoundToInt(ev.BossHpFraction * 100f),
                        ["raged"] = ev.BossRaged,
                        ["alive"] = ev.Boss != null && !ev.Boss.IsDestroyed
                    },
                    ["participants"] = ev.Participants.Count,
                    ["updatedUtc"] = DateTime.UtcNow.ToString("o")
                }.ToString();
            }

            public void Push(RiftEvent ev)
            {
                var cfg = ctx.Config.Api;
                if (!cfg.Enabled || string.IsNullOrEmpty(cfg.PushUrl)) return;
                if (Time.realtimeSinceStartup - lastPush < cfg.PushSeconds) return;
                lastPush = Time.realtimeSinceStartup;
                try
                {
                    ctx.Plugin.webrequest.Enqueue(cfg.PushUrl, BuildStatusJson(ev),
                        (code, resp) => { if (code != 200) ctx.Logger.Warn($"API push {code}"); },
                        ctx.Plugin, RequestMethod.POST,
                        new Dictionary<string, string> { ["Content-Type"] = "application/json", ["x-api-key"] = cfg.ApiKey }, 8f);
                }
                catch (Exception ex) { ctx.Logger.Warn($"api push: {ex.Message}"); }
            }

            private static string MapStatus(RiftPhaseId p)
            {
                switch (p)
                {
                    case RiftPhaseId.Idle: return "idle";
                    case RiftPhaseId.Detection: return "detecting";
                    case RiftPhaseId.Boss: return "boss";
                    case RiftPhaseId.Victory: return "victory";
                    default: return "active";
                }
            }
        }

        #endregion

        // ====================================================================
        //  MARKER COMPONENTS
        // ====================================================================
        #region Tags

        public class RiftCrystalTag : MonoBehaviour { }
        public class RiftBossTag : MonoBehaviour { }
        public class RiftNpcTag : MonoBehaviour
        {
            public int WaveIndex;
            public float DamageScale = 1f;
            public LootTable Loot;
        }

        #endregion
    }
}
