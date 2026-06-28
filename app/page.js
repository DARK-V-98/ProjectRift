import Image from "next/image";
import Navbar from "@/components/Navbar";
import Reveal from "@/components/Reveal";
import BlastDoors from "@/components/BlastDoors";
import Particles from "@/components/Particles";
import { Icon } from "@/components/Icons";
import { getServers } from "@/lib/data";

export const revalidate = 60; // refresh server/player data every minute

const features = [
  { icon: Icon.bolt, title: "High Performance", desc: "Bare-metal nodes with NVMe storage and sub-20ms tick rates." },
  { icon: Icon.shield, title: "Active Admins", desc: "24/7 staff coverage with instant anti-cheat enforcement." },
  { icon: Icon.scale, title: "Fair Play", desc: "Strict whitelist, wipe schedules and zero pay-to-win." },
  { icon: Icon.cube, title: "Custom Content", desc: "Exclusive maps, plugins and roleplay frameworks." },
  { icon: Icon.users, title: "Strong Community", desc: "Thousands of active members and weekly events." },
];

export default async function Home() {
  const servers = await getServers();

  return (
    <>
      <BlastDoors />
      <Navbar />
      <Reveal />

      {/* ============ HERO ============ */}
      <header className="hero" id="home">
        <div className="hero-art">
          <Image
            src="/bg.png"
            alt="Project Rift — Rust. Survive. Build. Conquer."
            width={1536}
            height={1024}
            quality={95}
            priority
            sizes="100vw"
            className="hero-img hero-img-desktop"
          />
          <Image
            src="/bgmobile.png"
            alt="Project Rift — Rust. Survive. Build. Conquer."
            width={941}
            height={1090}
            quality={95}
            priority
            sizes="100vw"
            className="hero-img hero-img-mobile"
          />
          <div className="hero-art-top" />
          <div className="hero-art-fade" />
          <Particles count={20} />
        </div>

        {/* Desktop: floating CTA cluster over the full poster */}
        <div className="hero-cta-overlay">
          <span className="hero-badge floating">
            <span className="dot" /> Season 04 · Now Live
          </span>
          <div className="hero-cta-row">
            <a className="btn btn-primary" href="#servers">
              Join Server <Icon.arrow style={{ width: 18, height: 18 }} />
            </a>
            <a className="btn btn-ghost" href="#servers">
              View Servers
            </a>
          </div>
        </div>

        {/* Mobile: CTAs overlaid on the dedicated portrait poster */}
        <div className="hero-mobile">
          <span className="hero-badge">
            <span className="dot" /> Season 04 · Now Live
          </span>
          <div className="hero-cta-row">
            <a className="btn btn-primary" href="#servers">
              Join Server <Icon.arrow style={{ width: 18, height: 18 }} />
            </a>
            <a className="btn btn-ghost" href="#servers">
              View Servers
            </a>
          </div>
        </div>
      </header>

      {/* ============ SERVERS ============ */}
      <section className="section" id="servers">
        <div className="container">
          <div className="section-head reveal">
            <span className="eyebrow">Our Network</span>
            <h2 className="section-title">
              Choose Your <span className="accent">Battlefield</span>
            </h2>
            <p>
              One premium Rust server — bare-metal hardware, zero compromise.
            </p>
          </div>

          {servers.map((srv) => {
            const live = srv.status === "LIVE";
            const pct = srv.maxPlayers
              ? Math.round((srv.players / srv.maxPlayers) * 100)
              : 0;
            return (
              <article key={srv.id} className="rust-showcase reveal">

                {/* ── Left: Art panel ── */}
                <div className="rust-art-panel">
                  {/* Actual game poster image */}
                  <Image
                    src="/rust.png"
                    alt={srv.name}
                    fill
                    sizes="(max-width: 900px) 100vw, 420px"
                    quality={95}
                    className="rust-art-bg-img"
                  />
                  {/* scan-line texture */}
                  <div className="rust-scanlines" />
                  {/* central glow orb */}
                  <div className="rust-orb" />
                  {/* bottom fade into info panel */}
                  <div className="rust-art-fade" />

                  {/* Top-left badges */}
                  <div className="rust-art-badges">
                    <span className="server-tag">{srv.tag}</span>
                    <span className={`badge ${live ? "badge-live" : "badge-soon"}`}>
                      {live && <span className="dot" />}
                      {srv.status}
                    </span>
                  </div>

                  {/* Centered game title */}
                  <div className="rust-art-title">
                    <h3>{srv.name}</h3>
                    <span className="rust-art-sub">Project Rift</span>
                  </div>
                </div>

                {/* ── Right: Info panel ── */}
                <div className="rust-info-panel">
                  {/* Accent beam */}
                  <div className="rust-beam" />

                  <span className="eyebrow">Now Playing</span>
                  <h2 className="rust-info-title">{srv.name}</h2>
                  <p className="rust-info-desc">
                    Bare-metal nodes, NVMe storage and sub-20ms tick rates —
                    engineered for competitive survival gameplay with zero pay-to-win.
                  </p>

                  {/* Player count + bar */}
                  <div className="rust-player-row">
                    <div className="rust-player-label">
                      <Icon.players style={{ width: 15, height: 15 }} />
                      <span>Players Online</span>
                    </div>
                    <span className="rust-player-count">
                      <b>{srv.players}</b>
                      <em>/ {srv.maxPlayers}</em>
                    </span>
                  </div>
                  <div className="rust-bar">
                    <div
                      className="rust-bar-fill"
                      style={{ width: `${pct}%` }}
                    />
                    <span className="rust-bar-pct">{pct}%</span>
                  </div>

                  {/* Stat chips */}
                  <div className="rust-stats">
                    <div className="rust-stat">
                      <Icon.bolt style={{ width: 16, height: 16 }} />
                      <span>Region</span>
                      <b>Asia Pacific</b>
                    </div>
                    <div className="rust-stat">
                      <Icon.shield style={{ width: 16, height: 16 }} />
                      <b>Active Admins</b>
                    </div>
                    <div className="rust-stat">
                      <Icon.scale style={{ width: 16, height: 16 }} />
                      <span>Wipe</span>
                      <b>Monthly</b>
                    </div>
                  </div>

                  {/* CTA */}
                  {live ? (
                    <a className="btn btn-primary rust-cta" href="steam://connect/51.79.218.241:20215">
                      Join Server <Icon.arrow style={{ width: 18, height: 18 }} />
                    </a>
                  ) : (
                    <button className="btn rust-cta disabled" disabled>
                      Coming Soon
                    </button>
                  )}

                  {/* Connection info */}
                  <div className="rust-connect">
                    <Icon.players style={{ width: 13, height: 13 }} />
                    <span>connect <code>play.projectrift.gg</code></span>
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      </section>

      {/* ============ FEATURES ============ */}
      <section className="section" id="about">
        <div className="container">
          <div className="section-head reveal">
            <span className="eyebrow">Why Project Rift</span>
            <h2 className="section-title">
              Built For <span className="accent">Domination</span>
            </h2>
            <p>Every detail engineered for competitive, lag-free gameplay.</p>
          </div>

          <div className="features-grid">
            {features.map((f, i) => (
              <div
                key={f.title}
                className="glass feature-card reveal"
                style={{ transitionDelay: `${i * 0.07}s` }}
              >
                <span className="feature-icon">
                  <f.icon style={{ width: 28, height: 28 }} />
                </span>
                <h3>{f.title}</h3>
                <p>{f.desc}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ============ COMMUNITY ============ */}
      <section className="section" id="reports">
        <div className="container">
          <div className="glass community reveal">
            <div className="community-avatars">
              {["R", "I", "F", "T", "+"].map((c) => (
                <span key={c}>{c}</span>
              ))}
            </div>
            <span className="eyebrow" style={{ justifyContent: "center" }}>
              Join The Movement
            </span>
            <h2>
              Become Part Of The <br />
              <span style={{ color: "var(--cyan)" }}>RIFT Community</span>
            </h2>
            <p>
              Connect with thousands of survivors, find squads, report issues
              and get instant support from our staff team on Discord.
            </p>
            <a
              className="btn btn-discord"
              href="https://discord.gg"
              target="_blank"
              rel="noreferrer"
            >
              <Icon.discord style={{ width: 20, height: 20 }} />
              Join Our Discord
            </a>
          </div>
        </div>
      </section>

      {/* ============ PARTNERS ============ */}
      <section className="section" id="team">
        <div className="container">
          <div className="section-head reveal">
            <span className="eyebrow">Trusted Partners</span>
            <h2 className="section-title">
              Powered By The <span className="accent">Best</span>
            </h2>
          </div>

          <div className="partners-grid">
            {/* ---- ESYSTEMLK card ---- */}
            <div className="glass partner-card reveal">
              {/* Blurred background images — desktop + mobile */}
              <div className="partner-card-bg" aria-hidden="true">
                <Image
                  src="/bg.png"
                  alt=""
                  fill
                  sizes="(max-width: 768px) 100vw, 50vw"
                  className="partner-bg-img partner-bg-desktop"
                />
                <Image
                  src="/bgmobile.png"
                  alt=""
                  fill
                  sizes="100vw"
                  className="partner-bg-img partner-bg-mobile"
                />
                <div className="partner-bg-overlay" />
              </div>
              {/* Logo */}
              <div className="partner-logo">
                <Image
                  src="/es.png"
                  alt="ESYSTEMLK logo"
                  width={70}
                  height={70}
                  className="partner-logo-img"
                />
              </div>
              <div>
                <span className="role">Powered By</span>
                <h3>
                  <a href="https://www.esystemlk.com" target="_blank" rel="noreferrer" className="partner-name-link">ESYSTEMLK</a>
                </h3>
                <p>
                  Providing powerful infrastructure and financial support to keep
                  the network blazing fast and always online.
                </p>
              </div>
            </div>

            {/* ---- TEAM 9Z card ---- */}
            <div
              className="glass partner-card reveal"
              style={{ transitionDelay: "0.1s" }}
            >
              {/* Blurred background images — desktop + mobile */}
              <div className="partner-card-bg" aria-hidden="true">
                <Image
                  src="/bg.png"
                  alt=""
                  fill
                  sizes="(max-width: 768px) 100vw, 50vw"
                  className="partner-bg-img partner-bg-desktop"
                />
                <Image
                  src="/bgmobile.png"
                  alt=""
                  fill
                  sizes="100vw"
                  className="partner-bg-img partner-bg-mobile"
                />
                <div className="partner-bg-overlay" />
              </div>
              {/* Logo */}
              <div className="partner-logo">
                <Image
                  src="/z9.png"
                  alt="Team 9Z logo"
                  width={70}
                  height={70}
                  className="partner-logo-img"
                />
              </div>
              <div>
                <span className="role">Managed By</span>
                <h3>TEAM 9Z</h3>
                <p>
                  Professional server management and player support, ensuring a
                  smooth, fair and welcoming experience for everyone.
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ============ CTA BANNER ============ */}
      <section className="cta-banner-section">
        <div className="container">
          <div className="cta-banner reveal">
            <Image
              src="/fsec.png"
              alt="Ready to enter the Rift? Join thousands of survivors and experience the ultimate Rust journey."
              width={1983}
              height={793}
              quality={95}
              sizes="(max-width: 1280px) 100vw, 1280px"
              className="cta-banner-img"
            />
            <div className="cta-banner-actions">
              <a className="btn btn-primary" href="#servers">
                Join Server <Icon.arrow style={{ width: 18, height: 18 }} />
              </a>
              <a
                className="btn btn-discord"
                href="https://discord.gg"
                target="_blank"
                rel="noreferrer"
              >
                <Icon.discord style={{ width: 20, height: 20 }} />
                Join Discord
              </a>
            </div>
          </div>
        </div>
      </section>

      {/* ============ FOOTER ============ */}
      <footer className="footer" id="whitelist">
        <div className="container">
          <div className="footer-grid">
            <div className="footer-brand">
              <a href="#home" className="logo">
                <Image
                  src="/logo.png"
                  alt="Project Rift logo"
                  width={44}
                  height={44}
                  className="logo-img"
                />
                <b>
                  PROJECT <span>RIFT</span>
                </b>
              </a>
              <p>
                A next-generation multiplayer gaming network. Survive, build,
                raid and dominate across premium survival and roleplay worlds.
              </p>
              <div className="socials">
                <a href="#" aria-label="Discord"><Icon.discord style={{ width: 18, height: 18 }} /></a>
                <a href="#" aria-label="X"><Icon.x style={{ width: 16, height: 16 }} /></a>
                <a href="#" aria-label="YouTube"><Icon.youtube style={{ width: 20, height: 20 }} /></a>
                <a href="#" aria-label="Twitch"><Icon.twitch style={{ width: 18, height: 18 }} /></a>
              </div>
            </div>

            <div className="footer-col">
              <h4>Network</h4>
              <a href="#servers">Servers</a>
              <a href="#whitelist">Whitelist</a>
              <a href="#reports">Reports</a>
              <a href="#about">Status</a>
            </div>
            <div className="footer-col">
              <h4>Community</h4>
              <a href="#">Discord</a>
              <a href="#">Events</a>
              <a href="#">Leaderboards</a>
              <a href="#">Store</a>
            </div>
            <div className="footer-col">
              <h4>Company</h4>
              <a href="#about">About</a>
              <a href="#team">Team</a>
              <a href="#">Rules</a>
              <a href="#">Privacy</a>
            </div>
          </div>

          <div className="footer-bottom">
            <span>© {new Date().getFullYear()} Project Rift. All rights reserved.</span>
            <span>
              Powered by{" "}
              <a href="https://www.esystemlk.com" target="_blank" rel="noreferrer" className="footer-partner-link">ESYSTEMLK</a>
              {" "}· Managed by TEAM 9Z
            </span>
          </div>
        </div>
      </footer>
    </>
  );
}
