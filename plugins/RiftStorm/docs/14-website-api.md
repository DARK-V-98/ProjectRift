# Step 14 — Website API

The plugin pushes a compact live-event JSON to the Project Rift website (same
pattern as `ProjectRiftCore`'s heartbeat). The website stores the latest payload
and exposes it on a JSON endpoint for the homepage, overlays, and Discord bots.

## Payload schema

```jsonc
{
  "status": "active",            // "idle" | "detecting" | "active" | "boss" | "victory"
  "phase": "Objectives",         // RiftPhaseId name
  "location": "Launch Site",
  "startedUtc": "2026-06-29T18:02:11Z",
  "remainingSeconds": 132,       // countdown (detection) or 0
  "stability": 50,               // portal %
  "crystals": { "alive": 2, "total": 4 },
  "wave": { "index": 2, "count": 3 },
  "boss": { "name": "Rift Overlord", "hpPercent": 73, "raged": false, "alive": true },
  "participants": 14,
  "updatedUtc": "2026-06-29T18:09:43Z"
}
```

## Plugin side — `src/Integrations/ApiService.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class ApiService
        {
            private readonly RiftContext ctx;
            private readonly RiftStorm plugin;
            private float lastPush;

            public ApiService(RiftContext ctx, RiftStorm plugin)
            { this.ctx = ctx; this.plugin = plugin; }

            public string BuildStatusJson(RiftEvent ev)
            {
                if (ev == null)
                    return new JObject { ["status"] = "idle", ["updatedUtc"] = DateTime.UtcNow.ToString("o") }.ToString();

                return new JObject
                {
                    ["status"] = MapStatus(ev.Phase),
                    ["phase"] = ev.Phase.ToString(),
                    ["location"] = ev.Location?.Name ?? "",
                    ["startedUtc"] = ev.StartedUtc.ToString("o"),
                    ["remainingSeconds"] = Mathf.Max(0, Mathf.CeilToInt(ev.CountdownRemaining)),
                    ["stability"] = Mathf.RoundToInt(ev.Stability),
                    ["crystals"] = new JObject { ["alive"] = ev.CrystalsAlive, ["total"] = ev.CrystalsTotal },
                    ["wave"] = new JObject { ["index"] = ev.WaveIndex, ["count"] = ev.WaveCount },
                    ["boss"] = new JObject {
                        ["name"] = ctx.Config.Boss.Name,
                        ["hpPercent"] = Mathf.RoundToInt(ev.BossHpFraction * 100f),
                        ["raged"] = ev.BossRaged,
                        ["alive"] = ev.Boss != null && !ev.Boss.IsDestroyed },
                    ["participants"] = ev.Participants.Count,
                    ["updatedUtc"] = DateTime.UtcNow.ToString("o")
                }.ToString();
            }

            public void Push(RiftEvent ev)
            {
                var cfg = ctx.Config.Api;
                if (!cfg.Enabled || string.IsNullOrEmpty(cfg.PushUrl)) return;
                if (UnityEngine.Time.realtimeSinceStartup - lastPush < cfg.PushSeconds) return;
                lastPush = UnityEngine.Time.realtimeSinceStartup;

                plugin.webrequest.Enqueue(cfg.PushUrl, BuildStatusJson(ev),
                    (code, resp) => { if (code != 200) ctx.Logger.Warn($"API push {code}"); },
                    plugin, Oxide.Core.Libraries.RequestMethod.POST,
                    new Dictionary<string, string> {
                        ["Content-Type"] = "application/json",
                        ["x-api-key"] = cfg.ApiKey
                    });
            }

            private static string MapStatus(RiftPhaseId p)
            {
                switch (p)
                {
                    case RiftPhaseId.Idle: return "idle";
                    case RiftPhaseId.Detection: return "detecting";
                    case RiftPhaseId.Boss: return "boss";
                    case RiftPhaseId.Victory: return "victory";
                    default: return "active";
                }
            }
        }
    }
}
```

Call `ctx.Api.Push(Active)` on every phase change and every few ticks (the
`PushSeconds` gate keeps it cheap).

## Website side — Next.js route handler

Create `app/api/rift/route.js` mirroring the existing `app/api/server`
in-memory-store pattern. **POST** stores the latest payload (auth via
`x-api-key` matching `PROJECT_RIFT_API_KEY`); **GET** returns it.

```js
// app/api/rift/route.js
import { NextResponse } from "next/server";

// in-memory latest snapshot (swap for Redis/Firestore for multi-instance)
globalThis.__rift ??= { status: "idle", updatedUtc: new Date().toISOString() };

export async function POST(req) {
  const key = req.headers.get("x-api-key");
  if (!process.env.PROJECT_RIFT_API_KEY || key !== process.env.PROJECT_RIFT_API_KEY)
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });

  const body = await req.json().catch(() => null);
  if (!body) return NextResponse.json({ error: "bad json" }, { status: 400 });

  globalThis.__rift = { ...body, receivedUtc: new Date().toISOString() };
  return NextResponse.json({ ok: true });
}

export async function GET() {
  const data = globalThis.__rift;
  // stale guard: if last update >2min ago, treat as idle
  const age = Date.now() - new Date(data.updatedUtc || 0).getTime();
  if (age > 120000 && data.status !== "idle")
    return NextResponse.json({ status: "idle", stale: true });
  return NextResponse.json(data, { headers: { "Cache-Control": "no-store" } });
}
```

Add `PROJECT_RIFT_API_KEY` to `.env.local` (same secret as the plugin config and
as `ProjectRiftCore`). The homepage can then poll `GET /api/rift` to render a
live "RIFT STORM ACTIVE" banner with location, boss HP, and participant count.

> **Persistence:** the in-memory store resets on redeploy (same caveat noted in
> the Core README). For production multi-instance, persist to Firestore (already
> wired in `lib/firebase.js`) or Redis.

Next: **[Step 15 — Commands & permissions](15-commands-permissions.md)**.
