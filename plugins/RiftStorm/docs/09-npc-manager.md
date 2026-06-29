# Step 09 — NPC Manager (waves + custom AI)

`NpcManager` spawns the three escalating waves from `NpcProfile` definitions,
applies custom HP / weapon / kit / AI tuning / loot, tracks the alive count, and
raises `WaveCleared` so the EventManager can advance.

## `src/Managers/NpcManager.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class NpcManager
        {
            private readonly RiftContext ctx;
            private readonly HashSet<BaseCombatEntity> alive = new HashSet<BaseCombatEntity>();
            private int currentWave = -1;
            private Timer watchTimer;

            public event Action<int> WaveCleared;

            public NpcManager(RiftContext ctx) { this.ctx = ctx; }

            public int AliveCount() { alive.RemoveWhere(n => n == null || n.IsDestroyed); return alive.Count; }

            public void SpawnWave(RiftEvent ev, int waveIndex)
            {
                var profiles = ctx.Config.Waves.Profiles;
                if (waveIndex < 0 || waveIndex >= profiles.Count) return;
                currentWave = waveIndex;
                ev.WaveIndex = waveIndex;
                ev.WaveCount = profiles.Count;
                var profile = profiles[waveIndex];

                int count = ComputeCount(profile);
                for (int i = 0; i < count && AliveCount() < ctx.Config.Performance.MaxNpcs; i++)
                    SpawnOne(ev, profile);

                ctx.Broadcast(ctx.Config.Announce.Prefix +
                    $" Wave {waveIndex + 1}/{profiles.Count}: {profile.Name} x{count}");
                ctx.Discord.Notify(new RiftNotice {
                    Type = NoticeType.WaveStart, Title = $"⚔️ Wave {waveIndex + 1}",
                    Message = $"{profile.Name} x{count}", LocationName = ev.Location.Name });
                StartWatch(waveIndex);
            }

            private int ComputeCount(NpcProfile p)
            {
                if (!ctx.Config.Waves.ScaleWithPlayers) return p.Count;
                int online = BasePlayer.activePlayerList.Count;
                int extra = online / Mathf.Max(1, ctx.Config.Waves.PlayersPerExtraNpc);
                return Mathf.Min(p.Count + extra, ctx.Config.Performance.MaxNpcs);
            }

            private void SpawnOne(RiftEvent ev, NpcProfile profile)
            {
                var pos = RandomRing(ev.Center, ctx.Config.Waves.SpawnRadius);
                var npc = GameManager.server.CreateEntity(profile.Prefab, pos) as BaseCombatEntity;
                if (npc == null) return;
                npc.Spawn();
                ApplyProfile(npc, profile);
                alive.Add(npc);
                ctx.Pool.Track(npc);
                // tag it so death hooks know it's ours and to which event
                npc.gameObject.AddComponent<RiftNpcTag>().WaveIndex = currentWave;
            }

            // Custom HP / weapon / kit / AI tuning.
            private void ApplyProfile(BaseCombatEntity npc, NpcProfile p)
            {
                npc.InitializeHealth(p.Health, p.Health);

                if (npc is ScientistNPC sci)
                {
                    // weapon
                    GiveWeapon(sci, p.Weapon);
                    // wear
                    foreach (var w in p.Wear) GiveWear(sci, w);
                    // AI tuning via the brain's senses/aim
                    var brain = sci.GetComponent<ScientistBrain>();
                    if (brain != null)
                    {
                        brain.Senses.Init(sci, brain, p.SenseRange, p.SenseRange,
                                          p.SenseRange, -1f, true, false, true, p.SenseRange,
                                          true, false, true, EntityType.Player, true);
                    }
                    sci.damageScale = p.DamageScale;
                    // speed: scale the movement on the navagent if present
                }
            }

            private void GiveWeapon(BasePlayer npc, string shortname)
            {
                var item = ItemManager.CreateByName(shortname, 1);
                if (item == null) return;
                item.MoveToContainer(npc.inventory.containerBelt);
                npc.inventory.UpdateAmmoAmounts?.Invoke();
                // force-hold first belt slot
                var held = npc.inventory.containerBelt.GetSlot(0)?.GetHeldEntity() as HeldEntity;
                held?.SetHeld(true);
            }

            private void GiveWear(BasePlayer npc, string shortname)
            {
                var item = ItemManager.CreateByName(shortname, 1);
                item?.MoveToContainer(npc.inventory.containerWear);
            }

            // poll until the wave is cleared
            private void StartWatch(int waveIndex)
            {
                watchTimer?.Destroy();
                void Check()
                {
                    if (AliveCount() > 0) { watchTimer = ctx.Timer(2f, Check); return; }
                    WaveCleared?.Invoke(waveIndex);
                }
                watchTimer = ctx.Timer(2f, Check);
            }

            public void Summon(RiftEvent ev, NpcProfile profile, int n)
            {
                for (int i = 0; i < n && AliveCount() < ctx.Config.Performance.MaxNpcs; i++)
                    SpawnOne(ev, profile);
            }

            public void ClearAll()
            {
                foreach (var n in alive) if (n != null && !n.IsDestroyed) n.Kill();
                alive.Clear();
                watchTimer?.Destroy();
            }

            private Vector3 RandomRing(Vector3 c, float r)
            {
                var a = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                p.y = TerrainMeta.HeightMap.GetHeight(p) + 0.5f;
                return p;
            }
        }

        public class RiftNpcTag : MonoBehaviour { public int WaveIndex; }
    }
}
```

## WavesPhase — `src/Phases/WavesPhase.cs`

Runs each configured wave in order, waiting for `WaveCleared` between them.

```csharp
public class WavesPhase : IRiftPhase
{
    public RiftPhaseId Id => RiftPhaseId.Waves;
    private int nextWave;
    private bool waiting;
    private bool finished;

    public void Enter(RiftEvent ev, RiftContext ctx)
    {
        nextWave = 0; finished = false; waiting = false;
        ctx.Npcs.WaveCleared += OnCleared;
        SpawnNext(ev, ctx);

        void OnCleared(int idx)
        {
            if (nextWave >= ctx.Config.Waves.Profiles.Count)
            { finished = true; ctx.Npcs.WaveCleared -= OnCleared; return; }
            // brief delay then next wave
            waiting = true;
            ctx.Timer(ctx.Config.Waves.WaveDelay, () => { waiting = false; SpawnNext(ev, ctx); });
        }
    }

    private void SpawnNext(RiftEvent ev, RiftContext ctx)
    {
        ctx.Npcs.SpawnWave(ev, nextWave);
        nextWave++;
    }

    public void Tick(RiftEvent ev, RiftContext ctx) { }
    public bool IsComplete(RiftEvent ev) => finished;
    public void Exit(RiftEvent ev, RiftContext ctx) { }
}
```

## NPC loot on death (wiring)

When a `RiftNpcTag` NPC dies, `LootManager` fills its corpse from the profile's
loot table — see **[Step 11](11-loot-manager.md)**. The death hook also removes
it from `alive` so `WaveCleared` can fire.

> **Better AI option:** for sharper combat, integrate the community
> `HumanNPC`/`BotReSpawn` plugins via `[PluginReference]` and let them own the
> brain; `NpcManager` then just requests spawns and applies loot. The
> profile-driven design means swapping the AI backend doesn't touch the rest.

Next: **[Step 10 — Boss system](10-boss-system.md)**.
