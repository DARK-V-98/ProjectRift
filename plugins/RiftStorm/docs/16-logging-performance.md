# Step 16 — Error Handling, Logging, Pooling, Cleanup & Performance

This step covers the cross-cutting concerns that make the plugin
production-ready for 100+ players: structured logging, the entity pool +
guaranteed cleanup, config validation, defensive error handling, and profiling.

## Structured logging — `src/Core/RiftLogger.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class RiftLogger
        {
            private readonly RiftStorm plugin;
            private readonly Configuration cfg;
            private readonly Dictionary<string, double> perf = new Dictionary<string, double>();

            public RiftLogger(RiftStorm plugin, Configuration cfg) { this.plugin = plugin; this.cfg = cfg; }

            public void Info(string m)   => plugin.Puts($"[INFO] {m}");
            public void Warn(string m)   => plugin.PrintWarning($"[WARN] {m}");
            public void Error(string m)  => plugin.PrintError($"[ERROR] {m}");
            public void Reward(string m) => plugin.Puts($"[REWARD] {m}");
            public void Kill(string m)   => plugin.Puts($"[KILL] {m}");

            // disposable perf scope: using(ctx.Logger.Perf("tick")) { ... }
            public IDisposable Perf(string label) => new PerfScope(this, label);

            internal void Record(string label, double ms)
            {
                perf[label] = ms;
                if (cfg.Performance.LogPerf && ms > 8.0)        // only log slow frames
                    plugin.Puts($"[PERF] {label} took {ms:0.0}ms");
            }

            public string PerfSummary()
            {
                var sb = new System.Text.StringBuilder("[PERF] ");
                foreach (var kv in perf) sb.Append($"{kv.Key}={kv.Value:0.0}ms ");
                return sb.ToString();
            }

            private sealed class PerfScope : IDisposable
            {
                private readonly RiftLogger log; private readonly string label;
                private readonly System.Diagnostics.Stopwatch sw;
                public PerfScope(RiftLogger log, string label)
                { this.log = log; this.label = label; sw = System.Diagnostics.Stopwatch.StartNew(); }
                public void Dispose() { sw.Stop(); log.Record(label, sw.Elapsed.TotalMilliseconds); }
            }
        }
    }
}
```

**Logged events** (per the brief): event start, event end, rewards, kills, boss
deaths, errors, performance — all routed through this logger so the prefix/level
is consistent and greppable.

## Entity pool + total cleanup — `src/Core/EntityPool.cs`

Every spawned entity/FX is **tracked** so cleanup is total — no orphaned NPCs,
crystals, or lights surviving the event (the #1 source of memory leaks).

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class EntityPool
        {
            private readonly RiftLogger log;
            private readonly HashSet<BaseEntity> tracked = new HashSet<BaseEntity>();

            public EntityPool(RiftLogger log) { this.log = log; }

            public void Track(BaseEntity e) { if (e != null) tracked.Add(e); }

            // Kill everything we spawned. Safe to call repeatedly.
            public void CleanupAll(RiftEvent ev)
            {
                int killed = 0;
                foreach (var e in tracked)
                {
                    try { if (e != null && !e.IsDestroyed) { e.Kill(); killed++; } }
                    catch (Exception ex) { log.Error($"cleanup kill failed: {ex.Message}"); }
                }
                tracked.Clear();
                ev?.SpawnedEntities.Clear();
                log.Info($"Cleanup complete — {killed} entities removed.");
            }

            public string DebugSummary() => $"[POOL] tracked entities: {tracked.Count}";
        }
    }
}
```

> **Why kill, not return-to-pool?** Rust entities are heavyweight networked
> objects; the win is *deterministic destruction*, not reuse. "Pooling" here
> means a single authoritative registry that guarantees nothing leaks. If you
> later pool lightweight things (FX requests, CUI containers), add typed sub-pools.

## Spatial helper — `src/Core/RiftZone.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class RiftZone
        {
            public Vector3 Center { get; }
            public float Radius { get; }
            private readonly float sqr;

            public RiftZone(Vector3 center, float radius)
            { Center = center; Radius = radius; sqr = radius * radius; }

            public bool Contains(Vector3 p) => (p - Center).sqrMagnitude <= sqr;

            public List<BasePlayer> PlayersInside()
            {
                var list = new List<BasePlayer>();
                foreach (var pl in BasePlayer.activePlayerList)
                    if (pl != null && !pl.IsDead() && Contains(pl.transform.position))
                        list.Add(pl);
                return list;
            }
        }
    }
}
```

## Config validation (in `RiftStorm.cs`)

```csharp
private void ValidateConfig()
{
    var c = config;
    void Fix(bool bad, string msg, Action f) { if (bad) { f(); ctx?.Logger?.Warn(msg); } }

    if (c.Schedule.MinHours > c.Schedule.MaxHours)
        (c.Schedule.MinHours, c.Schedule.MaxHours) = (c.Schedule.MaxHours, c.Schedule.MinHours);
    c.Performance.TickHz = Mathf.Clamp(c.Performance.TickHz, 0.5f, 10f);
    c.Crystals.Count = Mathf.Max(1, c.Crystals.Count);
    c.Performance.MaxNpcs = Mathf.Clamp(c.Performance.MaxNpcs, 1, 200);
    if (c.Locations.FindAll(l => l.Enabled).Count == 0)
        PrintWarning("No locations enabled — auto events cannot start.");
    SaveConfig();
}
```

## Defensive error handling rules

1. **Every hook is null-guarded** — `if (!events.IsRunning || entity == null) return;`
2. **Every prefab spawn is checked** — log + skip on `null`, never NRE.
3. **Timers are owned & destroyed** — store handles, `Destroy()` on stop/unload.
4. **try/catch around external boundaries** — webrequests, plugin `Call`s,
   cleanup kills — so one failure can't abort the event loop.
5. **Idempotent cleanup** — `CleanupAll`/`StopEvent`/`Unload` are safe to call
   twice (guards on `Active == null`, `IsDestroyed`).

## Performance budget (100+ players)

| Concern | Mitigation |
| ------- | ---------- |
| Per-frame work | One central tick at `TickHz` (default 2Hz), not per-entity `Update` |
| UI cost | Throttle (`RefreshSeconds`) + diff signature → skip no-op redraws (Step 12) |
| NPC count | `MaxNpcs` hard cap; player-scaling capped |
| Zone scans | `PlayersInside()` uses squared distance, no allocations beyond the result list |
| Memory leaks | `EntityPool.CleanupAll` after a `CleanupGrace` delay kills 100% of spawns |
| Webrequests | `PushSeconds`/milestone-only gates; async, non-blocking |
| GC | Reuse `CuiElementContainer` locals per draw; avoid LINQ in the tick path |

## Persistence (offline rewards)

For participants offline at grant time, write pending grants to a data file and
flush on connect:

```csharp
private Dictionary<ulong, int> pendingTokens; // loaded via Interface.Oxide.DataFileSystem
void OnPlayerConnected(BasePlayer p)
{
    if (pendingTokens != null && pendingTokens.TryGetValue(p.userID, out var amt))
    { /* grant amt, remove, save */ }
}
```

Next: **[Step 17 — Roadmap](17-roadmap.md)**.
