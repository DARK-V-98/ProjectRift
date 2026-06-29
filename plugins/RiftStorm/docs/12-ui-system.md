# Step 12 — UI System (CUI, purple theme)

`UiManager` draws the modern, purple-themed HUD with Rust's CUI. It shows
everything the brief asks for and is **throttled + diffed** so re-rendering for
100+ players is cheap.

## What the HUD shows

| Element | Phase(s) | Source |
| ------- | -------- | ------ |
| Countdown timer | Detection | `ev.CountdownRemaining` |
| Location name | all | `ev.Location.Name` |
| Wave number | Waves | `ev.WaveIndex / WaveCount` |
| Portal stability % | Objectives | `ev.Stability` |
| Crystal count | Objectives | `ev.CrystalsAlive / CrystalsTotal` |
| Boss health bar | Boss | `ev.BossHpFraction` |
| Players in zone | all | `ev.Zone.PlayersInside().Count` |
| Reward preview | Detection/Victory | config (toggle) |

## Theme constants

```csharp
// Project Rift palette — matches the website + ProjectRiftCore
private const string Panel   = "0.02 0.03 0.05 0.85";  // #05070D glass
private const string Accent  = "0.69 0.15 1.0 1.0";    // #B026FF purple
private const string Cyan    = "0.0 0.90 1.0 1.0";     // #00E5FF
private const string Faint   = "1 1 1 0.10";
```

## `src/Managers/UiManager.cs` (core shape)

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class UiManager
        {
            private const string Root = "riftstorm.hud";
            private const string BossRoot = "riftstorm.bossbar";
            private readonly RiftContext ctx;
            private float lastRefresh;
            private string lastSig = "";          // diff signature to skip no-op redraws

            public UiManager(RiftContext ctx) { this.ctx = ctx; }

            // throttled entry point called by EventManager.Tick()
            public void Refresh(RiftEvent ev)
            {
                if (!ctx.Config.Ui.Enabled) return;
                if (UnityEngine.Time.realtimeSinceStartup - lastRefresh < ctx.Config.Ui.RefreshSeconds) return;
                lastRefresh = UnityEngine.Time.realtimeSinceStartup;

                var sig = Signature(ev);
                if (sig == lastSig) return;       // nothing visible changed → skip
                lastSig = sig;

                foreach (var pl in BasePlayer.activePlayerList)
                    DrawHud(pl, ev);
            }

            private string Signature(RiftEvent ev) =>
                $"{ev.Phase}|{Mathf.CeilToInt(ev.CountdownRemaining)}|{ev.WaveIndex}|" +
                $"{ev.Stability:0}|{ev.CrystalsAlive}|{Mathf.RoundToInt(ev.BossHpFraction*100)}|" +
                $"{ev.Zone.PlayersInside().Count}";

            private void DrawHud(BasePlayer pl, RiftEvent ev)
            {
                var c = new CuiElementContainer();
                // top-center panel
                var panel = c.Add(new CuiPanel {
                    Image = { Color = Panel, Material = "assets/content/ui/uibackgroundblur.mat" },
                    RectTransform = { AnchorMin = "0.32 0.88", AnchorMax = "0.68 0.985" },
                    CursorEnabled = false
                }, "Hud", Root);

                // accent top border
                c.Add(new CuiPanel { Image = { Color = Accent },
                    RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" } }, panel);

                // title line
                Label(c, panel, $"⚡ RIFT STORM — {ev.Location.Name.ToUpper()}",
                      14, "0.04 0.55", "0.96 0.95", TextAnchor.MiddleLeft, Accent);

                // dynamic line by phase
                Label(c, panel, PhaseLine(ev), 12, "0.04 0.08", "0.96 0.5",
                      TextAnchor.MiddleLeft, "1 1 1 0.9");

                // players in zone (right)
                Label(c, panel, $"👥 {ev.Zone.PlayersInside().Count}", 12,
                      "0.78 0.55", "0.97 0.95", TextAnchor.MiddleRight, Cyan);

                CuiHelper.DestroyUi(pl, Root);
                CuiHelper.AddUi(pl, c);
            }

            private string PhaseLine(RiftEvent ev)
            {
                switch (ev.Phase)
                {
                    case RiftPhaseId.Detection:
                        return $"Opens in {Mathf.CeilToInt(ev.CountdownRemaining)}s";
                    case RiftPhaseId.Storm:  return "Storm intensifying...";
                    case RiftPhaseId.Rift:   return "The rift is open!";
                    case RiftPhaseId.Waves:  return $"Wave {ev.WaveIndex + 1}/{ev.WaveCount} — clear the scientists";
                    case RiftPhaseId.Objectives:
                        return $"Stability {ev.Stability:0}%  •  Crystals {ev.CrystalsAlive}/{ev.CrystalsTotal}";
                    case RiftPhaseId.Boss:   return $"RIFT OVERLORD  •  {ev.BossHpFraction*100:0}% HP";
                    case RiftPhaseId.Victory:return "VICTORY — Rift Crate incoming!";
                    default: return "";
                }
            }

            // ---- boss health bar (separate root so it sits lower-center) -----
            public void ShowBossBarAll(RiftEvent ev) { foreach (var p in BasePlayer.activePlayerList) DrawBossBar(p, ev); }
            public void UpdateBossBar(RiftEvent ev)  { foreach (var p in BasePlayer.activePlayerList) DrawBossBar(p, ev); }
            public void HideBossBarAll()             { foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, BossRoot); }

            private void DrawBossBar(BasePlayer pl, RiftEvent ev)
            {
                float frac = ev.BossHpFraction;
                var c = new CuiElementContainer();
                var bar = c.Add(new CuiPanel {
                    Image = { Color = Panel },
                    RectTransform = { AnchorMin = "0.3 0.12", AnchorMax = "0.7 0.16" }
                }, "Hud", BossRoot);
                // fill
                c.Add(new CuiPanel { Image = { Color = ev.BossRaged ? "1 0.2 0.4 0.95" : Accent },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = $"{frac} 1" } }, bar);
                Label(c, bar, $"{ctx.Config.Boss.Name}   {frac*100:0}%", 13, "0 0", "1 1",
                      TextAnchor.MiddleCenter, "1 1 1 1");
                CuiHelper.DestroyUi(pl, BossRoot);
                CuiHelper.AddUi(pl, c);
            }

            // ---- countdown splash on detection -------------------------------
            public void ShowCountdownAll(RiftEvent ev) { /* big center splash, fades */ }

            public void DestroyAll()
            {
                foreach (var pl in BasePlayer.activePlayerList)
                { CuiHelper.DestroyUi(pl, Root); CuiHelper.DestroyUi(pl, BossRoot); }
                lastSig = "";
            }

            private void Label(CuiElementContainer c, string parent, string text, int size,
                               string min, string max, TextAnchor align, string color)
            {
                c.Add(new CuiLabel {
                    Text = { Text = text, FontSize = size, Align = align, Color = color,
                             Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                }, parent);
            }
        }
    }
}
```

## Performance rules baked in

1. **Throttle** — `RefreshSeconds` gate (default 1s) caps redraw frequency.
2. **Diff** — a `Signature(ev)` string skips redraws when nothing visible
   changed (e.g. countdown second unchanged).
3. **Separate roots** — HUD vs boss bar are independent so we only rebuild what
   moved.
4. **Destroy on cleanup** — `DestroyAll()` runs in `EventManager.Cleanup()` and
   on `Unload()`/`OnPlayerDisconnected` to prevent ghost UI.

## New players mid-event

Hook `OnPlayerConnected` / `OnPlayerRespawned`: if `events.IsRunning`, force
`lastSig = ""` so the next `Refresh` redraws for everyone (cheap, throttled).

> **Reward preview** (config-toggle): when `ShowRewardPreview` is true, the
> Detection/Victory splash lists the top crate items (icons via the game item
> icon CDN or ImageLibrary), matching the website's reward styling.

Next: **[Step 13 — Discord integration](13-discord-integration.md)**.
