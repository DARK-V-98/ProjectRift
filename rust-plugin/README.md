# Project Rift Core — Carbon plugin

A standalone Rust plugin (separate from the website) that connects your Carbon
server to the Project Rift website.

It does **four** things:

1. **Live heartbeat** — every 30s it POSTs the real player count / max / hostname
   to `https://projectrift.esystemlk.com/api/server`, so the website and the
   `/loading` screen show live data.
2. **Native loading screen** — sets the Rust loading-screen header image and the
   server's website URL.
3. **Modern in-game UI (glassmorphism)**:
   - **HUD** — an always-on frosted-glass card (top-right) with the live online
     count and the wipe countdown, auto-refreshing every minute.
   - **Welcome screen** — a cinematic full-screen intro on connect (logo, title,
     tagline, live count, rotating tip). It stays until the player clicks
     **ENTER THE RIFT**, which plays a loading sequence with a **real spinning
     logo** (pre-rendered rotation frames via ImageLibrary) + progress bar
     before dropping them into the game. Without ImageLibrary it falls back to a
     rotating loader ring.
   - **/info panel** — a clean modal with status/team-limit/wipe stats, rules,
     commands and the Discord link (click outside or CLOSE to dismiss).
   - **Custom death screen** — on death: big "YOU DIED", the killer (or cause),
     weapon, distance, headshot/body, time survived, a list of your **sleeping
     bags + beds** (with per-bag cooldowns) to respawn at, and a RANDOM RESPAWN
     button. Handles PvP, NPCs/animals, and environment deaths (fall, cold…).
4. **Commands**: `/info` `/discord` `/website` `/loading`, plus admin `/rift`
   (force a heartbeat + refresh all HUDs).

The UI uses Rust's blur material (`uibackgroundblur.mat`) for a real frosted-glass
look, with the Project Rift purple/cyan accent palette.

## ⚠️ Important reality check

Rust **cannot** open a live webpage (HTML / video / JS) inside its native
loading screen — the game has no embedded browser there. So:

- The **web** loading screen at `…/loading` is for browsers, stream/OBS
  overlays, a second monitor, or your server's website link.
- The **in-game** "loading screen" is the CUI overlay this plugin draws (images
  + text only — no video/audio/HTML).

Both are powered by the same live data.

## Install

1. Copy `ProjectRiftCore.cs` into your server's **`carbon/plugins/`** folder
   (works in `oxide/plugins/` too — it's an Oxide-compatible plugin).
2. Carbon hot-loads it. You'll see `Project Rift Core loaded` in the console.
3. Edit the generated config at **`carbon/configs/ProjectRiftCore.json`**:

   ```json
   {
     "Website API URL (heartbeat is POSTed here)": "https://projectrift.esystemlk.com/api/server",
     "API Key (must match PROJECT_RIFT_API_KEY on the website)": "PUT-A-LONG-RANDOM-SECRET-HERE",
     "Website URL": "https://projectrift.esystemlk.com",
     "Discord invite URL": "https://discord.gg/yourinvite",
     "Loading-screen header image URL (shown by Rust)": "https://projectrift.esystemlk.com/bg.png",
     "In-game welcome overlay background image URL": "https://projectrift.esystemlk.com/bgmobile.png",
     "Heartbeat interval (seconds)": 30.0,
     "Show in-game welcome overlay on connect": true,
     "Welcome overlay auto-close (seconds, 0 = manual)": 12.0
   }
   ```

4. Reload: `c.reload ProjectRiftCore` (Carbon) or `o.reload ProjectRiftCore` (Oxide).

## Website setup (the other repo)

In the website's `.env.local`, set the **same** secret so heartbeats are accepted:

```
PROJECT_RIFT_API_KEY=PUT-A-LONG-RANDOM-SECRET-HERE
RUST_SERVER_NAME=PROJECT RIFT | RUST
RUST_SERVER_LOCATION=Singapore
RUST_SERVER_REGION=Asia
NEXT_PUBLIC_DISCORD_URL=https://discord.gg/yourinvite
NEXT_PUBLIC_SITE_URL=https://projectrift.esystemlk.com
# optional explicit wipe date; otherwise the API auto-computes the next Thursday
WIPE_NEXT=2026-07-03T18:00:00Z
```

The website API (`/api/server`) prefers a **fresh plugin heartbeat**, then falls
back to a direct **GameDig** query (`RUST_SERVER_HOST` / `RUST_SERVER_PORT`),
then to static demo data — so the site always shows something sensible.

## Data flow

```
Rust server (Carbon)
   │  POST /api/server  { players, maxPlayers, hostname }   (x-api-key)
   ▼
Website API  /api/server
   ▲  GET /api/server  { online, players, maxPlayers, wipeNext, ... }
   │
Website  /loading  +  homepage live stats
```

## Notes / upgrades

- The in-game heartbeat store on the website is **in-memory** (resets on
  redeploy). For multi-instance/serverless persistence, swap it for Redis or
  Firebase Admin in `app/api/server/route.js`.
- `CuiRawImageComponent { Url = ... }` downloads the image on the client and may
  flicker on first load. For instant images, pre-cache with the **ImageLibrary**
  plugin and reference by name instead.
- **Spinning logo (ENTER THE RIFT):** requires the **ImageLibrary** plugin. On
  load this plugin queues 24 rotation frames (`/spin/frame0.png` … `frame23.png`,
  shipped in the website's `public/spin/`) and cycles them for a real spinning
  logo. If ImageLibrary isn't installed, it automatically falls back to a
  rotating loader ring (no setup needed). Config: `Spinning logo frames base URL`
  and `Spinning logo frame count`.
- Tested target: **Carbon** (current Rust). Being Oxide-compatible it should also
  load on uMod/Oxide, but verify on your server before going live.
```
