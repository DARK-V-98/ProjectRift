# build/ — RiftStorm compile gate

Tooling to validate `dist/RiftStorm.cs` before you deploy it.

| File | Purpose |
| ---- | ------- |
| `lint.py` | Dependency-free structural check (brackets, regions, phases, namespace). Runs everywhere. |
| `check.sh` | Orchestrator: always lints; compiles too when the toolchain + refs exist. |
| `RiftStorm.csproj` | Real C# type-check of the plugin against Carbon/Rust/Unity DLLs. |
| `merge.sh` | (Optional) merge `src/` partials into `dist/` — see `docs/02`. |
| `refs/` | You drop the reference DLLs here (git-ignored, not redistributable). |

## Quick check (works in any environment)

```bash
bash plugins/RiftStorm/build/check.sh
```

Without a C# toolchain this runs the **structural lint** only and reports that
the real compile was skipped. That is the expected behavior in Claude Code on
the web, where the .NET download hosts are blocked by the session's egress
policy — so the genuine compile is done on your own Carbon dev box or CI.

## Real type-check (your Carbon dev box / CI)

You need the **.NET SDK** and the **reference assemblies** the plugin compiles
against. The DLLs ship with your server install; they are not committed here.

1. **Install the .NET SDK** (once): https://dotnet.microsoft.com/download

2. **Provide the reference DLLs.** Either copy them into `build/refs/`, or point
   the build at an existing managed folder. A Rust+Carbon server has them under:

   - `RustDedicated_Data/Managed/` — Rust + Unity + Newtonsoft (`Assembly-CSharp.dll`,
     `Facepunch.*.dll`, `UnityEngine.*.dll`, `Rust.*.dll`, `Newtonsoft.Json.dll`, …)
   - `carbon/managed/` (Carbon) or `RustDedicated_Data/Managed/x86_64/` (Oxide) —
     the framework DLLs (`Carbon.Common.dll` / `Oxide.Core.dll`,
     `Oxide.Rust.dll`, `Oxide.References` …)

   The simplest reliable option is to point `RefsDir` at the server's
   `Managed` folder (it contains everything):

   ```bash
   dotnet build plugins/RiftStorm/build/RiftStorm.csproj \
     -p:RefsDir="/path/to/server/RustDedicated_Data/Managed"
   ```

   or copy the DLLs in and use the default:

   ```bash
   cp /path/to/server/RustDedicated_Data/Managed/*.dll plugins/RiftStorm/build/refs/
   bash plugins/RiftStorm/build/check.sh
   ```

3. **Target framework.** The csproj targets `net48` (Rust's Mono surface). If your
   Carbon build is .NET 8, set `<TargetFramework>net8.0</TargetFramework>` and use
   that install's managed assemblies.

A successful build only produces a type-check; you still deploy the **source**
`dist/RiftStorm.cs` to `carbon/plugins/` (Carbon compiles it in-process).

## CI

In a workflow runner that has network access to the .NET feeds and a way to
obtain the reference DLLs (e.g. cache them as a build artifact or restore a
Carbon reference package), run:

```bash
bash plugins/RiftStorm/build/check.sh   # with RIFTSTORM_REFS pointing at the DLLs
```

It exits non-zero on a structural or compile error, so it gates a PR.
