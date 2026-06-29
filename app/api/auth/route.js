// In-game account auth for Project Rift (called by the Carbon plugin).
//
//   POST /api/auth  { action:"signup"|"login", email, password, steamId, name }
//   header: x-api-key  (must match PROJECT_RIFT_API_KEY)
//
// Uses Firebase Auth REST (web API key) to create/verify the email+password
// account, then links it to the player's SteamID in Firestore via Admin SDK.
import { adminDb, adminReady } from "@/lib/firebaseAdmin";

export const dynamic = "force-dynamic";

const WEB_KEY = process.env.NEXT_PUBLIC_FIREBASE_API_KEY;
const IDENTITY = "https://identitytoolkit.googleapis.com/v1/accounts";

async function firebaseAuthRest(path, payload) {
  const r = await fetch(`${IDENTITY}:${path}?key=${WEB_KEY}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...payload, returnSecureToken: true }),
  });
  const data = await r.json();
  if (!r.ok) {
    const code = data?.error?.message || "AUTH_FAILED";
    throw new Error(code);
  }
  return data; // { localId, idToken, email, ... }
}

function friendly(code) {
  const map = {
    EMAIL_EXISTS: "That email is already registered. Try logging in.",
    EMAIL_NOT_FOUND: "No account with that email.",
    INVALID_PASSWORD: "Wrong password.",
    INVALID_LOGIN_CREDENTIALS: "Wrong email or password.",
    WEAK_PASSWORD: "Password must be at least 6 characters.",
    INVALID_EMAIL: "That email is not valid.",
  };
  return map[code] || "Authentication failed.";
}

export async function POST(request) {
  const apiKey = process.env.PROJECT_RIFT_API_KEY;
  if (apiKey && request.headers.get("x-api-key") !== apiKey)
    return Response.json({ ok: false, error: "unauthorized" }, { status: 401 });

  if (!WEB_KEY)
    return Response.json(
      { ok: false, error: "Firebase web API key not configured on the website." },
      { status: 503 }
    );

  let body;
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, error: "invalid json" }, { status: 400 });
  }

  const { action, email, password, steamId, name } = body || {};
  if (!email || !password || !steamId)
    return Response.json({ ok: false, error: "email, password and steamId required" }, { status: 400 });

  try {
    const path = action === "signup" ? "signUp" : "signInWithPassword";
    const result = await firebaseAuthRest(path, { email, password });
    const uid = result.localId;

    // link Steam <-> account in Firestore (if Admin SDK is configured)
    const db = adminDb();
    if (db) {
      const now = Date.now();
      await db.collection("users").doc(uid).set(
        {
          uid,
          email,
          steamId: String(steamId),
          name: name || null,
          updatedAt: now,
          ...(action === "signup" ? { createdAt: now } : {}),
        },
        { merge: true }
      );
      await db.collection("steamLinks").doc(String(steamId)).set({ uid, email }, { merge: true });
    }

    return Response.json({
      ok: true,
      uid,
      linked: adminReady(),
      action: action === "signup" ? "signup" : "login",
    });
  } catch (e) {
    return Response.json({ ok: false, error: friendly(e.message), code: e.message }, { status: 400 });
  }
}
