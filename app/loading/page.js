"use client";
import { useEffect, useRef, useState } from "react";
import Image from "next/image";

const TIPS = [
  "Protect your Tool Cupboard — it controls your whole base.",
  "Raid weekends are dangerous. Upgrade to stone before Friday.",
  "Join the Discord for free starter kits and giveaways.",
  "Never run with a bag full of sulfur. Stash it first.",
  "Roof campers can't shoot what they can't see — break line of sight.",
  "Recyclers turn junk into scrap. Always recycle before you log off.",
  "Wipe day is the great equalizer. Rush metal fragments early.",
  "Teamwork wins raids. Find squads in #lfg on Discord.",
];

function useCountdown(targetIso) {
  const [parts, setParts] = useState(null);
  useEffect(() => {
    if (!targetIso) return;
    const tick = () => {
      const diff = new Date(targetIso).getTime() - Date.now();
      if (diff <= 0) return setParts({ d: 0, h: 0, m: 0, s: 0 });
      setParts({
        d: Math.floor(diff / 86400000),
        h: Math.floor((diff / 3600000) % 24),
        m: Math.floor((diff / 60000) % 60),
        s: Math.floor((diff / 1000) % 60),
      });
    };
    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [targetIso]);
  return parts;
}

export default function LoadingScreen() {
  const [data, setData] = useState(null);
  const [pct, setPct] = useState(8);
  const [tip, setTip] = useState(0);
  const [muted, setMuted] = useState(true);
  const audioRef = useRef(null);
  const wipe = useCountdown(data?.wipeNext);

  // Poll live server status
  useEffect(() => {
    let alive = true;
    const load = async () => {
      try {
        const r = await fetch("/api/server", { cache: "no-store" });
        const j = await r.json();
        if (alive) setData(j);
      } catch {
        /* keep last */
      }
    };
    load();
    const id = setInterval(load, 5000);
    return () => {
      alive = false;
      clearInterval(id);
    };
  }, []);

  // Decorative progress that eases toward 99% (the site can't read Rust's
  // real loader — this is the visual "entering" animation).
  useEffect(() => {
    const id = setInterval(() => {
      setPct((p) => (p >= 99 ? 99 : p + Math.max(0.4, (99 - p) * 0.04)));
    }, 180);
    return () => clearInterval(id);
  }, []);

  // Rotate tips
  useEffect(() => {
    const id = setInterval(() => setTip((t) => (t + 1) % TIPS.length), 5000);
    return () => clearInterval(id);
  }, []);

  const toggleMute = () => {
    const a = audioRef.current;
    if (!a) return;
    if (muted) {
      a.volume = 0.4;
      a.play().catch(() => {});
      setMuted(false);
    } else {
      a.pause();
      setMuted(true);
    }
  };

  const online = data?.online ?? true;
  const players = data?.players ?? "—";
  const maxPlayers = data?.maxPlayers ?? "—";

  return (
    <main className="ls-root">
      <div className="ls-bg">
        <Image src="/bg.png" alt="" fill priority sizes="100vw" className="ls-bg-img" />
        <div className="ls-bg-veil" />
        <div className="ls-grid" />
      </div>

      {/* optional background music — drop public/loading-music.mp3 */}
      <audio ref={audioRef} src="/loading-music.mp3" loop preload="none" />

      <div className="ls-content">
        {/* logo + brand */}
        <div className="ls-logo">
          <span className="ls-logo-ring" />
          <Image src="/logo.png" alt="Project Rift" width={120} height={120} className="ls-logo-img" priority />
        </div>
        <h1 className="ls-title">
          PROJECT <span>RIFT</span>
        </h1>
        <p className="ls-sub">Entering the Rift…</p>

        {/* progress */}
        <div className="ls-progress">
          <div className="ls-progress-track">
            <i style={{ width: `${pct}%` }} />
          </div>
          <div className="ls-progress-row">
            <span>Loading assets</span>
            <span>{Math.round(pct)}%</span>
          </div>
        </div>

        {/* live status pills */}
        <div className="ls-stats">
          <div className="ls-stat">
            <span className="ls-stat-label">Status</span>
            <span className={`ls-stat-val ${online ? "ok" : "off"}`}>
              <i className="ls-dot" /> {online ? "Online" : "Offline"}
            </span>
          </div>
          <div className="ls-stat">
            <span className="ls-stat-label">Players</span>
            <span className="ls-stat-val">
              {players}
              <small> / {maxPlayers}</small>
            </span>
          </div>
          <div className="ls-stat">
            <span className="ls-stat-label">Region</span>
            <span className="ls-stat-val">{data?.location || "—"}</span>
          </div>
          {data?.ping != null && (
            <div className="ls-stat">
              <span className="ls-stat-label">Ping</span>
              <span className="ls-stat-val">{data.ping}ms</span>
            </div>
          )}
        </div>

        {/* wipe countdown */}
        {wipe && (
          <div className="ls-wipe">
            <span className="ls-wipe-label">Next Wipe</span>
            <div className="ls-wipe-clock">
              {[
                ["Days", wipe.d],
                ["Hrs", wipe.h],
                ["Min", wipe.m],
                ["Sec", wipe.s],
              ].map(([l, v]) => (
                <div key={l} className="ls-wipe-unit">
                  <b>{String(v).padStart(2, "0")}</b>
                  <span>{l}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* rotating tip */}
        <div className="ls-tip" key={tip}>
          <span className="ls-tip-badge">TIP</span>
          {TIPS[tip]}
        </div>

        {/* actions */}
        <div className="ls-actions">
          <a className="btn btn-discord" href={data?.discordUrl || "https://discord.gg"} target="_blank" rel="noreferrer">
            Join Community
          </a>
          <button className="btn btn-ghost" onClick={toggleMute}>
            {muted ? "🔇 Music Off" : "🔊 Music On"}
          </button>
        </div>
      </div>

      <div className="ls-footer">
        Powered by <b>ESYSTEMLK</b> · Managed by <b>TEAM 9Z</b>
      </div>
    </main>
  );
}
