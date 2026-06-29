// Firebase Admin (server-side) — used by auth + stats API routes.
// Activates only when FIREBASE_SERVICE_ACCOUNT is set (the service-account
// JSON, stringified). Until then every getter returns null and the routes
// respond with a clear "not configured" message.
import { initializeApp, getApps, getApp, cert } from "firebase-admin/app";
import { getAuth } from "firebase-admin/auth";
import { getFirestore } from "firebase-admin/firestore";

let app = null;

function init() {
  if (app) return app;
  const raw = process.env.FIREBASE_SERVICE_ACCOUNT;
  if (!raw) return null;
  try {
    const credObj = JSON.parse(raw);
    app = getApps().length ? getApp() : initializeApp({ credential: cert(credObj) });
    return app;
  } catch (e) {
    console.error("Firebase Admin init failed:", e.message);
    return null;
  }
}

export function adminAuth() {
  return init() ? getAuth() : null;
}

export function adminDb() {
  return init() ? getFirestore() : null;
}

export function adminReady() {
  return Boolean(init());
}
