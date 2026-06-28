// Live server status API for Project Rift.
//
//   GET  /api/server  -> latest server status for the website + loading screen
//   POST /api/server  -> heartbeat from the Carbon plugin (auth via x-api-key)
//
// Data priority for GET:
//   1. Fresh plugin heartbeat (< 90s old)         source: "plugin"
//   2. Live GameDig query of the Rust server       source: "gamedig"
//   3. Static fallback                             source: "fallback"
import { getLiveRustStatus } from "@/lib/data";

export const dynamic = "force-dynamic";
export const revalidate = 0;

// In-memory store of the last plugin heartbeat. Survives hot-reload via
// globalThis. (For multi-instance/serverless persistence, swap for Redis or
// Firebase Admin — see rust-plugin/README.md.)
const store = (globalThis.__riftServer ??= { heartbeat: null });

const HEARTBEAT_TTL = 90 * 1000; // 90s

function computeNextWipe() {
  // If an explicit next wipe is configured, use it.
  if (process.env.WIPE_NEXT) return process.env.WIPE_NEXT;
  // Otherwise default to the next Thursday 18:00 UTC (typical Rust wipe day).
  const now = new Date();
  const d = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), 18, 0, 0)
  );
  const day = d.getUTCDay(); // 0 Sun ... 4 Thu
  let add = (4 - day + 7) % 7;
  if (add === 0 && now.getTime() > d.getTime()) add = 7;
  d.setUTCDate(d.getUTCDate() + add);
  return d.toISOString();
}

function baseInfo() {
  return {
    hostname: process.env.RUST_SERVER_NAME || "PROJECT RIFT | RUST",
    location: process.env.RUST_SERVER_LOCATION || "Singapore",
    region: process.env.RUST_SERVER_REGION || "Asia",
    discordUrl: process.env.NEXT_PUBLIC_DISCORD_URL || "https://discord.gg",
    websiteUrl:
      process.env.NEXT_PUBLIC_SITE_URL || "https://projectrift.esystemlk.com",
    wipeNext: computeNextWipe(),
  };
}

export async function GET() {
  const info = baseInfo();

  // 1) Fresh plugin heartbeat
  const hb = store.heartbeat;
  if (hb && Date.now() - hb.at < HEARTBEAT_TTL) {
    return Response.json({
      ...info,
      online: true,
      status: "LIVE",
      players: hb.players ?? 0,
      maxPlayers: hb.maxPlayers ?? 0,
      queued: hb.queued ?? 0,
      joining: hb.joining ?? 0,
      ping: hb.ping ?? null,
      hostname: hb.hostname || info.hostname,
      source: "plugin",
      updatedAt: new Date(hb.at).toISOString(),
    });
  }

  // 2) Live GameDig query
  try {
    const live = await getLiveRustStatus();
    if (live && live.status === "LIVE") {
      return Response.json({
        ...info,
        online: true,
        status: "LIVE",
        players: live.players ?? 0,
        maxPlayers: live.maxPlayers ?? 0,
        ping: null,
        hostname: live.name || info.hostname,
        source: "gamedig",
        updatedAt: new Date().toISOString(),
      });
    }
    if (live && live.status === "OFFLINE") {
      return Response.json({
        ...info,
        online: false,
        status: "OFFLINE",
        players: 0,
        maxPlayers: 0,
        ping: null,
        source: "gamedig",
        updatedAt: new Date().toISOString(),
      });
    }
  } catch {
    // ignore, fall through to fallback
  }

  // 3) Fallback
  return Response.json({
    ...info,
    online: true,
    status: "LIVE",
    players: 198,
    maxPlayers: 250,
    ping: null,
    source: "fallback",
    updatedAt: new Date().toISOString(),
  });
}

export async function POST(request) {
  const key = process.env.PROJECT_RIFT_API_KEY;
  if (key) {
    const provided = request.headers.get("x-api-key");
    if (provided !== key) {
      return Response.json({ ok: false, error: "unauthorized" }, { status: 401 });
    }
  }

  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, error: "invalid json" }, { status: 400 });
  }

  store.heartbeat = {
    at: Date.now(),
    players: Number(body.players) || 0,
    maxPlayers: Number(body.maxPlayers) || 0,
    queued: Number(body.queued) || 0,
    joining: Number(body.joining) || 0,
    ping: body.ping != null ? Number(body.ping) : null,
    hostname: typeof body.hostname === "string" ? body.hostname : null,
  };

  return Response.json({ ok: true, received: store.heartbeat });
}
