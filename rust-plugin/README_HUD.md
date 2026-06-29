# ProjectRiftHUD — Documentation & Installation Guide

A premium futuristic HUD plugin for Carbon (Rust), designed with the Project Rift glassmorphism aesthetic.

---

## Features

1. **Frosted-Glass Vitals Card (Bottom-Right)**:
   - Modern health bar with dynamic color configurations (pulsing/low health alert visual indicators).
   - Water and food tracking bars matching custom theme coloring.
2. **Status Strip (Bottom-Right, under Vitals)**:
   - Dynamic indicators for temperature (color-coded cold/hot), wetness (hidden when <= 5%), radiation (hidden when zero), bleeding, safe zones, and building blocks.
3. **Branding Header (Top-Left)**:
   - Elegant "PROJECT RIFT" banner with a purple neon accent indicator.
4. **Server Info Header (Top-Right)**:
   - Tracks players online, server FPS, average ping, current UTC time, and next wipe countdown.
5. **Night Indicator (Top-Left)**:
   - A conditional panel showing a moon icon and remaining time until sunrise. Only visible between 19:00 and 5:00.
6. **Notification Stack (Middle-Right)**:
   - Supports up to 3 stacked alerts that slide in and automatically dismiss.
7. **Event Announcer (Top-Center)**:
   - Temporary broadcast cards to announce events (e.g. Bradley Active, Heli Incoming) with custom API methods.

---

## Installation

1. Copy `ProjectRiftHUD.cs` into your server's `carbon/plugins/` or `oxide/plugins/` directory.
2. The plugin compiles automatically and registers permissions.
3. (Optional) Disable the basic stats overlay in `ProjectRiftCore.cs` configuration to avoid overlapping bottom-right widgets.

---

## Commands & Permissions

### Permissions
* `projectrifthud.use` — Required to view the HUD (granted to the `default` group automatically on first load).
* `projectrifthud.admin` — Required to run administrative reload and test commands.
* `projectrifthud.bypass` — Bypasses HUD rendering rules.

### Commands
* `/hud` — Toggles the entire HUD on/off for the executing player.
* `/hud reset` — Rebuilds and realigns all active panels.
* `/hud reload` (Admin only) — Reloads the config from disk and refreshes the HUD for all players.
* `/hud test` (Admin only) — Spawns a test notification stack and top-center event card.

---

## Developer API

Other plugins can interact with the Project Rift HUD via the following Hook APIs:

### `AddNotification`
Adds a notification card to the player's middle-right stack.
```csharp
// Signature
void AddNotification(BasePlayer player, string message, string type = "info", float duration = 5f)

// Usage example
ProjectRiftHUD?.Call("AddNotification", player, "You received 500 Scrap!", "success", 5.0f);
```

### `ShowEvent`
Displays an event banner at the top-center of the screen.
```csharp
// Signature
void ShowEvent(BasePlayer player, string eventName, float duration = 8f)

// Usage example
ProjectRiftHUD?.Call("ShowEvent", player, "Cargo Ship Active", 10.0f);
```
