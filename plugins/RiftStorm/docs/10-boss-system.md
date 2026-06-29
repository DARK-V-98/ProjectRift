# Step 10 — Boss System (Rift Overlord)

`BossController` spawns the Rift Overlord, runs its ability AI on cooldown
timers, handles **Rage Mode**, exposes HP for the health-bar UI, and raises
`BossDefeated` on death.

## Abilities

| Ability | Effect | Default CD |
| ------- | ------ | ---------- |
| **EMP Pulse** | Disables/empties active weapons + electronics in radius; brief screen FX | 25s |
| **Lightning Strike** | Targets a random player in zone; AoE damage + lightning FX | 12s |
| **Area Damage** | Shockwave from boss; damage falls off with distance | 18s |
| **Summon Reinforcements** | Spawns N elite scientists | 30s |
| **Rage Mode** | At ≤30% HP: damage ×1.5, cooldowns ×0.6, red aura | one-shot |

## `src/Managers/BossController.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class BossController
        {
            private readonly RiftContext ctx;
            private RiftEvent ev;
            private BaseCombatEntity boss;
            private readonly Dictionary<string, float> cd = new Dictionary<string, float>();
            private Timer abilityTimer;
            private bool raged;

            public event Action BossDefeated;

            public BossController(RiftContext ctx) { this.ctx = ctx; }

            public void SpawnBoss(RiftEvent ev)
            {
                this.ev = ev;
                var p = ctx.Config.Boss.Profile;
                var pos = ev.Center; pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 0.5f;
                boss = GameManager.server.CreateEntity(p.Prefab, pos) as BaseCombatEntity;
                if (boss == null) { ctx.Logger.Error("Boss prefab failed to spawn"); return; }
                boss.Spawn();
                boss.InitializeHealth(ctx.Config.Boss.Health, ctx.Config.Boss.Health);
                boss.gameObject.AddComponent<RiftBossTag>();
                ev.Boss = boss;
                ev.BossMaxHp = ctx.Config.Boss.Health;
                ctx.Pool.Track(boss);

                ctx.Broadcast(ctx.Config.Announce.Prefix +
                    $" <color=#B026FF>{ctx.Config.Boss.Name}</color> has emerged!");
                ctx.Discord.Notify(new RiftNotice {
                    Type = NoticeType.BossSpawned, Title = "👑 Rift Overlord",
                    Message = $"{ctx.Config.Boss.Name} has emerged with {ctx.Config.Boss.Health:0} HP.",
                    LocationName = ev.Location.Name });

                StartAbilities();
            }

            private void StartAbilities()
            {
                abilityTimer?.Destroy();
                void Loop()
                {
                    if (boss == null || boss.IsDestroyed) return;
                    CheckRage();
                    TryAbility("emp",       ctx.Config.Boss.EmpCooldown,       EmpPulse);
                    TryAbility("lightning", ctx.Config.Boss.LightningCooldown, LightningStrike);
                    TryAbility("area",      ctx.Config.Boss.AreaCooldown,      AreaDamage);
                    TryAbility("summon",    ctx.Config.Boss.SummonCooldown,    Summon);
                    abilityTimer = ctx.Timer(1f, Loop);
                }
                abilityTimer = ctx.Timer(2f, Loop);
            }

            private void TryAbility(string key, float baseCd, Action act)
            {
                float mult = raged ? ctx.Config.Boss.RageCdMult : 1f;
                cd.TryGetValue(key, out var ready);
                if (UnityEngine.Time.realtimeSinceStartup < ready) return;
                act();
                cd[key] = UnityEngine.Time.realtimeSinceStartup + baseCd * mult;
            }

            // ---- abilities ---------------------------------------------------
            private void EmpPulse()
            {
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", boss.transform.position);
                foreach (var pl in ev.Zone.PlayersInside())
                {
                    var held = pl.GetActiveItem();
                    if (held?.GetHeldEntity() is BaseProjectile gun)
                        gun.primaryMagazine.contents = 0;            // drains the mag
                    pl.ChatMessage("⚡ EMP PULSE — weapons disrupted!");
                }
            }

            private void LightningStrike()
            {
                var players = ev.Zone.PlayersInside();
                if (players.Count == 0) return;
                var target = players[UnityEngine.Random.Range(0, players.Count)];
                Effect.server.Run("assets/bundled/prefabs/fx/tesla/electrocute_arc.prefab", target.transform.position + Vector3.up * 20f);
                ApplyAoe(target.transform.position, 4f, 60f);
            }

            private void AreaDamage()
            {
                Effect.server.Run("assets/bundled/prefabs/fx/tesla/discharge_setup.prefab", boss.transform.position);
                ApplyAoe(boss.transform.position, 10f, 45f);
            }

            private void Summon()
            {
                var elite = ctx.Config.Waves.Profiles[ctx.Config.Waves.Profiles.Count - 1];
                ctx.Npcs.Summon(ev, elite, ctx.Config.Boss.SummonCount);
                ctx.Broadcast(ctx.Config.Announce.Prefix + " The Overlord summons reinforcements!");
            }

            private void ApplyAoe(Vector3 c, float radius, float maxDamage)
            {
                float mult = raged ? ctx.Config.Boss.RageDamageMult : 1f;
                foreach (var pl in ev.Zone.PlayersInside())
                {
                    float d = Vector3.Distance(c, pl.transform.position);
                    if (d > radius) continue;
                    float dmg = Mathf.Lerp(maxDamage, 0f, d / radius) * mult;
                    pl.Hurt(dmg, Rust.DamageType.ElectricShock, boss);
                }
            }

            private void CheckRage()
            {
                if (raged) return;
                if (ev.BossHpFraction * 100f <= ctx.Config.Boss.RageThreshold)
                {
                    raged = true;
                    ev.BossRaged = true;
                    Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", boss.transform.position);
                    ctx.Broadcast(ctx.Config.Announce.Prefix + " <color=#ff3366>THE OVERLORD ENTERS RAGE MODE!</color>");
                }
            }

            // called from OnEntityDeath hook
            public void NotifyBossDeath()
            {
                abilityTimer?.Destroy();
                BossDefeated?.Invoke();
            }

            public void Reset() { boss = null; raged = false; cd.Clear(); abilityTimer?.Destroy(); }
        }

        public class RiftBossTag : MonoBehaviour { }
    }
}
```

## BossPhase — `src/Phases/BossPhase.cs`

```csharp
public class BossPhase : IRiftPhase
{
    public RiftPhaseId Id => RiftPhaseId.Boss;
    private bool dead;

    public void Enter(RiftEvent ev, RiftContext ctx)
    {
        dead = false;
        ctx.Boss.BossDefeated += OnDead;
        ctx.Boss.SpawnBoss(ev);
        ctx.Ui.ShowBossBarAll(ev);
        void OnDead() { dead = true; ctx.Boss.BossDefeated -= OnDead; }
    }

    public void Tick(RiftEvent ev, RiftContext ctx) => ctx.Ui.UpdateBossBar(ev);
    public bool IsComplete(RiftEvent ev) => dead;
    public void Exit(RiftEvent ev, RiftContext ctx) => ctx.Ui.HideBossBarAll();
}
```

## Boss death wiring (in `RiftStorm.cs` `OnEntityDeath`)

```csharp
if (entity.GetComponent<RiftBossTag>() != null)
{
    // attribute kill + final-blow bonus
    if (info?.InitiatorPlayer != null)
        ev.AddDamage(info.InitiatorPlayer.userID, 0f);
    ctx.Boss.NotifyBossDeath();
}
```

Next: **[Step 11 — Loot manager](11-loot-manager.md)**.
