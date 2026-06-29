# Step 06 — Event Manager (scheduler + state machine)

`RiftEventManager` is the brain. It (1) schedules the next event on a random
2–4h timer, (2) resolves a location, (3) builds the `RiftEvent` + `RiftZone`,
and (4) runs the ordered phase pipeline via a single tick.

## Phase interface — `src/Phases/IRiftPhase.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public interface IRiftPhase
        {
            RiftPhaseId Id { get; }
            void Enter(RiftEvent ev, RiftContext ctx);
            void Tick(RiftEvent ev, RiftContext ctx);
            void Exit(RiftEvent ev, RiftContext ctx);
            bool IsComplete(RiftEvent ev);
        }
    }
}
```

## The manager — `src/Managers/RiftEventManager.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class RiftEventManager
        {
            private readonly RiftContext ctx;
            private readonly List<IRiftPhase> phases;   // ordered pipeline
            private int phaseIndex = -1;

            private Timer scheduleTimer;
            private Timer tickTimer;

            public RiftEvent Active { get; private set; }
            public bool IsRunning => Active != null && Active.Phase != RiftPhaseId.Idle;

            public RiftEventManager(RiftContext ctx)
            {
                this.ctx = ctx;
                // Open/Closed: register phases in order. Insert new ones here only.
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

                // subsystem events drive transitions (decoupled)
                ctx.Rift.CrystalDestroyed += OnCrystalDestroyed;
                ctx.Npcs.WaveCleared      += OnWaveCleared;
                ctx.Boss.BossDefeated     += OnBossDefeated;
            }

            // ---- scheduling ---------------------------------------------------
            public void ScheduleNext()
            {
                if (!ctx.Config.Schedule.Enabled) return;
                float hrs = UnityEngine.Random.Range(ctx.Config.Schedule.MinHours,
                                                      ctx.Config.Schedule.MaxHours);
                float secs = hrs * 3600f;
                ctx.Logger.Info($"Next Rift Storm scheduled in {hrs:0.00}h");
                scheduleTimer?.Destroy();
                scheduleTimer = ctx.Timer(secs, TryAutoStart);
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
                var loc = PickLocation();
                if (loc == null) { ctx.Logger.Warn("No valid location; rescheduling."); ScheduleNext(); return; }
                StartEvent(loc);
            }

            // ---- lifecycle ----------------------------------------------------
            public bool StartEvent(RiftLocation loc)
            {
                if (IsRunning) return false;
                Active = new RiftEvent
                {
                    Location = loc,
                    Zone = new RiftZone(loc.Position, ctx.Config.Zone.Radius),
                };
                phaseIndex = -1;
                ctx.Logger.Info($"Rift Storm starting at {loc.Name} {loc.Position}");
                ctx.Api.Push(Active);
                StartTick();
                Advance();                    // enter Detection
                return true;
            }

            public void StopEvent(bool victory)
            {
                if (Active == null) return;
                ctx.Logger.Info($"Rift Storm ending (victory={victory}) after {(DateTime.UtcNow - Active.StartedUtc).TotalMinutes:0.0}m");
                // ensure last phase exits
                CurrentPhase?.Exit(Active, ctx);
                Cleanup();
                Active.Phase = RiftPhaseId.Idle;
                Active = null;
                tickTimer?.Destroy();
                ScheduleNext();
            }

            private IRiftPhase CurrentPhase =>
                (phaseIndex >= 0 && phaseIndex < phases.Count) ? phases[phaseIndex] : null;

            private void Advance()
            {
                CurrentPhase?.Exit(Active, ctx);
                phaseIndex++;
                if (phaseIndex >= phases.Count) { StopEvent(true); return; }
                var next = phases[phaseIndex];
                Active.Phase = next.Id;
                Active.PhaseStartedUtc = DateTime.UtcNow;
                ctx.Logger.Info($"→ Phase {next.Id}");
                next.Enter(Active, ctx);
                ctx.Api.Push(Active);
            }

            // ---- central tick -------------------------------------------------
            private void StartTick()
            {
                tickTimer?.Destroy();
                float interval = 1f / Mathf.Max(0.5f, ctx.Config.Performance.TickHz);
                tickTimer = ctx.Timer(interval, Tick);  // ctx.Timer must repeat (see note)
            }

            public void Tick()
            {
                if (Active == null) return;
                using (ctx.Logger.Perf("tick"))
                {
                    var phase = CurrentPhase;
                    if (phase == null) return;
                    UpdateParticipation();
                    phase.Tick(Active, ctx);
                    ctx.Ui.Refresh(Active);          // throttled internally
                    if (phase.IsComplete(Active)) Advance();
                }
            }

            // ---- event handlers ----------------------------------------------
            private void OnCrystalDestroyed(BaseCombatEntity crystal)
            {
                if (Active == null) return;
                Active.CrystalsAlive = Mathf.Max(0, Active.CrystalsAlive - 1);
                Active.Stability = Mathf.Max(0f,
                    Active.Stability - ctx.Config.Crystals.StabilityPerCrystal);
                ctx.Logger.Info($"Crystal destroyed — stability {Active.Stability}%");
            }

            private void OnWaveCleared(int waveIndex) =>
                ctx.Logger.Info($"Wave {waveIndex + 1} cleared");

            private void OnBossDefeated() =>
                ctx.Logger.Info("Rift Overlord defeated!");

            // ---- helpers ------------------------------------------------------
            private RiftLocation PickLocation()
            {
                var enabled = ctx.Config.Locations.FindAll(l => l.Enabled);
                if (enabled.Count == 0) return null;
                var loc = enabled[UnityEngine.Random.Range(0, enabled.Count)];
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
                    default: // monument
                        foreach (var mon in TerrainMeta.Path.Monuments)
                        {
                            var n = (mon.displayPhrase.english + mon.name).ToLower();
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
                    var p = new Vector3(UnityEngine.Random.Range(-size, size), 0,
                                        UnityEngine.Random.Range(-size, size));
                    p.y = TerrainMeta.HeightMap.GetHeight(p);
                    if (p.y > 1f && !WaterLevel.Test(p, false, false))
                    { pos = p; return true; }
                }
                pos = Vector3.zero; return false;
            }

            private void UpdateParticipation()
            {
                float dt = 1f / Mathf.Max(0.5f, ctx.Config.Performance.TickHz);
                foreach (var pl in Active.Zone.PlayersInside())
                {
                    Active.Participants.Add(pl.userID);
                    Active.SecondsInZone.TryGetValue(pl.userID, out var s);
                    Active.SecondsInZone[pl.userID] = s + dt;
                }
            }

            private void Cleanup()
            {
                ctx.Weather.Restore();
                ctx.Ui.DestroyAll();
                ctx.Pool.CleanupAll(Active);   // kills every registered entity/FX
            }
        }
    }
}
```

> **Repeating timers:** Oxide's `timer.Every(interval, cb)` / Carbon equivalent
> gives a repeating handle. Wire `ctx.Timer` in the composition root to whichever
> you need; for the tick use a repeating timer, for one-shots use `timer.Once`.
> Keep a reference and `Destroy()` it on stop/unload (Step 16).

## Detection phase example — `src/Phases/DetectionPhase.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class DetectionPhase : IRiftPhase
        {
            public RiftPhaseId Id => RiftPhaseId.Detection;

            public void Enter(RiftEvent ev, RiftContext ctx)
            {
                ev.CountdownRemaining = ctx.Config.Schedule.CountdownSeconds;
                ctx.Broadcast($"⚡ <b>RIFT STORM DETECTED</b>\nAn unstable dimensional rift has appeared.\nLocation: <b>{ev.Location.Name}</b>\nStarts in {Mathf.RoundToInt(ev.CountdownRemaining)}s.");
                ctx.Discord.Notify(new RiftNotice {
                    Type = NoticeType.Detected, Title = "⚡ Rift Storm Detected",
                    Message = "An unstable dimensional rift has appeared.",
                    LocationName = ev.Location.Name });
                ctx.Ui.ShowCountdownAll(ev);
                PlayWarning(ctx, ev.Center);
            }

            public void Tick(RiftEvent ev, RiftContext ctx)
            {
                ev.CountdownRemaining -= 1f / Mathf.Max(0.5f, ctx.Config.Performance.TickHz);
                // marker beeps at 60/30/10/3..1 handled in UiManager via remaining
            }

            public bool IsComplete(RiftEvent ev) => ev.CountdownRemaining <= 0f;

            public void Exit(RiftEvent ev, RiftContext ctx) =>
                ctx.Broadcast("The Rift Storm is here. Brace yourselves.");

            private void PlayWarning(RiftContext ctx, Vector3 at)
            {
                foreach (var pl in BasePlayer.activePlayerList)
                    Effect.server.Run(ctx.Config.Announce.WarnSound, pl.transform.position);
            }
        }
    }
}
```

The remaining phases (`StormPhase`, `RiftSpawnPhase`, `WavesPhase`,
`ObjectivesPhase`, `BossPhase`, `VictoryPhase`) follow the same shape and are
detailed in Steps 07–11. Each one just delegates to the relevant manager.

Next: **[Step 07 — Weather controller](07-weather-controller.md)**.
