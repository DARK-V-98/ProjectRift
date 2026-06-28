import Image from "next/image";
import Navbar from "@/components/Navbar";
import Reveal from "@/components/Reveal";
import BlastDoors from "@/components/BlastDoors";
import Particles from "@/components/Particles";
import { Icon } from "@/components/Icons";
import { getStats, getServers } from "@/lib/data";

export const revalidate = 60; // refresh server/player data every minute

const features = [
  { icon: Icon.bolt, title: "High Performance", desc: "Bare-metal nodes with NVMe storage and sub-20ms tick rates." },
  { icon: Icon.shield, title: "Active Admins", desc: "24/7 staff coverage with instant anti-cheat enforcement." },
  { icon: Icon.scale, title: "Fair Play", desc: "Strict whitelist, wipe schedules and zero pay-to-win." },
  { icon: Icon.cube, title: "Custom Content", desc: "Exclusive maps, plugins and roleplay frameworks." },
  { icon: Icon.users, title: "Strong Community", desc: "Thousands of active members and weekly events." },
];

export default async function Home() {
  const [stats, servers] = await Promise.all([getStats(), getServers()]);

  const statCards = [
    { icon: Icon.server, num: stats.serversOnline, label: "Servers Online" },
    { icon: Icon.players, num: stats.playersOnline, label: "Players Online" },
    { icon: Icon.chat, num: stats.discordMembers, label: "Discord Members" },
    { icon: Icon.pulse, num: stats.uptime, label: "Uptime" },
  ];

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
            className="hero-img"
          />
          <div className="hero-art-top" />
          <div className="hero-art-fade" />
          <Particles count={20} />
        </div>

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
      </header>

      {/* ============ STATS ============ */}
      <section className="stats">
        <div className="container stats-grid">
          {statCards.map((s, i) => (
            <div
              key={s.label}
              className="glass stat-card reveal"
              style={{ transitionDelay: `${i * 0.08}s` }}
            >
              <span className="icon">
                <s.icon style={{ width: 24, height: 24 }} />
              </span>
              <div className="stat-num">{s.num}</div>
              <div className="stat-label">{s.label}</div>
            </div>
          ))}
        </div>
      </section>

      {/* ============ SERVERS ============ */}
      <section className="section" id="servers">
        <div className="container">
          <div className="section-head reveal">
            <span className="eyebrow">Our Network</span>
            <h2 className="section-title">
              Choose Your <span className="accent">Battlefield</span>
            </h2>
            <p>
              Hand-tuned, high-performance servers across the most demanding
              survival and roleplay titles in gaming.
            </p>
          </div>

          <div className="servers-grid">
            {servers.map((srv, i) => {
              const live = srv.status === "LIVE";
              const pct = srv.maxPlayers
                ? Math.round((srv.players / srv.maxPlayers) * 100)
                : 0;
              return (
                <article
                  key={srv.id}
                  className="server-card reveal"
                  style={{ transitionDelay: `${i * 0.08}s` }}
                >
                  <div className="server-art" style={{ background: srv.art }} />
                  <div className="server-pattern" />
                  <div className="server-top">
                    <span className="server-tag">{srv.tag}</span>
                    <span className={`badge ${live ? "badge-live" : "badge-soon"}`}>
                      {live && <span className="dot" />}
                      {srv.status}
                    </span>
                  </div>
                  <h3 className="server-name">{srv.name}</h3>
                  {live ? (
                    <>
                      <div className="server-players">
                        <Icon.players style={{ width: 16, height: 16 }} />
                        <b>{srv.players}</b> / {srv.maxPlayers} players
                      </div>
                      <div className="player-bar">
                        <i style={{ width: `${pct}%` }} />
                      </div>
                      <a className="btn btn-primary server-join" href="#">
                        Join Now
                      </a>
                    </>
                  ) : (
                    <>
                      <div className="server-players">Launching this season</div>
                      <div className="player-bar">
                        <i style={{ width: "0%" }} />
                      </div>
                      <button className="btn server-join disabled" disabled>
                        Coming Soon
                      </button>
                    </>
                  )}
                </article>
              );
            })}
          </div>
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
            <div className="glass partner-card reveal">
              <div className="partner-logo">E</div>
              <div>
                <span className="role">Powered By</span>
                <h3>ESYSTEMLK</h3>
                <p>
                  Providing powerful infrastructure and financial support to keep
                  the network blazing fast and always online.
                </p>
              </div>
            </div>
            <div
              className="glass partner-card reveal"
              style={{ transitionDelay: "0.1s" }}
            >
              <div className="partner-logo">9Z</div>
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

      {/* ============ FOOTER ============ */}
      <footer className="footer" id="whitelist">
        <div className="container">
          <div className="footer-grid">
            <div className="footer-brand">
              <a href="#home" className="logo">
                <span className="logo-mark">
                  <Icon.rift />
                </span>
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
              Powered by ESYSTEMLK · Managed by TEAM 9Z
            </span>
          </div>
        </div>
      </footer>
    </>
  );
}
