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

export async function getServers() {
  if (!db) return fallbackServers;
  try {
    const snap = await getDocs(collection(db, "servers"));
    if (snap.empty) return fallbackServers;
    return snap.docs.map((d) => ({ id: d.id, ...d.data() }));
  } catch {
    return fallbackServers;
  }
}
