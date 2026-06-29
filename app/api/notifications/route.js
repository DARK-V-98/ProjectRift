// In-game notification queue for Project Rift.
//
//   POST /api/notifications        admin sends a notification (x-api-key)
//   GET  /api/notifications?since=ID   plugin polls for new notifications
//
// Stored in-memory for now (survives hot-reload via globalThis). Swap for
// Firestore once the Firebase Admin service account is wired (see /admin).
export const dynamic = "force-dynamic";
export const revalidate = 0;

const store = (globalThis.__riftNotify ??= { items: [], nextId: 1 });

const MAX_KEEP = 50;
const DEFAULT_DURATION = 6; // seconds shown in-game

function active(now = Date.now()) {
  return store.items.filter((n) => n.expiresAt > now);
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const since = Number(searchParams.get("since") || 0);
  const now = Date.now();
  const items = active(now).filter((n) => n.id > since);
  const latestId = store.items.length ? store.items[store.items.length - 1].id : 0;
  return Response.json({ notifications: items, latestId });
}

export async function POST(request) {
  const key = process.env.PROJECT_RIFT_API_KEY;
  if (key) {
    if (request.headers.get("x-api-key") !== key)
      return Response.json({ ok: false, error: "unauthorized" }, { status: 401 });
  }

  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, error: "invalid json" }, { status: 400 });
  }

  const message = (body.message || "").toString().trim();
  if (!message)
    return Response.json({ ok: false, error: "message required" }, { status: 400 });

  const now = Date.now();
  const duration = Math.min(30, Math.max(2, Number(body.durationSec) || DEFAULT_DURATION));
  const notif = {
    id: store.nextId++,
    title: (body.title || "").toString().slice(0, 60),
    message: message.slice(0, 240),
    type: ["info", "success", "warning", "alert"].includes(body.type) ? body.type : "info",
    createdAt: now,
    expiresAt: now + duration * 1000,
    durationSec: duration,
  };

  store.items.push(notif);
  if (store.items.length > MAX_KEEP) store.items.splice(0, store.items.length - MAX_KEEP);

  return Response.json({ ok: true, notification: notif });
}
