# Step 01 — Architecture

## Goal

Design a system that is **object-oriented, modular, SOLID, and DI-friendly**,
while still loading inside the Carbon/Oxide single-assembly plugin model.

> ⚠️ **Reality of the plugin model:** Carbon/uMod compiles each plugin file into
> a single class deriving from `RustPlugin`/`CovalencePlugin`. You cannot ship a
> NuGet-style multi-project solution to a live server. We get "multiple files"
> two ways: (a) **partial classes** during development that merge into one `.cs`
> for release, or (b) **plain nested/standalone classes** inside the one file.
> This guide uses the **partial-class + build-merge** approach (Step 02) so the
> code reads like a real OO project but ships as one plugin.

## Layered overview

```
┌─────────────────────────────────────────────────────────────┐
│                      RiftStorm  (RustPlugin)                  │
│  Oxide/Carbon entry point: hooks, config load, command bind   │
│  Owns the service container + manager lifecycle               │
└───────────────┬─────────────────────────────────────────────┘
                │ creates & wires (composition root / DI)
                ▼
┌─────────────────────────────────────────────────────────────┐
│                     RiftEventManager                          │
│  • Scheduler (2–4h random)     • State machine (phases)       │
│  • Holds the active RiftEvent  • Delegates to subsystems      │
└──┬──────┬──────┬──────┬──────┬──────┬──────┬──────┬───────────┘
   │      │      │      │      │      │      │      │
   ▼      ▼      ▼      ▼      ▼      ▼      ▼      ▼
 Weather  Rift  Npc   Boss   Loot   Ui    Discord  Api
 Ctrl     Ctrl  Mgr   Ctrl   Mgr    Mgr   Service  Service
   │      │      │      │      │      │
   └──────┴──────┴──────┴──────┴──────┴──> shared: Logger, Pool, Zone
```

### Responsibilities (single-responsibility per class)

| Component | Responsibility |
| --------- | -------------- |
| `RiftStorm` | Plugin entry: Oxide hooks, config (de)serialization, permission/command registration, composition root. **No game logic.** |
| `RiftEventManager` | The brain. Schedules events, owns the **phase state machine**, advances phases, holds the current `RiftEvent`, coordinates subsystems. |
| `WeatherController` | Drives weather (rain/fog/wind/clouds/thunder) + purple smoke/lights atmosphere; restores weather on cleanup. |
| `RiftController` | Spawns/animates the central Rift (particles, beams, pulse) and the 4 Energy Crystals; tracks Portal Stability. |
| `NpcManager` | Spawns waves from NPC profiles, applies custom HP/weapon/loot/AI, tracks alive counts, fires "wave cleared". |
| `BossController` | Spawns the Rift Overlord, runs its ability AI (EMP/lightning/area/summon/rage), exposes HP for the UI. |
| `LootManager` | Resolves loot tables, populates corpses/crystals, builds + spawns the Rift Crate, grants tokens/cosmetics/titles. |
| `UiManager` | Builds & refreshes all CUI (countdown, location, wave, stability, crystals, boss bar, players-in-zone, reward preview). |
| `DiscordService` | Posts webhook embeds per phase with timestamps. |
| `ApiService` | Pushes live event JSON to the website + serves status. |
| `RiftZone` | Spatial helper: center, radius, contains(), players-in-zone, entity registry for cleanup. |
| `RiftLogger` | Structured, leveled logging wrapper. |
| `EntityPool` | Object pooling + master cleanup registry. |

## SOLID mapping

- **S**ingle Responsibility — one manager per concern (table above).
- **O**pen/Closed — phases implement `IRiftPhase`; new phases register without
  editing existing ones. NPC/loot behavior is data-driven via config profiles.
- **L**iskov — every phase is interchangeable through `IRiftPhase`; every NPC is
  driven by an `NpcProfile`, every reward by an `IRewardGrant`.
- **I**nterface Segregation — small interfaces: `IRiftPhase`, `IRewardGrant`,
  `INotifier` (Discord/UI/chat all implement it), `IEventListener`.
- **D**ependency Inversion — managers depend on **abstractions** passed in via
  the `RiftContext` (a lightweight service locator / DI container), never on the
  concrete plugin singleton.

## Dependency Injection: the `RiftContext`

Because we can't use a real DI framework inside a plugin, we use a small,
explicit **context object** built once in the composition root and passed to
every manager. This keeps managers decoupled and unit-testable.

```csharp
// Built once in RiftStorm.OnServerInitialized()
public sealed class RiftContext
{
    public Configuration Config { get; }
    public RiftLogger Logger { get; }
    public EntityPool Pool { get; }

    // Subsystems (interfaces where it matters for testing/extension)
    public WeatherController Weather { get; internal set; }
    public RiftController Rift { get; internal set; }
    public NpcManager Npcs { get; internal set; }
    public BossController Boss { get; internal set; }
    public LootManager Loot { get; internal set; }
    public UiManager Ui { get; internal set; }
    public INotifier Discord { get; internal set; }
    public ApiService Api { get; internal set; }

    // Convenience callbacks back into the plugin (timers, broadcast, schedule)
    public Action<string> Broadcast { get; }
    public Func<float, Action, Timer> Timer { get; }

    public RiftContext(Configuration config, RiftLogger logger, EntityPool pool,
                       Action<string> broadcast, Func<float, Action, Timer> timer)
    {
        Config = config; Logger = logger; Pool = pool;
        Broadcast = broadcast; Timer = timer;
    }
}
```

Every manager constructor takes `RiftContext ctx` and stores it. Cross-manager
calls go through `ctx` (e.g. `ctx.Ui.UpdateBossBar(...)`), so there is exactly
**one wiring point** and no hidden globals.

## The phase state machine (Open/Closed core)

```csharp
public interface IRiftPhase
{
    RiftPhaseId Id { get; }
    void Enter(RiftEvent ev, RiftContext ctx);   // start phase
    void Tick(RiftEvent ev, RiftContext ctx);    // periodic update (optional)
    void Exit(RiftEvent ev, RiftContext ctx);    // cleanup before next phase
    bool IsComplete(RiftEvent ev);               // advance when true
}
```

`RiftEventManager` holds an **ordered list** of `IRiftPhase` instances. It calls
`Enter` once, `Tick` on a timer, and advances to the next phase when
`IsComplete` returns true (or a phase explicitly requests advance). Adding Rift
Storm 2.0's "dimension teleport" phase = write one `IRiftPhase` class and insert
it in the list — **zero edits** to existing phases.

```
Detection → Storm → Rift → Waves → Objectives → Boss → Victory → (cleanup) → Idle
```

## Event-driven decoupling

Managers raise events instead of calling each other directly where possible:

```csharp
// e.g. NpcManager raises this; UiManager + EventManager subscribe
public event Action<int /*waveIndex*/> WaveCleared;
public event Action<BaseCombatEntity> CrystalDestroyed;
public event Action BossDefeated;
```

`RiftEventManager` subscribes to these to drive phase transitions; `UiManager`
subscribes to refresh HUD. This avoids tight coupling and makes the flow easy to
trace and extend.

## Why this matters for 100+ players

- One **central tick** owned by `RiftEventManager` (configurable Hz) fans out to
  phase + UI refresh, instead of every entity running its own `Update`.
- All spawned entities register in `EntityPool` / `RiftZone` so **cleanup is
  O(n) and total** — no orphaned NPCs leaking memory across events.
- UI is **diffed and throttled** (Step 12) — we only re-send changed panels.

Next: **[Step 02 — Folder structure](02-folder-structure.md)**.
