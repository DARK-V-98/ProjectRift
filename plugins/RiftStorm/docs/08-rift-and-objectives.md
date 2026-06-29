# Step 08 — Rift Visuals & Objectives (Crystals + Stability)

This step covers **Phase 3 (Rift)** and **Phase 5 (Objectives)**. The
`RiftController` spawns and animates the central rift, spawns the 4 Energy
Crystals, and tracks Portal Stability.

## `src/Managers/RiftController.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class RiftController
        {
            private readonly RiftContext ctx;
            private Timer pulseTimer;
            private Vector3 center;

            // Decoupling event: EventManager + UI subscribe.
            public event Action<BaseCombatEntity> CrystalDestroyed;

            public RiftController(RiftContext ctx) { this.ctx = ctx; }

            // ---- the rift itself ---------------------------------------------
            public void SpawnRift(Vector3 at)
            {
                center = at;
                // Anchor FX: beams + electric + smoke column at center.
                RunFx("assets/prefabs/missions/portal/proceduraldungeon/portalentrance.prefab", at);
                RunFx("assets/bundled/prefabs/fx/tesla/discharge_setup.prefab", at + Vector3.up * 2f);
                StartPulse();
                ctx.Logger.Info("Rift spawned + pulsing.");
            }

            private void StartPulse()
            {
                pulseTimer?.Destroy();
                void Pulse()
                {
                    // electric pulse + purple particle burst + floating debris
                    RunFx("assets/bundled/prefabs/fx/tesla/electrocute_arc.prefab", center + Vector3.up * 3f);
                    RunFx(ctx.Config.Crystals.ParticlePrefab, center + Vector3.up * 1.5f);
                    SpawnFloatingDebris();
                    pulseTimer = ctx.Timer(3f, Pulse);
                }
                pulseTimer = ctx.Timer(3f, Pulse);
            }

            private void SpawnFloatingDebris()
            {
                // Lightweight: short-lived debris junk that rises then despawns.
                for (int i = 0; i < 3; i++)
                {
                    var off = UnityEngine.Random.insideUnitSphere * 4f;
                    RunFx("assets/bundled/prefabs/fx/gestures/drink_vomit.prefab", center + off + Vector3.up * 2f);
                }
            }

            // ---- crystals -----------------------------------------------------
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

                    var ent = GameManager.server.CreateEntity(ctx.Config.Crystals.EntityPrefab, pos)
                              as BaseCombatEntity;
                    if (ent == null) continue;
                    ent.Spawn();
                    ent.InitializeHealth(ctx.Config.Crystals.Health, ctx.Config.Crystals.Health);
                    ent.gameObject.AddComponent<RiftCrystalTag>();   // marker for hooks
                    RunFx(ctx.Config.Crystals.ParticlePrefab, pos);  // purple aura
                    ev.Crystals.Add(ent);
                    ctx.Pool.Track(ent);
                }
                ctx.Logger.Info($"{count} Energy Crystals spawned.");
            }

            // Called from OnEntityDeath hook (Step 15 wiring) when a crystal dies.
            public void NotifyCrystalDestroyed(RiftEvent ev, BaseCombatEntity crystal)
            {
                Effect.server.Run(ctx.Config.Crystals.ExplosionPrefab, crystal.transform.position);
                RunFx(ctx.Config.Crystals.ParticlePrefab, crystal.transform.position);
                ev.Crystals.Remove(crystal);
                CrystalDestroyed?.Invoke(crystal);   // → EventManager lowers stability
            }

            public void Despawn()
            {
                pulseTimer?.Destroy();
            }

            public void Explode(Vector3 at)
            {
                // Victory: big purple explosion at the rift center.
                RunFx("assets/prefabs/tools/c4/effects/c4_explosion.prefab", at);
                RunFx(ctx.Config.Crystals.ParticlePrefab, at);
            }

            private void RunFx(string prefab, Vector3 pos)
            {
                Effect.server.Run(prefab, pos);
            }
        }

        // Marker MonoBehaviour to identify crystals in damage/death hooks.
        public class RiftCrystalTag : MonoBehaviour { }
    }
}
```

## RiftSpawnPhase — `src/Phases/RiftPhase.cs`

```csharp
public class RiftSpawnPhase : IRiftPhase
{
    public RiftPhaseId Id => RiftPhaseId.Rift;
    public void Enter(RiftEvent ev, RiftContext ctx)
    {
        ctx.Rift.SpawnRift(ev.Center);
        ctx.Discord.Notify(new RiftNotice {
            Type = NoticeType.RiftOpen, Title = "🌀 Rift Open",
            Message = "A massive rift has torn open.", LocationName = ev.Location.Name });
    }
    public void Tick(RiftEvent ev, RiftContext ctx) { }
    public bool IsComplete(RiftEvent ev) =>
        (DateTime.UtcNow - ev.PhaseStartedUtc).TotalSeconds >= 5; // brief reveal
    public void Exit(RiftEvent ev, RiftContext ctx) { }
}
```

## ObjectivesPhase — `src/Phases/ObjectivesPhase.cs`

Spawns the crystals, then waits until all are destroyed (stability hits 0%),
at which point the boss phase begins.

```csharp
public class ObjectivesPhase : IRiftPhase
{
    public RiftPhaseId Id => RiftPhaseId.Objectives;
    private bool spawned;

    public void Enter(RiftEvent ev, RiftContext ctx)
    {
        ctx.Rift.SpawnCrystals(ev);
        spawned = true;
        ctx.Broadcast(ctx.Config.Announce.Prefix +
            $" Destroy the {ev.CrystalsTotal} Energy Crystals to collapse the portal!");
    }

    public void Tick(RiftEvent ev, RiftContext ctx)
    {
        // Optional: keep a light NPC trickle pressuring players (Step 09).
    }

    // Stability path: 100→75→50→25→0
    public bool IsComplete(RiftEvent ev) => spawned && ev.CrystalsAlive <= 0;

    public void Exit(RiftEvent ev, RiftContext ctx)
    {
        ev.Stability = 0f;
        ctx.Broadcast(ctx.Config.Announce.Prefix + " Portal stability collapsed — the Overlord emerges!");
    }
}
```

## Damage/death wiring (lives in `RiftStorm.cs` hooks)

```csharp
// Identify crystals so they explode + drop stability, and forward boss/NPC deaths.
object OnEntityDeath(BaseCombatEntity entity, HitInfo info)
{
    if (!events.IsRunning || entity == null) return null;
    var ev = events.Active;

    if (entity.GetComponent<RiftCrystalTag>() != null && ev.Crystals.Contains(entity))
        ctx.Rift.NotifyCrystalDestroyed(ev, entity);

    return null;
}

// Track participant damage for reward scoring (Step 11).
void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
{
    if (!events.IsRunning || info?.InitiatorPlayer == null) return;
    if (entity == events.Active.Boss || entity.GetComponent<RiftCrystalTag>() != null)
        events.Active.AddDamage(info.InitiatorPlayer.userID, info.damageTypes.Total());
}
```

Next: **[Step 09 — NPC manager](09-npc-manager.md)**.
