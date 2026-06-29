# Step 00 — Overview & Vision

## What is the Rift Storm?

The **Rift Storm** is Project Rift's signature, server-wide world event. It is
**not** a simple "spawn some NPCs" plugin — it's a staged, cinematic encounter
with weather control, custom AI, destructible objectives, a multi-phase boss,
live UI, Discord broadcasts, and a website feed.

It runs automatically every **2–4 hours** (configurable) at a random supported
location, and can also be triggered by admins.

## The seven phases

```
 ┌──────────────┐   ┌──────────┐   ┌────────┐   ┌─────────────┐
 │ 1. DETECTION │ → │ 2. STORM │ → │ 3.RIFT │ → │ 4. NPC WAVES│
 └──────────────┘   └──────────┘   └────────┘   └─────────────┘
        │                                              │
   3-min warning,                              Scientists →
   countdown, UI,                              Heavy → Elite
   warning sounds                                     │
                                                       ▼
 ┌─────────────┐   ┌──────────┐   ┌──────────────────────────┐
 │ 7. VICTORY  │ ← │ 6. BOSS  │ ← │ 5. OBJECTIVES (4 CRYSTALS)│
 └─────────────┘   └──────────┘   └──────────────────────────┘
   portal explodes,   Rift Overlord:   destroy crystals →
   fireworks,         EMP / lightning  portal stability
   Rift Crate         /summon /rage    100→75→50→25→0%
```

| Phase | Name | Summary |
| ----- | ---- | ------- |
| 1 | **Detection** | Random monument chosen. Server-wide broadcast, 3-min countdown, warning SFX, UI banner. |
| 2 | **Storm** | Weather ramps up (rain, fog, clouds, wind, thunder). Purple smoke + lights spawn. Dangerous atmosphere. |
| 3 | **Rift** | A massive rift spawns in the center — particles, smoke, beams, electric FX, floating debris, pulsing every few seconds. |
| 4 | **NPC Defense** | 3 escalating waves: Scientists → Heavy Scientists → Elite Scientists. Custom HP/weapons/AI/loot. |
| 5 | **Objectives** | Players destroy **4 Energy Crystals**. Each death lowers Portal Stability (100→75→50→25→0%). At 0% the boss spawns. |
| 6 | **Boss** | **Rift Overlord** with EMP pulse, lightning strike, area damage, summon reinforcements, rage mode, health-bar UI, custom loot. |
| 7 | **Victory** | Boss dies → portal explodes (purple) → fireworks → server broadcast → **Rift Crate** with HQM, weapons, components, rare resources, Project Rift Tokens, cosmetics, titles. |

## Design pillars

1. **Modular** — each subsystem (weather, NPCs, loot, UI…) is an isolated
   manager with one job. Swapping or extending one never touches the others.
2. **Expandable** — the phase pipeline is data-driven and ordered, so new phases
   (e.g. "teleport to another dimension" in 2.0) slot in without rewrites.
3. **Performant** — object pooling, cached lookups, timed cleanup, throttled UI
   refresh. Built to survive 100+ players.
4. **Observable** — structured logging, Discord embeds, and a website JSON feed
   give full visibility into a live event.
5. **Configurable** — every tunable (cooldown, counts, HP, weather, rewards,
   locations, messages, permissions) lives in config.

## Glossary

| Term | Meaning |
| ---- | ------- |
| **Event** | One full run of the Rift Storm, phase 1 → 7. |
| **Phase** | A discrete stage of the event, advanced by the state machine. |
| **Location / Site** | A monument or wilderness point where the event runs. |
| **Rift** | The central visual+logical anchor entity of the event. |
| **Crystal** | A destructible Energy Crystal objective (4 per event). |
| **Stability** | Portal health %, driven by crystal destruction. |
| **Overlord** | The Rift Overlord final boss. |
| **Zone** | The spherical play area centered on the site. |
| **Token** | Project Rift Token — the premium reward currency. |

## Where this fits in the repo

```
ProjectRift/
├── app/ …                     ← Next.js website (existing)
├── rust-plugin/
│   └── ProjectRiftCore.cs      ← existing live HUD/heartbeat plugin
└── plugins/
    └── RiftStorm/              ← THIS event system (docs + source)
```

Next: **[Step 01 — Architecture](01-architecture.md)**.
