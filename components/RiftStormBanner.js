"use client";
import { useEffect, useState } from "react";
import { Icon } from "./Icons";

// Live "RIFT STORM ACTIVE" banner. Polls /api/rift (fed by the RiftStorm Carbon
// plugin) and surfaces the current event: location, phase, and the most relevant
// live stat (countdown / portal stability / boss HP). Renders nothing when idle.
const POLL_MS = 10000; // event JSON refresh cadence

function fmtCountdown(secs) {
  const s = Math.max(0, Math.round(secs));
  const m = Math.floor(s / 60);
  return `${m}:${String(s % 60).padStart(2, "0")}`;
}

export default function RiftStormBanner() {
  const [data, setData] = useState(null);
  // local countdown so the "opens in" timer ticks smoothly between polls
  const [countdown, setCountdown] = useState(0);

  // poll the event feed
  useEffect(() => {
    let alive = true;
    async function load() {
      try {
        const res = await fetch("/api/rift", { cache: "no-store" });
        if (!res.ok) return;
        const json = await res.json();
        if (!alive) return;
        setData(json);
        if (json?.status === "detecting") setCountdown(json.remainingSeconds || 0);
      } catch {
        /* network blip — keep last state */
      }
    }
    load();
    const id = setInterval(load, POLL_MS);
    return () => {
      alive = false;
      clearInterval(id);
    };
  }, []);

  // tick the local countdown each second while detecting
  useEffect(() => {
    if (data?.status !== "detecting") return;
    const id = setInterval(() => setCountdown((c) => Math.max(0, c - 1)), 1000);
    return () => clearInterval(id);
  }, [data?.status]);

  const status = data?.status;
  const live = status && status !== "idle" && !data?.stale;
  if (!live) return null;

  const raged = data?.boss?.raged;
  const label =
    status === "detecting" ? "RIFT STORM DETECTED"
    : status === "boss" ? (raged ? "RIFT OVERLORD — RAGE" : "RIFT OVERLORD")
    : status === "victory" ? "RIFT STORM CLEARED"
    : "RIFT STORM ACTIVE";

  // the single most relevant live stat for the current phase
  let stat = null;
  if (status === "detecting") {
    stat = `Opens in ${fmtCountdown(countdown)}`;
  } else if (status === "boss") {
    stat = `${data.boss?.hpPercent ?? 0}% HP`;
  } else if (status === "victory") {
    stat = "Rift Crate dropped";
  } else if (data?.phase === "Objectives") {
    stat = `Stability ${data.stability ?? 0}% · ${data.crystals?.alive ?? 0}/${data.crystals?.total ?? 0} crystals`;
  } else if (data?.phase === "Waves") {
    stat = `Wave ${(data.wave?.index ?? 0) + 1}/${data.wave?.count ?? 0}`;
  } else if (data?.phase) {
    stat = data.phase;
  }

  return (
    <div className={`rift-banner${raged ? " raged" : ""}`} role="status" aria-live="polite">
      <div className="rift-banner-inner glass">
        <span className="rift-banner-tag">
          <span className="rift-dot" />
          <Icon.bolt style={{ width: 16, height: 16 }} />
          {label}
        </span>

        <span className="rift-banner-loc">{data.location || "Unknown site"}</span>

        {stat && <span className="rift-banner-stat">{stat}</span>}

        {typeof data.participants === "number" && (
          <span className="rift-banner-players">
            <Icon.users style={{ width: 15, height: 15 }} />
            {data.participants}
          </span>
        )}
      </div>
    </div>
  );
}
