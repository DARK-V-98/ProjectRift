# src/ — development source (partial classes)

This folder holds the per-subsystem C# source as you build it following
`docs/06` through `docs/16`. Every file declares `public partial class RiftStorm`
inside `namespace Oxide.Plugins`, mirroring the layout in `docs/02-folder-structure.md`.

Run `bash ../build/merge.sh` to produce the single shippable `dist/RiftStorm.cs`.
