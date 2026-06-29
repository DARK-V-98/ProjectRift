// Live Rift Storm event API for Project Rift.
//
//   GET  /api/rift  -> latest Rift Storm event snapshot for the website/overlays
//   POST /api/rift  -> live push from the RiftStorm Carbon plugin (auth x-api-key)
//
// Mirrors /api/server: in-memory store (survives hot-reload via globalThis).
// For multi-instance/serverless persistence, swap for Redis or Firebase Admin.

export const dynamic = "force-dynamic";
export const revalidate = 0;

const store = (globalThis.__riftEvent ??= { snapshot: null });

// Treat a snapshot older than this as idle (plugin stopped pushing).
const TTL = 120 * 1000; // 120s

function idle() {
  return { status: "idle", updatedUtc: new Date().toISOString() };
}

export async function GET() {
  const snap = store.snapshot;
  if (!snap) return Response.json(idle());

  const age = Date.now() - new Date(snap.updatedUtc || 0).getTime();
  if (age > TTL && snap.status !== "idle") {
    return Response.json({ ...idle(), stale: true });
  }
  return Response.json(snap, { headers: { "Cache-Control": "no-store" } });
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

  store.snapshot = { ...body, receivedUtc: new Date().toISOString() };
  return Response.json({ ok: true });
}
