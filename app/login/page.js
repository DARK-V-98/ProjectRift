"use client";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import Image from "next/image";
import {
  createUserWithEmailAndPassword,
  signInWithEmailAndPassword,
  updateProfile,
} from "firebase/auth";
import { auth } from "@/lib/firebase";
import { useAuth } from "@/lib/useAuth";

const FRIENDLY = {
  "auth/email-already-in-use": "That email is already registered — try signing in.",
  "auth/invalid-email": "That email address is not valid.",
  "auth/weak-password": "Password must be at least 6 characters.",
  "auth/invalid-credential": "Wrong email or password.",
  "auth/user-not-found": "No account with that email.",
  "auth/wrong-password": "Wrong password.",
  "auth/too-many-requests": "Too many attempts — please wait a moment.",
};

export default function LoginPage() {
  const router = useRouter();
  const { user, loading, configured } = useAuth();
  const [mode, setMode] = useState("login"); // "login" | "signup"
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  // already signed in → go to dashboard
  useEffect(() => {
    if (!loading && user) router.replace("/dashboard");
  }, [user, loading, router]);

  const submit = async (e) => {
    e.preventDefault();
    if (!auth) {
      setError("Sign-in is not configured yet. Add Firebase keys to .env.local.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      if (mode === "signup") {
        const cred = await createUserWithEmailAndPassword(auth, email.trim(), password);
        if (name.trim()) await updateProfile(cred.user, { displayName: name.trim() });
      } else {
        await signInWithEmailAndPassword(auth, email.trim(), password);
      }
      router.replace("/dashboard");
    } catch (err) {
      setError(FRIENDLY[err.code] || "Authentication failed. Please try again.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="admin-wrap">
      <div className="admin-card glass auth-card">
        <div className="admin-head" style={{ textAlign: "center" }}>
          <Image
            src="/logo.png"
            alt="Project Rift"
            width={64}
            height={64}
            style={{ margin: "0 auto 10px", display: "block", borderRadius: "50%" }}
          />
          <span className="eyebrow" style={{ justifyContent: "center" }}>
            {mode === "signup" ? "Create account" : "Member access"}
          </span>
          <h1>
            PROJECT <span className="accent">RIFT</span>
          </h1>
          <p>
            {mode === "signup"
              ? "Register to apply for the whitelist and access your dashboard."
              : "Sign in to your dashboard and whitelist applications."}
          </p>
        </div>

        <div className="auth-tabs">
          <button
            type="button"
            className={mode === "login" ? "active" : ""}
            onClick={() => { setMode("login"); setError(null); }}
          >
            Sign In
          </button>
          <button
            type="button"
            className={mode === "signup" ? "active" : ""}
            onClick={() => { setMode("signup"); setError(null); }}
          >
            Sign Up
          </button>
        </div>

        <form onSubmit={submit} className="admin-form">
          {mode === "signup" && (
            <label>
              Display name
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Your in-game / display name"
                maxLength={40}
              />
            </label>
          )}
          <label>
            Email
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@example.com"
              autoComplete="email"
            />
          </label>
          <label>
            Password
            <input
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="At least 6 characters"
              autoComplete={mode === "signup" ? "new-password" : "current-password"}
            />
          </label>

          {error && <div className="admin-status err">{error}</div>}
          {!configured && (
            <div className="admin-status err">
              Firebase web config missing — set NEXT_PUBLIC_FIREBASE_* in .env.local.
            </div>
          )}

          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? "Please wait…" : mode === "signup" ? "Create account" : "Sign in"}
          </button>
        </form>

        <p className="auth-foot">
          <Link href="/">← Back to home</Link>
        </p>
      </div>
    </main>
  );
}
