// Approved-whitelist feed for the Rust server plugin.
//
//   GET /api/whitelist/approved
//     header: x-api-key  (must match PROJECT_RIFT_API_KEY)
//     → { ok:true, count, steamIds:[ "7656...", ... ] }
//
// The RiftWhitelist Carbon plugin polls this and only lets these SteamIDs join.
import { adminDb } from "@/lib/firebaseAdmin";

export const dynamic = "force-dynamic";

export async function GET(request) {
  const key = request.headers.get("x-api-key");
  if (!process.env.PROJECT_RIFT_API_KEY || key !== process.env.PROJECT_RIFT_API_KEY)
    return Response.json({ ok: false, error: "unauthorized" }, { status: 401 });

  const db = adminDb();
  if (!db) return Response.json({ ok: false, error: "database not configured" }, { status: 503 });

  try {
    const snap = await db
      .collection("whitelistApplications")
      .where("status", "==", "approved")
      .get();

    const ids = [];
    snap.forEach((doc) => {
      const sid = doc.data()?.data?.steamId64;
      if (sid) ids.push(String(sid).trim());
    });

    // de-dupe + only valid 17-digit SteamID64s
    const steamIds = [...new Set(ids)].filter((s) => /^\d{17}$/.test(s));

    return Response.json(
      { ok: true, count: steamIds.length, steamIds },
      { headers: { "Cache-Control": "no-store" } }
    );
  } catch (e) {
    return Response.json({ ok: false, error: "query failed" }, { status: 500 });
  }
}
