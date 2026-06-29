# PROJECT RIFT — RIFT STORM EVENT SYSTEM

> A modular, expandable, production-ready **custom world event** for Rust
> (Carbon-first, uMod/Oxide compatible). This folder contains the **step-by-step
> build guide**: design, architecture, data models, configuration, and the full
> source skeleton for every subsystem.

The Rift Storm is the **signature event** of Project Rift: a dimensional rift
opens at a random monument, the weather turns violent, waves of scientists pour
out, players race to destroy 4 Energy Crystals, then face the **Rift Overlord**
boss before the portal explodes and drops a high-tier Rift Crate.

---

## How to use this guide

The documentation is split into **numbered steps**. Build the plugin in order —
each step produces one or more C# classes that drop into the folder structure
defined in Step 02. By the end you have a complete, compilable plugin.

| Step | File | What you build |
| ---- | ---- | -------------- |
| 00 | [docs/00-overview.md](docs/00-overview.md) | Concept, event flow, vision, glossary |
| 01 | [docs/01-architecture.md](docs/01-architecture.md) | High-level architecture, SOLID, DI, manager pattern |
| 02 | [docs/02-folder-structure.md](docs/02-folder-structure.md) | Repo + plugin folder layout, build/merge strategy |
| 03 | [docs/03-class-diagram.md](docs/03-class-diagram.md) | Full class diagram + responsibilities |
| 04 | [docs/04-configuration.md](docs/04-configuration.md) | Complete `RiftStorm.json` config + loader |
| 05 | [docs/05-data-models.md](docs/05-data-models.md) | All POCOs / state objects / enums |
| 06 | [docs/06-event-manager.md](docs/06-event-manager.md) | Scheduler, state machine, phase orchestration |
| 07 | [docs/07-weather-controller.md](docs/07-weather-controller.md) | Rain/fog/wind/thunder + purple FX atmosphere |
| 08 | [docs/08-rift-and-objectives.md](docs/08-rift-and-objectives.md) | The Rift visuals + 4 Energy Crystals + stability |
| 09 | [docs/09-npc-manager.md](docs/09-npc-manager.md) | Wave spawning, custom NPC profiles, AI |
| 10 | [docs/10-boss-system.md](docs/10-boss-system.md) | Rift Overlord, abilities, rage, health bar |
| 11 | [docs/11-loot-manager.md](docs/11-loot-manager.md) | Loot tables, Rift Crate, tokens, rewards |
| 12 | [docs/12-ui-system.md](docs/12-ui-system.md) | CUI HUD: countdown, waves, health bars, theme |
| 13 | [docs/13-discord-integration.md](docs/13-discord-integration.md) | Webhook embeds for every phase |
| 14 | [docs/14-website-api.md](docs/14-website-api.md) | JSON push to the Next.js site + endpoint |
| 15 | [docs/15-commands-permissions.md](docs/15-commands-permissions.md) | Admin commands + permission gates |
| 16 | [docs/16-logging-performance.md](docs/16-logging-performance.md) | Logging, object pooling, cleanup, profiling |
| 17 | [docs/17-roadmap.md](docs/17-roadmap.md) | Rift Storm 2.0 → 4.0, seasons, battle pass |

---

## Target & conventions

- **Framework:** Carbon (preferred) — written as an Oxide-compatible
  `RustPlugin` in namespace `Oxide.Plugins`, so it also loads on uMod/Oxide.
- **Author/brand:** `ESYSTEMLK` · `https://projectrift.esystemlk.com`
- **Theme:** Project Rift purple `#B026FF` / electric violet `#8A2BE2` / cyan
  `#00E5FF` on deep black `#05070D` — matching the website and `ProjectRiftCore`.
- **Companion plugin:** Designed to coexist with the existing
  [`ProjectRiftCore`](../../rust-plugin/ProjectRiftCore.cs). Rift Storm can push
  notifications through Core's notification shower and reuse its API key.

## Deliverables checklist

- [x] Complete plugin architecture (Step 01)
- [x] Folder structure (Step 02)
- [x] Class diagram (Step 03)
- [x] Configuration file (Step 04)
- [x] Data models (Step 05)
- [x] Event manager (Step 06)
- [x] Weather controller (Step 07)
- [x] Rift + objectives / crystals (Step 08)
- [x] NPC manager (Step 09)
- [x] Boss system (Step 10)
- [x] Loot manager (Step 11)
- [x] UI system (Step 12)
- [x] Discord integration (Step 13)
- [x] Website API endpoints (Step 14)
- [x] Admin commands (Step 15)
- [x] Error handling, logging, performance (Step 16)
- [x] Future-version roadmap (Step 17)

> **Status:** Design docs **+ a complete shippable plugin**. The full working
> source is at [`dist/RiftStorm.cs`](dist/RiftStorm.cs) — a single Carbon/Oxide
> `RustPlugin` implementing every phase, manager, the CUI HUD, Discord webhooks,
> the website push, and the admin commands. Companion website endpoint:
> [`app/api/rift/route.js`](../../app/api/rift/route.js).
>
> Code follows real Rust/Carbon APIs and CUI, but compile + field-test on a
> staging server before going live — Rust's API surface changes across forced
> wipes (prefab paths are configurable for exactly this reason).
