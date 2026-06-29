"use client";
import { useEffect, useState } from "react";

// Minimal admin notification sender. Protected by the API key for now;
// will be replaced by Firebase-auth admin dashboard once wired.
export default function AdminNotify() {
  const [key, setKey] = useState("");
  const [title, setTitle] = useState("");
  const [message, setMessage] = useState("");
  const [type, setType] = useState("info");
  const [duration, setDuration] = useState(6);
  const [status, setStatus] = useState(null);
  const [sending, setSending] = useState(false);

  useEffect(() => {
    setKey(localStorage.getItem("rift_admin_key") || "");
  }, []);

  const send = async (e) => {
    e.preventDefault();
    if (!message.trim()) return;
    setSending(true);
    setStatus(null);
    localStorage.setItem("rift_admin_key", key);
    try {
      const r = await fetch("/api/notifications", {
        method: "POST",
        headers: { "Content-Type": "application/json", "x-api-key": key },
        body: JSON.stringify({ title, message, type, durationSec: Number(duration) }),
      });
      const j = await r.json();
      if (r.ok && j.ok) {
        setStatus({ ok: true, text: `Sent #${j.notification.id} — live in-game` });
        setMessage("");
        setTitle("");
      } else {
        setStatus({ ok: false, text: j.error || `HTTP ${r.status}` });
      }
    } catch (err) {
      setStatus({ ok: false, text: String(err) });
    } finally {
      setSending(false);
    }
  };

  const colors = { info: "#00e5ff", success: "#2bff88", warning: "#ffb020", alert: "#ff4060" };

  return (
    <main className="admin-wrap">
      <div className="admin-card glass">
        <div className="admin-head">
          <span className="eyebrow">Admin · Notifications</span>
          <h1>
            Broadcast to <span className="accent">in-game</span>
          </h1>
          <p>Sends a header notification to every player on the server right now.</p>
        </div>

        <form onSubmit={send} className="admin-form">
          <label>
            API key
            <input
              type="password"
              value={key}
              onChange={(e) => setKey(e.target.value)}
              placeholder="PROJECT_RIFT_API_KEY"
              autoComplete="off"
            />
          </label>

          <label>
            Title (optional)
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="e.g. EVENT"
              maxLength={60}
            />
          </label>

          <label>
            Message
            <textarea
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Zombie raid starts in 10 minutes!"
              maxLength={240}
              rows={3}
            />
          </label>

          <div className="admin-row">
            <label>
              Type
              <select value={type} onChange={(e) => setType(e.target.value)}>
                <option value="info">Info</option>
                <option value="success">Success</option>
                <option value="warning">Warning</option>
                <option value="alert">Alert</option>
              </select>
            </label>
            <label>
              Duration (s)
              <input
                type="number"
                min={2}
                max={30}
                value={duration}
                onChange={(e) => setDuration(e.target.value)}
              />
            </label>
          </div>

          {/* live preview of the in-game pill */}
          <div className="admin-preview" style={{ borderColor: colors[type] }}>
            <span className="admin-preview-dot" style={{ background: colors[type] }} />
            <b style={{ color: colors[type] }}>{title || "PROJECT RIFT"}</b>
            <span>{message || "Your notification preview…"}</span>
          </div>

          <button className="btn btn-primary" disabled={sending || !message.trim()}>
            {sending ? "Sending…" : "Send Notification"}
          </button>

          {status && (
            <div className={`admin-status ${status.ok ? "ok" : "err"}`}>{status.text}</div>
          )}
        </form>
      </div>
    </main>
  );
}
