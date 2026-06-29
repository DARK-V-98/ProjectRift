# dist/ — shippable plugin

`RiftStorm.cs` is the complete, single-file Rift Storm plugin (the docs' **Mode
B**: all managers/models/phases as nested classes in one `RustPlugin`). This is
the file you deploy — no merge step required.

## Install

- **Carbon:** copy to `carbon/plugins/RiftStorm.cs` → config auto-generates at
  `carbon/configs/RiftStorm.json`, lang at `carbon/lang/en/RiftStorm.json`.
- **Oxide/uMod:** copy to `oxide/plugins/RiftStorm.cs` → config at
  `oxide/config/RiftStorm.json`, lang at `oxide/lang/en/RiftStorm.json`.

Reload: `c.reload RiftStorm` (Carbon) or `o.reload RiftStorm` (Oxide).

## Companion website endpoint

The plugin pushes live event JSON to `app/api/rift` (see `docs/14`). Set the
same secret in both places:

- plugin config → `Website API → API key`
- website `.env.local` → `PROJECT_RIFT_API_KEY`

## ⚠️ Validate before going live

Rust's runtime API changes across forced wipes. Calls touching game internals
(entity spawn/health, NPC gear, effect prefab paths) follow common plugin idioms
and are wrapped defensively, but **compile and field-test on a staging Carbon
server** before production. Tune prefab paths in config to match the current
game build if any effect/entity does not appear.

The development source split (partial classes per subsystem) is described in
`../docs/02-folder-structure.md` for when you want to break this file apart.
