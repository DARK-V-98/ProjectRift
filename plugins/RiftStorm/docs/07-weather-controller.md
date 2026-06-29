# Step 07 — Weather Controller & Atmosphere

`WeatherController` makes Phase 2 (Storm) feel dangerous: it overrides server
weather (rain/fog/clouds/wind/thunder) and spawns the purple smoke + lights
atmosphere. It **always restores** the previous weather on cleanup.

## How Rust weather override works

Rust exposes weather via `Climate`/`TOD_Sky` and the `weather.*` convars. The
robust approach is to drive the **environment override** convars each tick so
vanilla weather can't fight us, then clear them on restore.

## `src/Managers/WeatherController.cs`

```csharp
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        public class WeatherController
        {
            private readonly RiftContext ctx;
            private bool active;
            private Timer thunderTimer;
            private readonly List<BaseEntity> fxLights = new List<BaseEntity>();

            public WeatherController(RiftContext ctx) { this.ctx = ctx; }

            public void RampUp(RiftEvent ev)
            {
                if (!ctx.Config.Weather.Enabled) return;
                active = true;
                var w = ctx.Config.Weather;
                // Override env so vanilla cycle can't undo it.
                ApplyOverride("weather.rain", w.Rain);
                ApplyOverride("weather.fog", w.Fog);
                ApplyOverride("weather.clouds", w.Clouds);
                ApplyOverride("weather.wind", w.Wind);
                ctx.Logger.Info("Weather override applied (storm).");

                if (w.Thunder) StartThunder();
                SpawnAtmosphere(ev.Center);
            }

            private void ApplyOverride(string convar, float value) =>
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, convar, value);

            private void StartThunder()
            {
                thunderTimer?.Destroy();
                void Strike()
                {
                    if (!active) return;
                    foreach (var pl in BasePlayer.activePlayerList)
                        Effect.server.Run("assets/bundled/prefabs/fx/impacts/additive/fire.prefab",
                                          pl.transform.position + Vector3.up * 30f);
                    float next = UnityEngine.Random.Range(ctx.Config.Weather.ThunderMin,
                                                          ctx.Config.Weather.ThunderMax);
                    thunderTimer = ctx.Timer(next, Strike);
                }
                thunderTimer = ctx.Timer(ctx.Config.Weather.ThunderMin, Strike);
            }

            // Purple smoke ring + purple point lights around the site.
            public void SpawnAtmosphere(Vector3 center)
            {
                var w = ctx.Config.Weather;
                int n = Mathf.Max(0, w.LightCount);
                for (int i = 0; i < n; i++)
                {
                    float a = (360f / n) * i * Mathf.Deg2Rad;
                    var pos = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * w.RingRadius;
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 1f;

                    // continuous purple smoke
                    Effect.server.Run(w.SmokePrefab, pos);

                    // a purple light: deployable light entity tinted purple
                    var light = GameManager.server.CreateEntity(
                        "assets/prefabs/deployable/playerioents/lights/simplelight/electric.simplelight.deployed.prefab",
                        pos + Vector3.up * 1.2f) as BaseEntity;
                    if (light != null)
                    {
                        light.Spawn();
                        TintPurple(light);
                        fxLights.Add(light);
                        ctx.Pool.Track(light);     // register for cleanup
                    }
                }
            }

            private void TintPurple(BaseEntity e)
            {
                // IO lights expose color via flags/RPC; simplest is a colored
                // point-light child. Production: attach a Light component child
                // with color #B026FF, range ~15, intensity ~3.
                var go = e.gameObject;
                var pl = go.GetComponent<Light>() ?? go.AddComponent<Light>();
                pl.color = HexToColor(ctx.Config.Ui.Accent);
                pl.range = 15f; pl.intensity = 3f;
            }

            public void Restore()
            {
                if (!active) return;
                active = false;
                thunderTimer?.Destroy();
                // hand weather back to the vanilla cycle
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, "weather.rain", -1);
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, "weather.fog", -1);
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, "weather.clouds", -1);
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet, "weather.wind", -1);
                fxLights.Clear(); // entities killed by Pool.CleanupAll
                ctx.Logger.Info("Weather restored.");
            }

            public static Color HexToColor(string hex)
            {
                ColorUtility.TryParseHtmlString(hex, out var c);
                return c;
            }
        }
    }
}
```

## StormPhase — `src/Phases/StormPhase.cs`

```csharp
public class StormPhase : IRiftPhase
{
    public RiftPhaseId Id => RiftPhaseId.Storm;
    private const float Duration = 12f; // build-up seconds before the rift opens

    public void Enter(RiftEvent ev, RiftContext ctx)
    {
        ctx.Weather.RampUp(ev);
        ctx.Broadcast(ctx.Config.Announce.Prefix + " The storm intensifies...");
        foreach (var pl in BasePlayer.activePlayerList)
            Effect.server.Run(ctx.Config.Announce.StormSound, pl.transform.position);
        ctx.Discord.Notify(new RiftNotice {
            Type = NoticeType.StormActive, Title = "🌩️ Storm Active",
            Message = "The dimensional storm is raging.", LocationName = ev.Location.Name });
    }
    public void Tick(RiftEvent ev, RiftContext ctx) { }
    public bool IsComplete(RiftEvent ev) =>
        (DateTime.UtcNow - ev.PhaseStartedUtc).TotalSeconds >= Duration;
    public void Exit(RiftEvent ev, RiftContext ctx) { }
}
```

> **uMod note:** the `weather.*` convars exist on both Carbon and Oxide. If a
> future Rust build renames them, swap `ApplyOverride` to set
> `Climate.Instance` overrides directly — the rest of the controller is unchanged.

Next: **[Step 08 — Rift & objectives](08-rift-and-objectives.md)**.
