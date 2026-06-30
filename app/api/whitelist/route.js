// Whitelist applications API for PROJECT RIFT.
//
//   POST /api/whitelist  { type:"player"|"streamer", data:{...} }
//     header: Authorization: Bearer <Firebase ID token>
//     → stores the application in Firestore under whitelist/{uid}/apps
//
//   GET /api/whitelist
//     header: Authorization: Bearer <Firebase ID token>
//     → returns the signed-in user's applications (newest first)
//
import { adminAuth, adminDb } from "@/lib/firebaseAdmin";
import { ADMIN_STATUSES } from "@/lib/whitelistStatus";

export const dynamic = "force-dynamic";

const MAX_FIELD = 2000;

// Admin allowlist: comma-separated emails in RIFT_ADMIN_EMAILS, plus any user
// with a Firebase custom claim { admin: true }.
function adminEmails() {
  return (process.env.RIFT_ADMIN_EMAILS || "")
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);
}

async function requireUser(request) {
  const auth = adminAuth();
  if (!auth) return { error: "Auth not configured on the server.", status: 503 };
  const header = request.headers.get("authorization") || "";
  const token = header.startsWith("Bearer ") ? header.slice(7) : null;
  if (!token) return { error: "Missing auth token.", status: 401 };
  try {
    const decoded = await auth.verifyIdToken(token);
    const email = decoded.email || null;
    const isAdmin =
      decoded.admin === true ||
      (email && adminEmails().includes(email.toLowerCase()));
    return { uid: decoded.uid, email, isAdmin };
  } catch {
    return { error: "Invalid or expired session.", status: 401 };
  }
}

function sanitize(data) {
  const out = {};
  for (const [k, v] of Object.entries(data || {})) {
    if (typeof v === "string") out[k] = v.slice(0, MAX_FIELD).trim();
    else if (typeof v === "number" || typeof v === "boolean") out[k] = v;
  }
  return out;
}

export async function POST(request) {
  const u = await requireUser(request);
  if (u.error) return Response.json({ ok: false, error: u.error }, { status: u.status });

  const db = adminDb();
  if (!db) return Response.json({ ok: false, error: "Database not configured." }, { status: 503 });

  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, error: "Invalid JSON." }, { status: 400 });
  }

  const type = body?.type === "streamer" ? "streamer" : "player";
  const data = sanitize(body?.data);
  if (!data || Object.keys(data).length === 0)
    return Response.json({ ok: false, error: "No application data provided." }, { status: 400 });

  const now = Date.now();
  const doc = {
    uid: u.uid,
    email: u.email,
    type,
    data,
    status: "pending",
    createdAt: now,
    updatedAt: now,
  };

  try {
    const ref = await db.collection("whitelistApplications").add(doc);
    // mirror onto the user doc for quick dashboard lookups
    await db.collection("users").doc(u.uid).set(
      { lastWhitelist: { id: ref.id, type, status: "pending", createdAt: now } },
      { merge: true }
    );
    return Response.json({ ok: true, id: ref.id });
  } catch (e) {
    return Response.json({ ok: false, error: "Failed to save application." }, { status: 500 });
  }
}

export async function GET(request) {
  const u = await requireUser(request);
  if (u.error) return Response.json({ ok: false, error: u.error }, { status: u.status });

  const db = adminDb();
  if (!db) return Response.json({ ok: false, error: "Database not configured." }, { status: 503 });

  const { searchParams } = new URL(request.url);
  const scopeAll = searchParams.get("scope") === "all";

  try {
    // Admin view: every application, with the full form data for review.
    if (scopeAll) {
      if (!u.isAdmin)
        return Response.json({ ok: false, error: "Admin access required." }, { status: 403 });
      const snap = await db.collection("whitelistApplications").get();
      const applications = snap.docs
        .map((d) => ({ id: d.id, ...d.data() }))
        .sort((a, b) => (b.createdAt || 0) - (a.createdAt || 0));
      return Response.json({ ok: true, admin: true, applications });
    }

    // Player view: only their own applications, without the heavy payload.
    const snap = await db
      .collection("whitelistApplications")
      .where("uid", "==", u.uid)
      .get();
    const applications = snap.docs
      .map((d) => ({ id: d.id, ...d.data() }))
      .map(({ data, ...meta }) => meta)
      .sort((a, b) => (b.createdAt || 0) - (a.createdAt || 0));
    return Response.json({ ok: true, applications });
  } catch (e) {
    return Response.json({ ok: false, error: "Failed to load applications." }, { status: 500 });
  }
}

// Admin: update an application's status (+ optional staff note).
export async function PATCH(request) {
  const u = await requireUser(request);
  if (u.error) return Response.json({ ok: false, error: u.error }, { status: u.status });
  if (!u.isAdmin) return Response.json({ ok: false, error: "Admin access required." }, { status: 403 });

  const db = adminDb();
  if (!db) return Response.json({ ok: false, error: "Database not configured." }, { status: 503 });

  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, error: "Invalid JSON." }, { status: 400 });
  }

  const { id, status } = body || {};
  const note = typeof body?.note === "string" ? body.note.slice(0, MAX_FIELD).trim() : "";
  if (!id || !ADMIN_STATUSES.includes(status))
    return Response.json({ ok: false, error: "id and a valid status are required." }, { status: 400 });

  try {
    const ref = db.collection("whitelistApplications").doc(id);
    const cur = await ref.get();
    if (!cur.exists)
      return Response.json({ ok: false, error: "Application not found." }, { status: 404 });

    const now = Date.now();
    await ref.set(
      {
        status,
        staffNote: note,
        reviewedBy: u.email || u.uid,
        reviewedAt: now,
        updatedAt: now,
      },
      { merge: true }
    );

    // mirror onto the applicant's user doc for fast dashboard lookups
    const applicantUid = cur.data().uid;
    if (applicantUid)
      await db.collection("users").doc(applicantUid).set(
        { lastWhitelist: { id, type: cur.data().type, status, createdAt: cur.data().createdAt || now } },
        { merge: true }
      );

    return Response.json({ ok: true });
  } catch (e) {
    return Response.json({ ok: false, error: "Failed to update application." }, { status: 500 });
  }
}
