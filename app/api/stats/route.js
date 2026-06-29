// Player stats sync for Project Rift (called by the Carbon plugin).
//
//   POST /api/stats  { steamId, name, kills, deaths, playtimeSec }
//   header: x-api-key  (must match PROJECT_RIFT_API_KEY)
//
// Writes to the linked Firestore user doc so the player dashboard can read it.
// No-ops gracefully if the Admin SDK isn't configured yet.
import { adminDb } from "@/lib/firebaseAdmin";

export const dynamic = "force-dynamic";

export async function POST(request) {
  const apiKey = process.env.PROJECT_RIFT_API_KEY;
  if (apiKey && request.headers.get("x-api-key") !== apiKey)
    return Response.json({ ok: false, error: "unauthorized" }, { status: 401 });

  const db = adminDb();
  if (!db)
    return Response.json({ ok: false, error: "firebase admin not configured" }, { status: 503 });

  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, error: "invalid json" }, { status: 400 });
  }

  const steamId = String(body.steamId || "");
  if (!steamId) return Response.json({ ok: false, error: "steamId required" }, { status: 400 });

  const stats = {
    name: body.name ?? null,
    kills: Number(body.kills) || 0,
    deaths: Number(body.deaths) || 0,
    playtimeSec: Number(body.playtimeSec) || 0,
    kd: Number(body.deaths) > 0 ? +(Number(body.kills) / Number(body.deaths)).toFixed(2) : Number(body.kills) || 0,
    updatedAt: Date.now(),
  };

  try {
    // resolve linked uid; if none, store under steamId so it links on next login
    const link = await db.collection("steamLinks").doc(steamId).get();
    const uid = link.exists ? link.data().uid : null;

    if (uid) {
      await db.collection("users").doc(uid).set({ steamId, stats }, { merge: true });
    } else {
      await db.collection("unlinkedStats").doc(steamId).set(stats, { merge: true });
    }
    return Response.json({ ok: true, linked: Boolean(uid) });
  } catch (e) {
    return Response.json({ ok: false, error: e.message }, { status: 500 });
  }
}
