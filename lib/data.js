// Live data layer for PROJECT RIFT.
// Reads from Firebase Firestore when configured; otherwise falls back to
// curated demo data so the site renders perfectly out of the box.
import { db } from "./firebase";
import { collection, getDocs, doc, getDoc } from "firebase/firestore";

export const fallbackStats = {
  serversOnline: 5,
  playersOnline: 152,
  discordMembers: "4.8K",
  uptime: "99.9%",
};

export const fallbackServers = [
  {
    id: "rust",
    name: "RUST",
    tag: "SURVIVAL",
    status: "LIVE",
    players: 198,
    maxPlayers: 250,
    accent: "#B026FF",
    art: "linear-gradient(135deg, #2a0a3d 0%, #5a1378 60%, #b026ff 140%)",
  },
];


export async function getStats() {
  if (!db) return fallbackStats;
  try {
    const snap = await getDoc(doc(db, "meta", "stats"));
    return snap.exists() ? { ...fallbackStats, ...snap.data() } : fallbackStats;
  } catch {
    return fallbackStats;
  }
}

import { GameDig } from 'gamedig';

export async function getLiveRustStatus() {
  const host = process.env.RUST_SERVER_HOST;
  const port = process.env.RUST_SERVER_PORT;

  if (!host || !port) {
    return null;
  }

  try {
    const state = await GameDig.query({
      type: 'rust',
      host: host,
      port: parseInt(port, 10),
      maxAttempts: 2,
      socketTimeout: 2000
    });

    return {
      status: "LIVE",
      name: state.name,
      players: state.numplayers,
      maxPlayers: state.maxplayers,
    };
  } catch (error) {
    console.error("GameDig query failed:", error);
    return { status: "OFFLINE" };
  }
}

export async function getServers() {
  let servers = [...fallbackServers];

  if (db) {
    try {
      const snap = await getDocs(collection(db, "servers"));
      if (!snap.empty) {
        servers = snap.docs.map((d) => ({ id: d.id, ...d.data() }));
      }
    } catch {
      // ignore
    }
  }

  // Merge live Rust server data
  const liveStatus = await getLiveRustStatus();
  if (liveStatus) {
    const rustServer = servers.find(s => s.id === 'rust');
    if (rustServer) {
      if (liveStatus.status === "LIVE") {
        rustServer.players = liveStatus.players;
        rustServer.maxPlayers = liveStatus.maxPlayers;
        rustServer.status = "LIVE";
        // Optional: you could overwrite rustServer.name with liveStatus.name
      } else {
        rustServer.status = "OFFLINE";
        rustServer.players = 0;
      }
    }
  }

  return servers;
}
