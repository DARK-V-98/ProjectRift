"use client";
import { useEffect, useState } from "react";
import { Icon } from "./Icons";

const links = [
  ["Home", "#home"],
  ["Servers", "#servers"],
  ["Whitelist", "#whitelist"],
  ["Reports", "#reports"],
  ["About", "#about"],
  ["Team", "#team"],
];

export default function Navbar() {
  const [scrolled, setScrolled] = useState(false);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 24);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <nav className={`nav ${scrolled ? "scrolled" : ""}`}>
      <div className="container nav-inner">
        <a href="#home" className="logo">
          <span className="logo-mark">
            <Icon.rift />
          </span>
          <b>
            PROJECT <span>RIFT</span>
          </b>
        </a>

        <ul className="nav-links">
          {links.map(([label, href]) => (
            <li key={label}>
              <a href={href}>{label}</a>
            </li>
          ))}
        </ul>

        <div className="nav-right">
          <a
            className="btn btn-discord"
            href="https://discord.gg"
            target="_blank"
            rel="noreferrer"
          >
            <Icon.discord style={{ width: 18, height: 18 }} />
            Discord
          </a>
          <button
            className="nav-toggle"
            aria-label="Toggle menu"
            onClick={() => setOpen((v) => !v)}
          >
            {open ? <Icon.x style={{ width: 22, height: 22 }} /> : <Icon.menu style={{ width: 24, height: 24 }} />}
          </button>
        </div>
      </div>

      {open && (
        <div
          className="glass"
          style={{
            margin: "0 16px",
            padding: 14,
            display: "flex",
            flexDirection: "column",
            gap: 4,
          }}
        >
          {links.map(([label, href]) => (
            <a
              key={label}
              href={href}
              onClick={() => setOpen(false)}
              style={{
                padding: "12px 14px",
                fontFamily: "var(--font-ui)",
                textTransform: "uppercase",
                letterSpacing: "0.08em",
                color: "var(--muted)",
              }}
            >
              {label}
            </a>
          ))}
        </div>
      )}
    </nav>
  );
}
