"use client";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/useAuth";
import { WL_STATUS, ADMIN_STATUSES, statusMeta } from "@/lib/whitelistStatus";
import { Icon } from "@/components/Icons";

const ACTIONS = [
  { status: "approved", label: "Confirm" },
  { status: "rejected", label: "Reject" },
  { status: "resubmit", label: "Re-fill" },
  { status: "suspended", label: "Suspend" },
  { status: "out_of_region", label: "Out of Region" },
];

export default function AdminWhitelist() {
  const router = useRouter();
  const { user, loading } = useAuth();
  const [apps, setApps] = useState(null);
  const [denied, setDenied] = useState(false);
  const [filter, setFilter] = useState("all");
  const [openId, setOpenId] = useState(null);
  const [notes, setNotes] = useState({});
  const [busyId, setBusyId] = useState(null);
  const [toast, setToast] = useState(null);

  useEffect(() => {
    if (!loading && !user) router.replace("/login");
  }, [user, loading, router]);

  const load = useCallback(async () => {
    if (!user) return;
    const token = await user.getIdToken();
    const r = await fetch("/api/whitelist?scope=all", {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (r.status === 403) { setDenied(true); setApps([]); return; }
    const j = await r.json();
    setApps(j.ok ? j.applications || [] : []);
  }, [user]);

  useEffect(() => { load(); }, [load]);

  const setStatus = async (id, status) => {
    if (!user) return;
    setBusyId(id);
    setToast(null);
    try {
      const token = await user.getIdToken();
      const r = await fetch("/api/whitelist", {
        method: "PATCH",
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
        body: JSON.stringify({ id, status, note: notes[id] || "" }),
      });
      const j = await r.json();
      if (r.ok && j.ok) {
        setApps((prev) =>
          prev.map((a) =>
            a.id === id ? { ...a, status, staffNote: notes[id] || "", reviewedAt: Date.now() } : a
          )
        );
        setToast({ ok: true, text: `Marked as ${statusMeta(status).label}.` });
      } else {
        setToast({ ok: false, text: j.error || "Update failed." });
      }
    } catch (e) {
      setToast({ ok: false, text: String(e) });
    } finally {
      setBusyId(null);
    }
  };

  const counts = useMemo(() => {
    const c = { all: apps?.length || 0 };
    (apps || []).forEach((a) => { c[a.status] = (c[a.status] || 0) + 1; });
    return c;
  }, [apps]);

  const shown = useMemo(
    () => (apps || []).filter((a) => filter === "all" || a.status === filter),
    [apps, filter]
  );

  if (loading || !user || apps === null) {
    return <main className="admin-wrap"><div className="dash-loading">Loading review queue…</div></main>;
  }

  if (denied) {
    return (
      <main className="admin-wrap">
        <div className="admin-card glass" style={{ textAlign: "center" }}>
          <span className="eyebrow" style={{ justifyContent: "center" }}>Admin</span>
          <h1 style={{ fontFamily: "var(--font-display)", margin: "8px 0" }}>Access denied</h1>
          <p style={{ color: "var(--muted)" }}>
            Your account ({user.email}) is not an admin. Add it to
            <code> RIFT_ADMIN_EMAILS</code> in the environment, or set the
            <code> admin</code> custom claim.
          </p>
          <Link href="/dashboard" className="btn btn-ghost" style={{ marginTop: 16 }}>← Dashboard</Link>
        </div>
      </main>
    );
  }

  return (
    <main className="dash-wrap">
      <div className="container wl-inner" style={{ maxWidth: 1040 }}>
        <div className="wl-top">
          <Link href="/dashboard" className="wl-back">← Dashboard</Link>
          <span className="wl-badge">WHITELIST · ADMIN REVIEW</span>
        </div>

        <div className="admin-head" style={{ marginBottom: 14 }}>
          <h1 style={{ fontFamily: "var(--font-display)", fontWeight: 800, fontSize: "1.8rem" }}>
            Review <span className="accent">Applications</span>
          </h1>
          <p style={{ color: "var(--muted)" }}>{counts.all} total · click a row to view the full application.</p>
        </div>

        {/* filters */}
        <div className="wl-filters">
          {["all", ...ADMIN_STATUSES].map((k) => (
            <button
              key={k}
              className={filter === k ? "active" : ""}
              onClick={() => setFilter(k)}
            >
              {k === "all" ? "All" : statusMeta(k).label}
              <span className="wl-filter-n">{counts[k] || 0}</span>
            </button>
          ))}
        </div>

        {toast && <div className={`admin-status ${toast.ok ? "ok" : "err"}`} style={{ marginBottom: 14 }}>{toast.text}</div>}

        {shown.length === 0 && <div className="glass" style={{ padding: 28, textAlign: "center", color: "var(--muted)" }}>No applications in this view.</div>}

        <div className="wl-list">
          {shown.map((a) => {
            const meta = statusMeta(a.status);
            const open = openId === a.id;
            const d = a.data || {};
            return (
              <div key={a.id} className="wl-row glass">
                <button className="wl-row-head" onClick={() => setOpenId(open ? null : a.id)}>
                  <span className="wl-row-type">
                    {a.type === "streamer"
                      ? <><Icon.video style={{ width: 16, height: 16 }} /> Streamer</>
                      : <><Icon.user style={{ width: 16, height: 16 }} /> Player</>}
                  </span>
                  <span className="wl-row-name">{d.fullName || a.email || a.uid}</span>
                  <span className="wl-row-date">{a.createdAt ? new Date(a.createdAt).toLocaleDateString() : ""}</span>
                  <span className="wl-row-status" style={{ color: meta.color, borderColor: meta.color }}>{meta.short}</span>
                </button>

                {open && (
                  <div className="wl-row-body">
                    <dl className="wl-detail">
                      {Object.entries(d).map(([k, v]) => (
                        <div key={k}>
                          <dt>{k}</dt>
                          <dd>{String(v) || "—"}</dd>
                        </div>
                      ))}
                    </dl>

                    {a.staffNote && (
                      <p className="wl-prevnote">Last staff note: “{a.staffNote}”{a.reviewedBy ? ` — ${a.reviewedBy}` : ""}</p>
                    )}

                    <label className="wl-note-label">
                      Staff note (shown to applicant — explain the decision, e.g. region/ping)
                      <textarea
                        rows={2}
                        value={notes[a.id] ?? a.staffNote ?? ""}
                        onChange={(e) => setNotes((n) => ({ ...n, [a.id]: e.target.value }))}
                        placeholder="e.g. You're outside the Singapore region — ping too high to whitelist."
                      />
                    </label>

                    <div className="wl-actions">
                      {ACTIONS.map((act) => (
                        <button
                          key={act.status}
                          disabled={busyId === a.id}
                          className="wl-act"
                          style={{ "--c": WL_STATUS[act.status].color }}
                          onClick={() => setStatus(a.id, act.status)}
                        >
                          {act.label}
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </main>
  );
}
