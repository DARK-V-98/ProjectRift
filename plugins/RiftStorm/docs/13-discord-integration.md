# Step 13 — Discord Integration

`DiscordService` implements `INotifier` and posts a rich embed to a Discord
webhook for each milestone, with timestamps. It's behind a config toggle and
fails silently (logged) if the webhook is unset/unreachable.

## Events posted

| NoticeType | When | Embed title |
| ---------- | ---- | ----------- |
| `Detected` | Phase 1 start | ⚡ Rift Storm Detected |
| `StormActive` | Phase 2 start | 🌩️ Storm Active |
| `BossSpawned` | Phase 6 start | 👑 Rift Overlord |
| `BossDefeated` | Phase 7 start | 🏆 Victory |
| `Finished` | After rewards | ✅ Event Finished |

## `src/Integrations/INotifier.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public interface INotifier { void Notify(RiftNotice notice); }
    }
}
```

## `src/Integrations/DiscordService.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class DiscordService : INotifier
        {
            private readonly RiftContext ctx;
            private readonly RiftStorm plugin;   // for webrequest access

            public DiscordService(RiftContext ctx, RiftStorm plugin)
            { this.ctx = ctx; this.plugin = plugin; }

            public void Notify(RiftNotice n)
            {
                var cfg = ctx.Config.Discord;
                if (!cfg.Enabled || string.IsNullOrEmpty(cfg.Webhook)) return;

                var fields = new JArray();
                if (!string.IsNullOrEmpty(n.LocationName))
                    fields.Add(Field("Location", n.LocationName, true));
                foreach (var kv in n.Fields) fields.Add(Field(kv.Key, kv.Value, true));

                var embed = new JObject
                {
                    ["title"] = n.Title,
                    ["description"] = n.Message,
                    ["color"] = cfg.Color,
                    ["timestamp"] = n.TimestampUtc.ToString("o"),
                    ["footer"] = new JObject { ["text"] = "PROJECT RIFT • Rift Storm" },
                    ["fields"] = fields
                };

                var payload = new JObject { ["embeds"] = new JArray { embed } };
                if (!string.IsNullOrEmpty(cfg.MentionRole) && IsLoud(n.Type))
                    payload["content"] = $"<@&{cfg.MentionRole}>";

                plugin.webrequest.Enqueue(cfg.Webhook, payload.ToString(),
                    (code, resp) =>
                    {
                        if (code != 204 && code != 200)
                            ctx.Logger.Warn($"Discord webhook returned {code}: {resp}");
                    },
                    plugin, Oxide.Core.Libraries.RequestMethod.POST,
                    new Dictionary<string, string> { ["Content-Type"] = "application/json" });
            }

            private static bool IsLoud(NoticeType t) =>
                t == NoticeType.Detected || t == NoticeType.BossSpawned;

            private static JObject Field(string name, string value, bool inline) =>
                new JObject { ["name"] = name, ["value"] = value, ["inline"] = inline };
        }
    }
}
```

## Example embed for "Detected"

```json
{
  "embeds": [{
    "title": "⚡ Rift Storm Detected",
    "description": "An unstable dimensional rift has appeared.",
    "color": 11544575,
    "timestamp": "2026-06-29T18:02:11.000Z",
    "footer": { "text": "PROJECT RIFT • Rift Storm" },
    "fields": [{ "name": "Location", "value": "Launch Site", "inline": true }]
  }],
  "content": "<@&123456789012345678>"
}
```

## Notes

- Uses Oxide/Carbon's built-in `webrequest` — no extra dependency. If you run
  the **DiscordMessages**/**Discord Extension** plugin you can swap `Notify` to
  call it via `[PluginReference]`; the rest is unchanged (Open/Closed).
- All HTTPS in the dev environment goes through the agent proxy; on a real
  server it's a direct outbound POST to `discord.com`.
- Rate-limit safety: milestones are infrequent (a handful per event), so no
  batching is needed. If you add per-wave posts, debounce them.

Next: **[Step 14 — Website API](14-website-api.md)**.
