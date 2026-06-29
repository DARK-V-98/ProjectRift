# Step 15 — Admin Commands & Permissions, Plugin Entry Point

This step covers the chat/console commands, permission registration, and the
**composition root** (`RiftStorm.cs`) that wires everything together.

## Permissions

| Permission | Grants |
| ---------- | ------ |
| `riftstorm.admin` | All `/admin` Rift commands |
| `riftstorm.reward` *(optional)* | Eligibility for rewards (blank = everyone) |

Registered in `Init()`:

```csharp
permission.RegisterPermission(config.Permissions.Admin, this);
if (!string.IsNullOrEmpty(config.Permissions.Reward))
    permission.RegisterPermission(config.Permissions.Reward, this);
```

## Commands

| Command | Action |
| ------- | ------ |
| `/admin startrift [location]` | Force-start now (optional location name) |
| `/admin stoprift` | Abort the active event + cleanup |
| `/admin nextrift` | Reschedule the next auto-event immediately |
| `/admin riftstatus` | Print current phase/stability/boss HP to chat |
| `/admin debugrift` | Verbose dump: entities tracked, timers, perf samples |

> Brief uses `/admin <verb>`. We register the `admin` chat command and branch on
> the first argument so all five live under one verb, matching the spec.

## `src/Commands/AdminCommands.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        [ChatCommand("admin")]
        private void CmdAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdmin(player)) { Reply(player, "No permission."); return; }
            if (args.Length == 0) { Reply(player, "Usage: /admin <startrift|stoprift|nextrift|riftstatus|debugrift>"); return; }

            switch (args[0].ToLower())
            {
                case "startrift": DoStart(player, args); break;
                case "stoprift":  events.StopEvent(false); Reply(player, "Rift Storm stopped."); break;
                case "nextrift":  events.ScheduleNext(); Reply(player, "Next Rift Storm rescheduled."); break;
                case "riftstatus":DoStatus(player); break;
                case "debugrift": DoDebug(player); break;
                default: Reply(player, "Unknown subcommand."); break;
            }
        }

        // console equivalents (RCON / server console)
        [ConsoleCommand("riftstorm.start")]
        private void CcStart(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !HasAdmin(arg.Player())) return;
            var loc = events.PickByNameOrRandom(arg.GetString(0, null));
            arg.ReplyWith(events.StartEvent(loc) ? "Started." : "Could not start.");
        }

        private void DoStart(BasePlayer player, string[] args)
        {
            string name = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : null;
            var loc = events.PickByNameOrRandom(name);
            if (loc == null) { Reply(player, "No valid location."); return; }
            Reply(player, events.StartEvent(loc) ? $"Rift Storm started at {loc.Name}." : "An event is already running.");
        }

        private void DoStatus(BasePlayer player)
        {
            if (!events.IsRunning) { Reply(player, "No active Rift Storm."); return; }
            var ev = events.Active;
            Reply(player, $"Phase: {ev.Phase} | Loc: {ev.Location.Name} | Stability: {ev.Stability:0}% | " +
                          $"Crystals: {ev.CrystalsAlive}/{ev.CrystalsTotal} | Wave: {ev.WaveIndex + 1}/{ev.WaveCount} | " +
                          $"Boss: {ev.BossHpFraction*100:0}% | Players: {ev.Zone.PlayersInside().Count}");
        }

        private void DoDebug(BasePlayer player)
        {
            Reply(player, ctx.Pool.DebugSummary());
            Reply(player, ctx.Logger.PerfSummary());
        }

        private bool HasAdmin(BasePlayer p) =>
            p.IsAdmin || permission.UserHasPermission(p.UserIDString, config.Permissions.Admin);

        private void Reply(BasePlayer p, string msg) =>
            p.ChatMessage($"{config.Announce.Prefix} {msg}");
    }
}
```

## Composition root — `src/RiftStorm.cs`

This is the only file Oxide/Carbon loads as the plugin class; everything else is
a partial of it. It loads config, builds the `RiftContext` (DI), and wires hooks.

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RiftStorm", "ESYSTEMLK", "1.0.0")]
    [Description("Project Rift signature world event: detection → storm → rift → waves → crystals → Rift Overlord → rewards.")]
    public partial class RiftStorm : RustPlugin
    {
        [PluginReference] private Plugin ProjectRiftCore; // optional: route notifications
        [PluginReference] private Plugin ImageLibrary;    // optional: UI icons

        private RiftContext ctx;
        private RiftEventManager events;

        private void Init() => permission.RegisterPermission(config.Permissions.Admin, this);

        private void OnServerInitialized()
        {
            ValidateConfig();
            BuildContainer();
            events.ScheduleNext();
            ctx.Logger.Info("RiftStorm loaded.");
        }

        private void BuildContainer()
        {
            var logger = new RiftLogger(this, config);
            var pool   = new EntityPool(logger);
            ctx = new RiftContext(config, logger, pool,
                broadcast: msg => Broadcast(msg),
                timer: (secs, cb) => timer.In(secs, cb));   // wrap Oxide timers

            ctx.Weather = new WeatherController(ctx);
            ctx.Rift    = new RiftController(ctx);
            ctx.Npcs    = new NpcManager(ctx);
            ctx.Boss    = new BossController(ctx);
            ctx.Loot    = new LootManager(ctx);
            ctx.Ui      = new UiManager(ctx);
            ctx.Discord = new DiscordService(ctx, this);
            ctx.Api     = new ApiService(ctx, this);

            events = new RiftEventManager(ctx);
        }

        private void Broadcast(string msg)
        {
            if (config.Announce.UseCoreNotifications && ProjectRiftCore != null)
                ProjectRiftCore.Call("PushNotification", msg, "alert");   // reuse Core shower
            else
                Server.Broadcast(msg);
        }

        // ---- hooks forwarded to managers ---------------------------------
        object OnEntityDeath(BaseCombatEntity entity, HitInfo info) { /* Step 08/10 wiring */ return null; }
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) { /* Step 08 wiring */ }
        void OnPlayerConnected(BasePlayer player) { if (events != null && events.IsRunning) ctx.Ui.Refresh(events.Active); }
        void OnEntityKill(BaseNetworkable e) { /* drop dead NPCs from alive set */ }

        private void Unload()
        {
            events?.StopEvent(false);   // forces full cleanup
            ctx?.Pool?.CleanupAll(null);
            foreach (var pl in BasePlayer.activePlayerList)
            { CuiHelper.DestroyUi(pl, "riftstorm.hud"); CuiHelper.DestroyUi(pl, "riftstorm.bossbar"); }
        }
    }
}
```

> **`PushNotification` reuse:** the existing `ProjectRiftCore` exposes a
> notification shower. If you add a public `[HookMethod]`/`Call`-able
> `PushNotification(string, string)` to Core, Rift Storm broadcasts flow through
> the same styled UI. Otherwise it falls back to `Server.Broadcast`.

Next: **[Step 16 — Logging & performance](16-logging-performance.md)**.
