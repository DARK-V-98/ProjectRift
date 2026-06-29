# Step 11 — Loot Manager & Victory Rewards

`LootManager` resolves loot tables, fills NPC corpses, builds the Rift Crate on
victory, and grants tokens / cosmetics / titles to qualifying participants.

## Reward grant abstraction (Open/Closed)

Different reward kinds (items, tokens, cosmetic skins, titles/groups) implement
a common interface, so adding a new reward type later (e.g. Battle-Pass XP in
4.0) is a new class, not an edit.

```csharp
public interface IRewardGrant
{
    void Grant(BasePlayer player, RiftContext ctx, bool topDamage);
}
```

## `src/Managers/LootManager.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class LootManager
        {
            private readonly RiftContext ctx;
            private readonly List<IRewardGrant> grants;

            public LootManager(RiftContext ctx)
            {
                this.ctx = ctx;
                grants = new List<IRewardGrant>
                {
                    new TokenGrant(),
                    new CosmeticGrant(),
                    new TitleGrant(),
                };
            }

            // ---- corpse loot --------------------------------------------------
            public void FillCorpse(BaseCombatEntity npc, LootTable table)
            {
                var corpse = npc.GetComponent<LootableCorpse>() ?? FindCorpse(npc);
                if (corpse?.containers == null || corpse.containers.Length == 0) return;
                var container = corpse.containers[0];
                container.Clear();
                RollInto(container, table);
            }

            private void RollInto(ItemContainer container, LootTable table)
            {
                int rolls = UnityEngine.Random.Range(table.MinRolls, table.MaxRolls + 1);
                for (int i = 0; i < rolls; i++)
                {
                    var entry = table.Entries[UnityEngine.Random.Range(0, table.Entries.Count)];
                    if (UnityEngine.Random.value > entry.Chance) continue;
                    int amt = UnityEngine.Random.Range(entry.Min, entry.Max + 1);
                    var item = ItemManager.CreateByName(entry.Shortname, amt, entry.Skin);
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(entry.CustomName)) item.name = entry.CustomName;
                    if (!item.MoveToContainer(container)) item.Remove();
                }
            }

            private LootableCorpse FindCorpse(BaseCombatEntity npc)
            {
                // NPC corpses spawn on death; locate the nearest fresh one.
                var list = Pool.GetList<LootableCorpse>();
                Vis.Entities(npc.transform.position, 2f, list);
                var c = list.Count > 0 ? list[0] : null;
                Pool.FreeList(ref list);
                return c;
            }

            // ---- victory: rift crate -----------------------------------------
            public void SpawnRiftCrate(Vector3 at)
            {
                at.y = TerrainMeta.HeightMap.GetHeight(at) + 0.5f;
                var crate = GameManager.server.CreateEntity(ctx.Config.Rewards.CratePrefab, at)
                            as HackableLockedCrate;
                if (crate == null) { ctx.Logger.Error("Rift Crate prefab failed"); return; }
                crate.Spawn();
                crate.inventory.Clear();
                RollInto(crate.inventory, ctx.Config.Rewards.CrateLoot);
                crate.StartHacking();
                ctx.Logger.Info("Rift Crate spawned + filled.");
                // NOTE: do NOT Pool.Track the crate — reward must persist past cleanup.
            }

            // ---- victory: grant tokens / cosmetics / titles ------------------
            public void GrantRewards(RiftEvent ev)
            {
                ulong topId = TopDamage(ev);
                foreach (var id in ev.Participants)
                {
                    if (!Qualifies(ev, id)) continue;
                    var pl = BasePlayer.FindByID(id);
                    if (pl == null) continue;          // offline: queue via data file (Step 16)
                    bool top = id == topId;
                    foreach (var g in grants) g.Grant(pl, ctx, top);
                }
                ctx.Logger.Reward($"Rewards granted to {ev.Participants.Count} participants (top={topId}).");
            }

            private bool Qualifies(RiftEvent ev, ulong id)
            {
                ev.SecondsInZone.TryGetValue(id, out var s);
                return s >= ctx.Config.Zone.MinParticipationSeconds;
            }

            private ulong TopDamage(RiftEvent ev)
            {
                ulong best = 0; float max = -1f;
                foreach (var kv in ev.DamageByPlayer)
                    if (kv.Value > max) { max = kv.Value; best = kv.Key; }
                return best;
            }
        }

        // ---- reward grant implementations ------------------------------------
        public class TokenGrant : IRewardGrant
        {
            public void Grant(BasePlayer pl, RiftContext ctx, bool top)
            {
                int amt = ctx.Config.Rewards.ParticipantTokens + (top ? ctx.Config.Rewards.TopDamageTokens : 0);
                var cmd = ctx.Config.Rewards.TokenGrantCmd
                    .Replace("{steamid}", pl.UserIDString)
                    .Replace("{amount}", amt.ToString());
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, cmd);
                pl.ChatMessage($"<color=#B026FF>+{amt} Project Rift Tokens</color>");
            }
        }

        public class CosmeticGrant : IRewardGrant
        {
            public void Grant(BasePlayer pl, RiftContext ctx, bool top)
            {
                foreach (var skin in ctx.Config.Rewards.CosmeticSkins)
                {
                    // grant via your skin/cosmetic plugin hook or item with skin id
                    var item = ItemManager.CreateByName("box.wooden", 1, skin);
                    if (item != null && !item.MoveToContainer(pl.inventory.containerMain)) item.Drop(pl.transform.position, Vector3.zero);
                }
            }
        }

        public class TitleGrant : IRewardGrant
        {
            public void Grant(BasePlayer pl, RiftContext ctx, bool top)
            {
                var grp = ctx.Config.Rewards.TitleGroup;
                if (string.IsNullOrEmpty(grp)) return;
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, $"oxide.usergroup add {pl.UserIDString} {grp}");
            }
        }
    }
}
```

## VictoryPhase — `src/Phases/VictoryPhase.cs`

```csharp
public class VictoryPhase : IRiftPhase
{
    public RiftPhaseId Id => RiftPhaseId.Victory;
    private bool done;

    public void Enter(RiftEvent ev, RiftContext ctx)
    {
        // portal explodes — purple explosion + fireworks
        ctx.Rift.Explode(ev.Center);
        Fireworks(ev.Center, ctx);

        ctx.Broadcast(ctx.Config.Announce.Prefix +
            " <color=#00E5FF>THE RIFT OVERLORD HAS FALLEN!</color> The portal collapses!");
        ctx.Discord.Notify(new RiftNotice {
            Type = NoticeType.BossDefeated, Title = "🏆 Victory",
            Message = "The Rift Overlord has been defeated!", LocationName = ev.Location.Name });

        ctx.Npcs.ClearAll();
        ctx.Loot.SpawnRiftCrate(ev.Center);
        ctx.Loot.GrantRewards(ev);

        ctx.Discord.Notify(new RiftNotice {
            Type = NoticeType.Finished, Title = "✅ Event Finished",
            Message = "Rift Crate has dropped. Rewards distributed.", LocationName = ev.Location.Name });

        // brief grace so FX/crate settle before full cleanup
        ctx.Timer(ctx.Config.Performance.CleanupGrace, () => done = true);
    }

    private void Fireworks(Vector3 c, RiftContext ctx)
    {
        for (int i = 0; i < 8; i++)
        {
            var off = UnityEngine.Random.insideUnitCircle * 15f;
            var pos = c + new Vector3(off.x, 12f, off.y);
            Effect.server.Run("assets/prefabs/deployable/fireworks/volcanofirework.prefab", pos);
        }
    }

    public void Tick(RiftEvent ev, RiftContext ctx) { }
    public bool IsComplete(RiftEvent ev) => done;
    public void Exit(RiftEvent ev, RiftContext ctx) { /* EventManager.Cleanup() runs next */ }
}
```

> **Offline participants:** if a qualifying player is offline at grant time,
> persist their pending reward to a data file (`OnPlayerConnected` flush). See
> Step 16's data-file pattern.

Next: **[Step 12 — UI system](12-ui-system.md)**.
