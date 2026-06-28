"use client";
import { useEffect, useState } from "react";
import Image from "next/image";

// Cinematic intro: two sci-fi blast doors that slide apart on first load,
// revealing the hero. Respects prefers-reduced-motion (skips straight to open).
export default function BlastDoors() {
  // phases: "closed" -> "opening" -> "done"
  const [phase, setPhase] = useState("closed");

  useEffect(() => {
    const reduce =
      typeof window !== "undefined" &&
      window.matchMedia &&
      window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    if (reduce) {
      setPhase("done");
      return;
    }

    document.body.style.overflow = "hidden"; // lock scroll during intro
    const t1 = setTimeout(() => setPhase("opening"), 650);
    const t2 = setTimeout(() => {
      setPhase("done");
      document.body.style.overflow = "";
    }, 2600);

    return () => {
      clearTimeout(t1);
      clearTimeout(t2);
      document.body.style.overflow = "";
    };
  }, []);

  if (phase === "done") return null;

  return (
    <div className={`blast ${phase}`} aria-hidden="true">
      <div className="blast-door left">
        <div className="blast-stripes" />
        <div className="blast-edge" />
      </div>
      <div className="blast-door right">
        <div className="blast-stripes" />
        <div className="blast-edge" />
      </div>

      <div className="blast-center">
        <span className="blast-logo">
          <span className="blast-logo-ring" />
          <Image
            src="/logo.png"
            alt="Project Rift logo"
            width={132}
            height={132}
            className="blast-logo-img"
            priority
          />
        </span>
        <div className="blast-loadbar">
          <i />
        </div>
        <div className="blast-status">INITIALIZING RIFT…</div>
      </div>

      <div className="blast-seam" />
    </div>
  );
}
