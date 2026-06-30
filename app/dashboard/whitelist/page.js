"use client";
import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/useAuth";

// ---- form field definitions (mirror the official applications) -------------
const PLAYER_FIELDS = [
  { name: "fullName", label: "Full Name", required: true },
  { name: "age", label: "Age", required: true, type: "number" },
  { name: "country", label: "Country", required: true },
  { name: "discordUsername", label: "Discord Username (e.g. User#0001 or @username)", required: true },
  { name: "discordUserId", label: "Discord User ID", required: true },
  { name: "steamProfileUrl", label: "Steam Profile URL", required: true },
  { name: "steamId64", label: "SteamID64", required: true },
  { name: "hoursPlayed", label: "Hours Played in Rust", required: true, type: "number" },
  { name: "banned", label: "Have you ever been banned? If yes, explain.", area: true },
  { name: "whyJoin", label: "Why do you want to join PROJECT RIFT?", area: true, required: true },
  { name: "playerType", label: "What type of player are you? (PvP / Builder / Farmer / Roleplay / Mixed)", required: true },
  { name: "followRules", label: "Will you follow all server rules?", required: true },
  { name: "friends", label: "Do you have friends already playing on the server? If yes, list their names.", area: true },
  { name: "anythingElse", label: "Anything else you'd like us to know?", area: true },
];

const STREAMER_FIELDS = [
  { name: "fullName", label: "Full Name", required: true },
  { name: "channelName", label: "Creator/Channel Name", required: true },
  { name: "country", label: "Country", required: true },
  { name: "discordUsername", label: "Discord Username", required: true },
  { name: "discordUserId", label: "Discord User ID", required: true },
  { name: "steamProfileUrl", label: "Steam Profile URL", required: true },
  { name: "steamId64", label: "SteamID64", required: true },
  { name: "twitchUrl", label: "Twitch Channel URL" },
  { name: "youtubeUrl", label: "YouTube Channel URL" },
  { name: "otherPlatformUrl", label: "Kick/Facebook Gaming URL (if applicable)" },
  { name: "avgViewers", label: "Average Concurrent Viewers", required: true, type: "number" },
  { name: "totalFollowers", label: "Total Followers/Subscribers", required: true, type: "number" },
  { name: "streamFrequency", label: "How often do you stream Rust?", required: true },
  { name: "featureRegularly", label: "Will PROJECT RIFT be featured regularly on your content?", required: true },
  { name: "promotedBefore", label: "Have you previously promoted Rust servers? If yes, which ones?", area: true },
  { name: "bestLinks", label: "Links to your best Rust videos/streams", area: true },
  { name: "whyWhitelist", label: "Why do you want a Streamer Whitelist?", area: true, required: true },
  { name: "anythingElse", label: "Anything else you'd like us to know?", area: true },
];

function blank(fields) {
  return fields.reduce((o, f) => ((o[f.name] = ""), o), {});
}

export default function WhitelistPage() {
  const router = useRouter();
  const { user, loading } = useAuth();
  const [type, setType] = useState("player"); // "player" | "streamer"
  const [player, setPlayer] = useState(() => blank(PLAYER_FIELDS));
  const [streamer, setStreamer] = useState(() => blank(STREAMER_FIELDS));
  const [agree, setAgree] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState(null);

  useEffect(() => {
    if (!loading && !user) router.replace("/login");
  }, [user, loading, router]);

  const fields = type === "player" ? PLAYER_FIELDS : STREAMER_FIELDS;
  const values = type === "player" ? player : streamer;
  const setValues = type === "player" ? setPlayer : setStreamer;

  const set = (name, v) => setValues((prev) => ({ ...prev, [name]: v }));

  const missingRequired = useMemo(
    () => fields.some((f) => f.required && !String(values[f.name] || "").trim()),
    [fields, values]
  );

  const submit = async (e) => {
    e.preventDefault();
    if (!user) return;
    if (!agree) {
      setResult({ ok: false, text: "You must confirm the declaration before submitting." });
      return;
    }
    setBusy(true);
    setResult(null);
    try {
      const token = await user.getIdToken();
      const r = await fetch("/api/whitelist", {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
        body: JSON.stringify({ type, data: values }),
      });
      const j = await r.json();
      if (r.ok && j.ok) {
        setResult({ ok: true, text: "Application submitted! Our staff team will review it. Track its status on your dashboard." });
        setValues(blank(fields));
        setAgree(false);
      } else {
        setResult({ ok: false, text: j.error || `Submit failed (HTTP ${r.status}).` });
      }
    } catch (err) {
      setResult({ ok: false, text: String(err) });
    } finally {
      setBusy(false);
    }
  };

  if (loading || !user) {
    return (
      <main className="admin-wrap">
        <div className="dash-loading">Loading…</div>
      </main>
    );
  }

  return (
    <main className="dash-wrap">
      <div className="container wl-inner">
        <div className="wl-top">
          <Link href="/dashboard" className="wl-back">← Dashboard</Link>
          <span className="wl-badge">RUST SERVER WHITELIST</span>
        </div>

        <div className="admin-head" style={{ textAlign: "center", marginBottom: 18 }}>
          <h1 style={{ fontFamily: "var(--font-display)", fontWeight: 800, fontSize: "2rem" }}>
            Whitelist <span className="accent">Application</span>
          </h1>
          <p style={{ color: "var(--muted)" }}>
            Apply for access to the PROJECT RIFT Rust server. Streamers receive a special role on approval.
          </p>
        </div>

        <div className="wl-tabs">
          <button
            type="button"
            className={type === "player" ? "active" : ""}
            onClick={() => { setType("player"); setResult(null); }}
          >
            👤 Player
          </button>
          <button
            type="button"
            className={type === "streamer" ? "active" : ""}
            onClick={() => { setType("streamer"); setResult(null); }}
          >
            🎥 Streamer
          </button>
        </div>

        <form onSubmit={submit} className="admin-form glass wl-form">
          <p className="wl-intro">
            {type === "player"
              ? "Complete this application to request whitelist access to the PROJECT RIFT Rust server."
              : "Complete this application to request Streamer Whitelist access to the PROJECT RIFT Rust server."}
          </p>

          {fields.map((f) => (
            <label key={f.name}>
              {f.label}{f.required ? " *" : ""}
              {f.area ? (
                <textarea
                  rows={3}
                  value={values[f.name]}
                  onChange={(e) => set(f.name, e.target.value)}
                  required={f.required}
                />
              ) : (
                <input
                  type={f.type || "text"}
                  value={values[f.name]}
                  onChange={(e) => set(f.name, e.target.value)}
                  required={f.required}
                />
              )}
            </label>
          ))}

          <label className="wl-declare">
            <input
              type="checkbox"
              checked={agree}
              onChange={(e) => setAgree(e.target.checked)}
            />
            <span>
              I confirm that the information provided is accurate. I understand that whitelist
              approval is at the discretion of the PROJECT RIFT staff team.
            </span>
          </label>

          {result && (
            <div className={`admin-status ${result.ok ? "ok" : "err"}`}>{result.text}</div>
          )}

          <button type="submit" className="btn btn-primary" disabled={busy || missingRequired || !agree}>
            {busy ? "Submitting…" : `Submit ${type === "streamer" ? "Streamer" : "Player"} Application`}
          </button>
          {missingRequired && (
            <p className="wl-hint">Fill in all required (*) fields to enable submit.</p>
          )}
        </form>
      </div>
    </main>
  );
}
