"use client";
import { useMemo } from "react";

// Deterministic pseudo-random so server and client markup match (no hydration warning).
function seeded(i) {
  const x = Math.sin(i * 99.13) * 10000;
  return x - Math.floor(x);
}

export default function Particles({ count = 26 }) {
  const dots = useMemo(
    () =>
      Array.from({ length: count }, (_, i) => {
        const a = seeded(i);
        const b = seeded(i + 100);
        const c = seeded(i + 200);
        return {
          left: `${(a * 100).toFixed(2)}%`,
          bottom: `${(b * 60).toFixed(2)}%`,
          duration: `${(7 + c * 9).toFixed(2)}s`,
          delay: `${(a * 8).toFixed(2)}s`,
          size: 2 + Math.round(b * 3),
          cyan: c > 0.5,
        };
      }),
    [count]
  );

  return (
    <div className="particles" aria-hidden="true">
      {dots.map((d, i) => (
        <span
          key={i}
          className="particle"
          style={{
            left: d.left,
            bottom: d.bottom,
            width: d.size,
            height: d.size,
            animationDuration: d.duration,
            animationDelay: d.delay,
            background: d.cyan ? "var(--cyan)" : "var(--violet)",
            boxShadow: `0 0 10px ${d.cyan ? "var(--cyan)" : "var(--violet)"}`,
          }}
        />
      ))}
    </div>
  );
}
