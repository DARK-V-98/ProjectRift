# Step 04 — Configuration

Everything is configurable. The config is a strongly-typed POCO with sensible
defaults, auto-generated on first load. Nested sections keep it readable.

## `src/Config/Configuration.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class Configuration
        {
            [JsonProperty("Schedule")]
            public ScheduleConfig Schedule = new ScheduleConfig();

            [JsonProperty("Weather")]
            public WeatherConfig Weather = new WeatherConfig();

            [JsonProperty("Zone")]
            public ZoneConfig Zone = new ZoneConfig();

            [JsonProperty("Waves (NPC)")]
            public WavesConfig Waves = new WavesConfig();

            [JsonProperty("Objectives (Crystals)")]
            public CrystalConfig Crystals = new CrystalConfig();

            [JsonProperty("Boss (Rift Overlord)")]
            public BossConfig Boss = new BossConfig();

            [JsonProperty("Rewards")]
            public RewardConfig Rewards = new RewardConfig();

            [JsonProperty("Locations")]
            public List<RiftLocation> Locations = RiftLocation.Defaults();

            [JsonProperty("UI")]
            public UiConfig Ui = new UiConfig();

            [JsonProperty("Discord")]
            public DiscordConfig Discord = new DiscordConfig();

            [JsonProperty("Website API")]
            public ApiConfig Api = new ApiConfig();

            [JsonProperty("Announcements")]
            public AnnounceConfig Announce = new AnnounceConfig();

            [JsonProperty("Permissions")]
            public PermissionConfig Permissions = new PermissionConfig();

            [JsonProperty("Performance")]
            public PerfConfig Performance = new PerfConfig();
        }

        public class ScheduleConfig
        {
            [JsonProperty("Enabled (auto-run)")] public bool Enabled = true;
            [JsonProperty("Min hours between events")] public float MinHours = 2f;
            [JsonProperty("Max hours between events")] public float MaxHours = 4f;
            [JsonProperty("Detection countdown (seconds)")] public float CountdownSeconds = 180f;
            [JsonProperty("Block during server restart window")] public bool RespectRestart = true;
            [JsonProperty("Min online players to start")] public int MinPlayers = 2;
        }

        public class WeatherConfig
        {
            [JsonProperty("Override weather")] public bool Enabled = true;
            [JsonProperty("Rain (0-1)")] public float Rain = 0.9f;
            [JsonProperty("Fog (0-1)")] public float Fog = 0.7f;
            [JsonProperty("Clouds (0-1)")] public float Clouds = 0.95f;
            [JsonProperty("Wind (0-1)")] public float Wind = 0.8f;
            [JsonProperty("Thunder enabled")] public bool Thunder = true;
            [JsonProperty("Thunder interval (s, min)")] public float ThunderMin = 6f;
            [JsonProperty("Thunder interval (s, max)")] public float ThunderMax = 14f;
            [JsonProperty("Purple smoke prefab")] public string SmokePrefab = "assets/bundled/prefabs/fx/smoke_signal_full.prefab";
            [JsonProperty("Purple light count")] public int LightCount = 12;
            [JsonProperty("Atmosphere ring radius")] public float RingRadius = 40f;
        }

        public class ZoneConfig
        {
            [JsonProperty("Play area radius (m)")] public float Radius = 80f;
            [JsonProperty("Damage players who leave during boss")] public bool LeashBoss = false;
            [JsonProperty("Participation: min seconds inside to earn loot")] public float MinParticipationSeconds = 30f;
        }

        public class WavesConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Spawn radius from center")] public float SpawnRadius = 35f;
            [JsonProperty("Delay between waves (s)")] public float WaveDelay = 8f;
            [JsonProperty("Scale NPC count per online players")] public bool ScaleWithPlayers = true;
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
            [JsonProperty("Profile (uses NpcProfile fields)")] public NpcProfile Profile = NpcProfile.DefaultBoss();
            [JsonProperty("EMP pulse cooldown (s)")] public float EmpCooldown = 25f;
            [JsonProperty("Lightning cooldown (s)")] public float LightningCooldown = 12f;
            [JsonProperty("Area damage cooldown (s)")] public float AreaCooldown = 18f;
            [JsonProperty("Summon cooldown (s)")] public float SummonCooldown = 30f;
            [JsonProperty("Summon count")] public int SummonCount = 3;
            [JsonProperty("Rage mode HP threshold (%)")] public float RageThreshold = 30f;
            [JsonProperty("Rage damage multiplier")] public float RageDamageMult = 1.5f;
            [JsonProperty("Rage cooldown multiplier")] public float RageCdMult = 0.6f;
        }

        public class RewardConfig
        {
            [JsonProperty("Rift Crate prefab")] public string CratePrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
            [JsonProperty("Crate loot table")] public LootTable CrateLoot = LootTable.DefaultCrate();
            [JsonProperty("Project Rift Tokens for participants")] public int ParticipantTokens = 25;
            [JsonProperty("Project Rift Tokens for top damage")] public int TopDamageTokens = 100;
            [JsonProperty("Grant cosmetic skin ids")] public List<ulong> CosmeticSkins = new List<ulong>();
            [JsonProperty("Grant title (permission/group)")] public string TitleGroup = "rift_survivor";
            [JsonProperty("Economics plugin token command")] public string TokenGrantCmd = "addtokens {steamid} {amount}";
        }

        public class UiConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Accent (purple) hex")] public string Accent = "#B026FF";
            [JsonProperty("Secondary (cyan) hex")] public string Secondary = "#00E5FF";
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
            [JsonProperty("Use ProjectRiftCore notification shower if present")] public bool UseCoreNotifications = true;
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
    }
}
```

## Loader (in `RiftStorm.cs`)

```csharp
private Configuration config;

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
```

> **Validation:** on load, clamp values (e.g. `MinHours <= MaxHours`,
> `Crystals.Count >= 1`, `TickHz` between 0.5 and 10) and log corrections. See
> Step 16 for the `ValidateConfig()` helper.

Next: **[Step 05 — Data models](05-data-models.md)**.
