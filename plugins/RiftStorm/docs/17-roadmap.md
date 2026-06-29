# Step 17 — Roadmap (Rift Storm 2.0 → 4.0)

The architecture is built so future versions are **additive**, not rewrites.
This step maps each planned version to the exact extension point it uses.

## How the design enables this

| Extension point | Lets you add… |
| ---------------- | ------------- |
| `IRiftPhase` + ordered phase list | New phases anywhere in the flow (no edits to existing phases) |
| `NpcProfile` (data) | New enemy types, new waves — config only |
| `LootTable` / `IRewardGrant` | New reward kinds (XP, battle-pass, currency) |
| `RiftLocation.Mode` | New location resolution strategies |
| `INotifier` | New broadcast channels (Discord, in-game, website, mobile push) |
| `RiftContext` (DI) | New managers wired in one place |
| Event hooks (`WaveCleared`, `CrystalDestroyed`, `BossDefeated`) | New systems that react without coupling |

---

## Rift Storm 2.0 — "Into the Rift" (dimension teleport)

**Goal:** at the boss phase, teleport participants into a separate arena
("another dimension") and back.

**Plan:**
- New `DimensionPhase : IRiftPhase` inserted before `BossPhase`. On `Enter`, it
  records each participant's origin, teleports them to a prebuilt sky/cave arena
  (or a procedurally walled platform), and applies a tinted skybox.
- New `DimensionManager` (wired into `RiftContext`) owns origin bookkeeping,
  safe-return on disconnect/death, and arena cleanup.
- Reuse `BossController` unchanged — it just runs inside the arena coordinates.
- Config: `Dimension { Enabled, ArenaCenter, ReturnOnDeath, SkyboxTint }`.

**Risk controls:** persist origin positions to a data file so a crash/disconnect
never strands a player.

---

## Rift Storm 3.0 — Multiple simultaneous storms

**Goal:** run N concurrent events at different locations.

**Plan:**
- Promote `RiftEventManager` from "owns one `Active`" to "owns a
  `List<RiftEvent>` + a phase cursor per event." The phase machine is already
  per-`RiftEvent`, so this is mostly bookkeeping.
- `EntityPool`/`RiftZone` become **per-event** (they already take an event/center).
- `UiManager` shows the **nearest** active storm to each player (zone distance).
- `ApiService` payload becomes an array of events.
- Config: `Schedule.MaxConcurrent`, anti-overlap radius so two storms don't pick
  adjacent monuments.

**Why it's cheap:** no manager logic changes — they all already operate on a
passed-in `RiftEvent`/`RiftContext`, never on a hidden singleton.

---

## Rift Storm 4.0 — Story, seasons, progression

**Goal:** dynamic story missions, season progression, achievements, battle pass.

**Plan:**
- **Story missions:** a `MissionScript` data model (ordered objectives) consumed
  by a `StoryPhase`/`MissionManager`. Each Rift Storm advances a server-wide
  narrative beat; the chosen location/boss variant is driven by current chapter.
- **Seasons:** a `SeasonManager` persists a season id + per-player progress to a
  data file (or Firestore via the website). Rewards scale with season tier.
- **Achievements:** subscribe an `AchievementService` to existing events
  (`BossDefeated`, `CrystalDestroyed`, participation thresholds) — pure
  add-on, no core edits.
- **Battle Pass:** new `BattlePassGrant : IRewardGrant` awards XP/levels; the
  website renders the pass from the same token/progress feed used by `/api/rift`.

**Data flow stays the same:** the plugin pushes richer JSON to the website;
the Next.js site already has Firestore (`lib/firebase.js`) to persist seasons,
leaderboards, and battle-pass state.

---

## Suggested version cut-lines

| Version | Scope | New classes (indicative) |
| ------- | ----- | ------------------------ |
| 1.0 | This guide — full single-storm event | — |
| 1.1 | Polish: reward preview UI, more locations, balance config | — |
| 2.0 | Dimension teleport | `DimensionPhase`, `DimensionManager` |
| 3.0 | Concurrent storms | refactor manager to multi-event |
| 4.0 | Story + seasons + achievements + battle pass | `MissionManager`, `SeasonManager`, `AchievementService`, `BattlePassGrant` |

---

## Build order recap (1.0)

1. Steps 02–05: scaffold folder, config, models.
2. Step 06: event manager + phase machine + Detection phase.
3. Step 07: weather + Storm phase.
4. Step 08: rift visuals, crystals, Objectives phase.
5. Step 09: NPC waves.
6. Step 10: boss.
7. Step 11: loot + Victory phase.
8. Step 12: UI.
9. Steps 13–14: Discord + website API.
10. Step 15: commands + composition root wiring.
11. Step 16: logging, pooling, cleanup, validation, profiling.
12. Merge (`build/merge.sh`) → `dist/RiftStorm.cs` → deploy → field test.

That's the complete Rift Storm Event System. 🛰️⚡
