# Step 05 — Data Models

The data layer is plain POCOs + enums. They carry **state and definitions**, no
behavior beyond simple helpers — logic lives in managers (SRP).

## Phase enum — `src/Models/RiftPhaseId.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public enum RiftPhaseId
        {
            Idle = 0,
            Detection = 1,
            Storm = 2,
            Rift = 3,
            Waves = 4,
            Objectives = 5,
            Boss = 6,
            Victory = 7,
            Cleanup = 8
        }
    }
}
```

## Live event state — `src/Models/RiftEvent.cs`

This is the single source of truth for an in-progress event. Managers read/write
it through the `RiftContext`.

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class RiftEvent
        {
            // identity & timing
            public Guid Id { get; } = Guid.NewGuid();
            public DateTime StartedUtc { get; } = DateTime.UtcNow;
            public DateTime PhaseStartedUtc { get; set; } = DateTime.UtcNow;

            // location / space
            public RiftLocation Location { get; set; }
            public Vector3 Center => Location?.Position ?? Vector3.zero;
            public RiftZone Zone { get; set; }

            // phase machine
            public RiftPhaseId Phase { get; set; } = RiftPhaseId.Idle;

            // detection
            public float CountdownRemaining { get; set; }

            // objectives
            public float Stability { get; set; } = 100f;   // portal stability %
            public int CrystalsTotal { get; set; }
            public int CrystalsAlive { get; set; }
            public List<BaseCombatEntity> Crystals { get; } = new List<BaseCombatEntity>();

            // waves
            public int WaveIndex { get; set; } = -1;        // -1 = not started
            public int WaveCount { get; set; }

            // boss
            public BaseCombatEntity Boss { get; set; }
            public bool BossRaged { get; set; }
            public float BossMaxHp { get; set; }

            // participation & scoring
            public HashSet<ulong> Participants { get; } = new HashSet<ulong>();
            public Dictionary<ulong, float> DamageByPlayer { get; } = new Dictionary<ulong, float>();
            public Dictionary<ulong, float> SecondsInZone { get; } = new Dictionary<ulong, float>();

            // entity bookkeeping for cleanup
            public List<BaseEntity> SpawnedEntities { get; } = new List<BaseEntity>();
            public List<uint> FxRefs { get; } = new List<uint>();

            public float BossHpFraction =>
                Boss != null && !Boss.IsDestroyed && BossMaxHp > 0f
                    ? Mathf.Clamp01(Boss.Health() / BossMaxHp) : 0f;

            public void AddDamage(ulong id, float amount)
            {
                DamageByPlayer.TryGetValue(id, out var cur);
                DamageByPlayer[id] = cur + amount;
            }
        }
    }
}
```

## Location definition — `src/Models/RiftLocation.cs`

Locations are data, so adding a new one is a config edit. Either pin a fixed
world position, or resolve dynamically from a **monument prefab name** at
runtime, or pick a **random wilderness** point.

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class RiftLocation
        {
            [JsonProperty("Name")] public string Name = "Launch Site";
            [JsonProperty("Enabled")] public bool Enabled = true;

            // Resolution mode: "monument" | "fixed" | "wilderness"
            [JsonProperty("Mode")] public string Mode = "monument";

            // monument mode: substring match against monument prefab/display name
            [JsonProperty("Monument match")] public string MonumentMatch = "launch_site";

            // fixed mode: explicit world position
            [JsonProperty("Fixed X")] public float X;
            [JsonProperty("Fixed Y")] public float Y;
            [JsonProperty("Fixed Z")] public float Z;

            // computed at selection time (not serialized)
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
    }
}
```

> **Resolution** lives in `RiftEventManager.ResolveLocation(...)` (Step 06): it
> scans `TerrainMeta.Path.Monuments` for the match, or picks a random walkable
> wilderness point clear of buildings/water. This keeps `RiftLocation` pure data.

## NPC profile — `src/Models/NpcProfile.cs`

One profile drives one NPC type (HP, weapon, kit, loot, AI tuning). Waves and
the boss all reuse this — Liskov-friendly.

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class NpcProfile
        {
            [JsonProperty("Display name")] public string Name = "Rift Scientist";
            [JsonProperty("Count per wave")] public int Count = 4;
            [JsonProperty("Health")] public float Health = 150f;
            [JsonProperty("Prefab")] public string Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";

            // gear
            [JsonProperty("Weapon shortname")] public string Weapon = "rifle.ak";
            [JsonProperty("Wear item shortnames")] public List<string> Wear = new List<string> { "metal.facemask", "metal.plate.torso" };

            // AI tuning
            [JsonProperty("Damage scale")] public float DamageScale = 1f;
            [JsonProperty("Sense range")] public float SenseRange = 60f;
            [JsonProperty("Aim cone")] public float AimCone = 2f;
            [JsonProperty("Speed scale")] public float SpeedScale = 1f;

            // rewards
            [JsonProperty("Loot table")] public LootTable Loot = LootTable.DefaultScientist();
            [JsonProperty("Skin/tint accent")] public string Accent = "#B026FF";

            public static List<NpcProfile> DefaultWaves() => new List<NpcProfile>
            {
                new NpcProfile { Name = "Rift Scientist",       Count = 5,  Health = 150f },
                new NpcProfile { Name = "Rift Heavy Scientist", Count = 4,  Health = 350f,
                    Wear = { "heavy.plate.helmet", "heavy.plate.jacket", "heavy.plate.pants" }, DamageScale = 1.2f },
                new NpcProfile { Name = "Rift Elite Scientist", Count = 3,  Health = 600f,
                    Weapon = "rifle.l96", DamageScale = 1.4f, SenseRange = 80f, AimCone = 1f },
            };

            public static NpcProfile DefaultBoss() => new NpcProfile
            {
                Name = "Rift Overlord", Count = 1, Health = 8000f,
                Weapon = "rifle.ak", DamageScale = 1.6f, SenseRange = 100f,
                SpeedScale = 1.15f, Loot = LootTable.DefaultBoss(),
            };
        }
    }
}
```

## Loot table — `src/Models/LootTable.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
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
                MinRolls = 1, MaxRolls = 2, Entries =
                {
                    new LootEntry { Shortname = "scrap", Min = 20, Max = 60, Chance = 1f },
                    new LootEntry { Shortname = "rifle.ak", Chance = 0.05f },
                    new LootEntry { Shortname = "metal.refined", Min = 5, Max = 15, Chance = 0.5f },
                }
            };

            public static LootTable DefaultBoss() => new LootTable
            {
                MinRolls = 4, MaxRolls = 6, Entries =
                {
                    new LootEntry { Shortname = "metal.refined", Min = 100, Max = 250, Chance = 1f },
                    new LootEntry { Shortname = "explosive.timed", Min = 2, Max = 5, Chance = 0.6f },
                    new LootEntry { Shortname = "rifle.l96", Chance = 0.25f },
                }
            };

            public static LootTable DefaultCrate() => new LootTable
            {
                MinRolls = 5, MaxRolls = 8, Entries =
                {
                    new LootEntry { Shortname = "metal.refined", Min = 250, Max = 500, Chance = 1f },
                    new LootEntry { Shortname = "rifle.ak", Min = 1, Max = 1, Chance = 0.5f },
                    new LootEntry { Shortname = "techparts", Min = 5, Max = 20, Chance = 0.8f },
                    new LootEntry { Shortname = "metal.fragments", Min = 2000, Max = 5000, Chance = 1f },
                    new LootEntry { Shortname = "supply.signal", Min = 1, Max = 2, Chance = 0.4f },
                }
            };
        }
    }
}
```

## Notification model — used by `INotifier` (Discord/UI/chat)

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
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
    }
}
```

Next: **[Step 06 — Event manager](06-event-manager.md)**.
