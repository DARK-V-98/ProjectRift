// RiftBoost — client FPS helper for PROJECT RIFT.
//
// A server plugin can't render frames for a player's GPU, but it CAN push
// performance console commands to their client. /boost applies an FPS-friendly
// preset instantly; /boost off restores a balanced look. Per-player, opt-in.
//
//   /boost        apply the performance preset (higher FPS)
//   /boost off    restore balanced visuals
//
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("RiftBoost", "ESYSTEMLK", "1.0.0")]
    [Description("Client-side FPS preset: /boost applies performance settings to the player's client.")]
    public class RiftBoost : RustPlugin
    {
        private Configuration config;

        public class Configuration
        {
            // Console commands sent to the CLIENT when a player runs /boost.
            public List<string> BoostCommands = new List<string>
            {
                "fps.limit 0",
                "effects.aa false",
                "effects.ssao false",
                "effects.motionblur false",
                "effects.bloom false",
                "effects.depthoffield false",
                "effects.sunshafts false",
                "effects.lensdirt false",
                "effects.vignet false",
                "grass.displacement false",
                "water.quality 0",
                "water.reflections 0",
                "graphics.shadowdistance 50",
                "graphics.quality 1",
                "terrain.quality 50",
                "gametip.hidegametip true",
            };

            // Sent on /boost off — a balanced, decent-looking preset.
            public List<string> RestoreCommands = new List<string>
            {
                "effects.aa true",
                "effects.ssao true",
                "effects.bloom true",
                "effects.motionblur false",
                "grass.displacement true",
                "water.quality 2",
                "water.reflections 1",
                "graphics.shadowdistance 100",
                "graphics.quality 4",
                "terrain.quality 100",
            };

            public bool AnnounceOnJoin = true;   // tell players /boost exists
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<Configuration>() ?? new Configuration(); }
            catch { config = new Configuration(); }
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(config);

        private void OnPlayerConnected(BasePlayer player)
        {
            if (config.AnnounceOnJoin && player != null)
                timer.Once(20f, () =>
                {
                    if (player != null && player.IsConnected)
                        player.ChatMessage("<color=#B026FF>[RIFT]</color> Low FPS? Type <color=#00e5ff>/boost</color> for a performance preset (and /boost off to restore).");
                });
        }

        [ChatCommand("boost")]
        private void CmdBoost(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (args.Length > 0 && (args[0].ToLower() == "off" || args[0].ToLower() == "restore"))
            {
                SendAll(player, config.RestoreCommands);
                player.ChatMessage("<color=#B026FF>[RIFT]</color> Visuals restored to balanced.");
                return;
            }

            SendAll(player, config.BoostCommands);
            player.ChatMessage("<color=#B026FF>[RIFT]</color> <color=#2bff88>Performance preset applied!</color> Type /boost off to restore.");
        }

        private void SendAll(BasePlayer player, List<string> commands)
        {
            if (commands == null) return;
            foreach (var cmd in commands)
                if (!string.IsNullOrWhiteSpace(cmd))
                    player.SendConsoleCommand(cmd);
        }
    }
}
