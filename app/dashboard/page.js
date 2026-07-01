"use client";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import Image from "next/image";
import { signOut } from "firebase/auth";
import { auth } from "@/lib/firebase";
import { useAuth } from "@/lib/useAuth";
import { SITE } from "@/lib/site";
import { statusMeta } from "@/lib/whitelistStatus";
import { Icon } from "@/components/Icons";

export default function Dashboard() {
  const router = useRouter();
  const { user, loading } = useAuth();
  const [apps, setApps] = useState(null); // whitelist applications

  useEffect(() => {
    if (!loading && !user) router.replace("/login");
  }, [user, loading, router]);

  // load this user's whitelist applications
  useEffect(() => {
    if (!user) return;
    (async () => {
      try {
        const token = await user.getIdToken();
        const r = await fetch("/api/whitelist", {
          headers: { Authorization: `Bearer ${token}` },
        });
        const j = await r.json();
        setApps(j.ok ? j.applications || [] : []);
      } catch {
        setApps([]);
      }
    })();
  }, [user]);

  if (loading || !user) {
    return (
      <main className="admin-wrap">
        <div className="dash-loading">Loading your dashboard…</div>
      </main>
    );
  }

  const logout = async () => {
    if (auth) await signOut(auth);
    router.replace("/");
  };

  const latest = apps && apps.length ? apps[0] : null;
  const latestMeta = latest ? statusMeta(latest.status) : null;

  return (
    <main className="dash-wrap">
      <div className="container dash-inner">
        <header className="dash-head glass">
          <div className="dash-id">
            <Image src="/logo.png" alt="" width={52} height={52} style={{ borderRadius: "50%" }} />
            <div>
              <span className="eyebrow">Player Dashboard</span>
              <h1>{user.displayName || user.email.split("@")[0]}</h1>
              <p className="dash-email">{user.email}</p>
            </div>
          </div>
          <button className="btn btn-ghost" onClick={logout}>
            Sign out
          </button>
        </header>

        {/* whitelist status strip */}
        <section className="dash-status glass">
          <div style={{ flex: 1, minWidth: 240 }}>
            <span className="eyebrow">Whitelist status</span>
            {apps === null ? (
              <h2 className="dash-status-val">Checking…</h2>
            ) : latest ? (
              <>
                <h2 className="dash-status-val" style={{ color: latestMeta.color }}>
                  {latestMeta.short}
                  <span className="dash-status-type"> · {latest.type === "streamer" ? "Streamer" : "Player"}</span>
                </h2>
                <p className="dash-status-desc">{latestMeta.desc}</p>
                {latest.staffNote && (
                  <p className="dash-status-note">Staff note: “{latest.staffNote}”</p>
                )}
              </>
            ) : (
              <h2 className="dash-status-val" style={{ color: "#9aa3c4" }}>NOT APPLIED</h2>
            )}
          </div>
          <Link className="btn btn-primary" href="/dashboard/whitelist">
            {latest
              ? latest.status === "resubmit" || latest.status === "rejected"
                ? "Re-fill application"
                : "View / Re-apply"
              : "Apply for whitelist"}
          </Link>
        </section>

        {/* menu */}
        <div className="dash-grid">
          <Link href="/dashboard/whitelist" className="dash-card glass">
            <span className="dash-card-ic">🛡️</span>
            <h3>Rust Server Whitelist</h3>
            <p>Apply as a Player or Streamer to get whitelisted on the server.</p>
          </Link>

          <a href={SITE.connectUrl} className="dash-card glass">
            <span className="dash-card-ic">🎮</span>
            <h3>Connect to Server</h3>
            <p>{SITE.serverIp}</p>
          </a>

          <a href={SITE.discordUrl} target="_blank" rel="noreferrer" className="dash-card glass">
            <span className="dash-card-ic">💬</span>
            <h3>Discord Community</h3>
            <p>Join announcements, events and support.</p>
          </a>

          <Link href="/#servers" className="dash-card glass">
            <span className="dash-card-ic">📊</span>
            <h3>Server Status</h3>
            <p>Live players, wipe schedule and Rift Storm events.</p>
          </Link>
        </div>

        {/* application history */}
        {apps && apps.length > 0 && (
          <section className="dash-apps glass">
            <span className="eyebrow">Your applications</span>
            <ul>
              {apps.map((a) => (
                <li key={a.id}>
                  <span className="dash-app-type">{a.type === "streamer" ? "Streamer" : "Player"}</span>
                  <span className="dash-app-date">
                    {a.createdAt ? new Date(a.createdAt).toLocaleDateString() : ""}
                  </span>
                  <span className="dash-app-status" style={{ color: statusMeta(a.status).color }}>
                    {statusMeta(a.status).short}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </main>
  );
}
