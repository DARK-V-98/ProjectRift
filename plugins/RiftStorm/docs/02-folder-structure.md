# Step 02 — Folder Structure & Build Strategy

## Repository layout (this folder)

```
plugins/RiftStorm/
├── README.md                  ← index / overview
├── docs/                      ← this step-by-step guide (00..17)
├── src/                       ← development source (partial classes)
│   ├── RiftStorm.cs            ← entry point + composition root
│   ├── Core/
│   │   ├── RiftContext.cs       ← DI container
│   │   ├── RiftLogger.cs        ← structured logging
│   │   ├── EntityPool.cs        ← pooling + cleanup registry
│   │   └── RiftZone.cs          ← spatial / zone helper
│   ├── Config/
│   │   └── Configuration.cs     ← config POCO + defaults + loader
│   ├── Models/
│   │   ├── RiftEvent.cs         ← live event state
│   │   ├── RiftPhaseId.cs       ← phase enum
│   │   ├── NpcProfile.cs        ← NPC definition
│   │   ├── LootTable.cs         ← loot definitions
│   │   └── RiftLocation.cs      ← location definition
│   ├── Phases/
│   │   ├── IRiftPhase.cs
│   │   ├── DetectionPhase.cs
│   │   ├── StormPhase.cs
│   │   ├── RiftPhase.cs
│   │   ├── WavesPhase.cs
│   │   ├── ObjectivesPhase.cs
│   │   ├── BossPhase.cs
│   │   └── VictoryPhase.cs
│   ├── Managers/
│   │   ├── RiftEventManager.cs
│   │   ├── WeatherController.cs
│   │   ├── RiftController.cs
│   │   ├── NpcManager.cs
│   │   ├── BossController.cs
│   │   ├── LootManager.cs
│   │   └── UiManager.cs
│   ├── Integrations/
│   │   ├── INotifier.cs
│   │   ├── DiscordService.cs
│   │   └── ApiService.cs
│   └── Commands/
│       └── AdminCommands.cs
├── build/
│   ├── merge.ps1               ← merges src/**/*.cs → dist/RiftStorm.cs
│   └── merge.sh                ← same, bash
├── dist/
│   └── RiftStorm.cs            ← the single shippable plugin (generated)
└── lang/
    └── en/RiftStorm.json       ← localized messages
```

## Two development modes

### Mode A — Partial classes (recommended)

All `src/**/*.cs` files declare the **same** partial class so they read like a
normal multi-file OO project, but merge cleanly into one plugin:

```csharp
// src/Managers/WeatherController.cs
namespace Oxide.Plugins
{
    public partial class RiftStorm
    {
        // WeatherController is a NESTED class of the plugin so it can be split
        // across files via the partial outer class, and reach plugin helpers.
        public class WeatherController { /* ... */ }
    }
}
```

> Carbon supports loading a folder of partials in dev, but for **uMod/Oxide
> compatibility** and reliable hot-loads, always ship the **merged single file**
> from `dist/`.

### Mode B — Standalone classes in one file

If you prefer no build step, keep everything in one `RiftStorm.cs` with the
managers as plain top-level classes inside `namespace Oxide.Plugins`. Same
architecture, just one physical file. Use this if your CI can't run the merge.

## The merge build

`build/merge.sh` concatenates files in dependency order, strips duplicate
`using`/namespace lines, and emits `dist/RiftStorm.cs`:

```bash
#!/usr/bin/env bash
# build/merge.sh — merge partial sources into one shippable plugin file.
set -euo pipefail
SRC="$(dirname "$0")/../src"
OUT="$(dirname "$0")/../dist/RiftStorm.cs"

# 1) collect every using across the project, de-dup, sort
USINGS=$(grep -hR '^using ' "$SRC" | sort -u)

# 2) emit header + usings + single namespace + merged class bodies
{
  echo "// AUTO-GENERATED — do not edit. Edit src/ and re-run build/merge.sh"
  echo "$USINGS"
  echo "namespace Oxide.Plugins"
  echo "{"
  # strip per-file usings + namespace wrappers, keep bodies
  grep -hRL '^$' "$SRC" >/dev/null 2>&1 || true
  for f in $(find "$SRC" -name '*.cs' | sort); do
    sed -e '/^using /d' -e '/^namespace Oxide.Plugins/d' "$f" \
      | awk 'NR==1{found=0} {print}'   # bodies (manual brace balancing in dev)
  done
  echo "}"
} > "$OUT"
echo "Wrote $OUT"
```

> The real merge must balance the outer namespace braces. In practice teams use
> a tiny C# Roslyn merger or the [Oxide.Compiler] partial support. The shell
> script above is the conceptual contract; pin a tested merger in CI.

## Install path on the server

The generated `dist/RiftStorm.cs` is dropped into:

- **Carbon:** `carbon/plugins/RiftStorm.cs` → config in `carbon/configs/RiftStorm.json`
- **Oxide/uMod:** `oxide/plugins/RiftStorm.cs` → config in `oxide/config/RiftStorm.json`

Lang file → `carbon/lang/en/RiftStorm.json` (or `oxide/lang/en/RiftStorm.json`).

## Build & reload loop

```bash
bash build/merge.sh                 # src/ → dist/RiftStorm.cs
cp dist/RiftStorm.cs <server>/carbon/plugins/
# in server console:
c.reload RiftStorm                  # Carbon   (o.reload on Oxide)
```

Next: **[Step 03 — Class diagram](03-class-diagram.md)**.
