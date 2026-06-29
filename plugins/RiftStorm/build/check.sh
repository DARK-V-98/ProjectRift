#!/usr/bin/env bash
# ============================================================================
#  RiftStorm compile gate.
#
#  Two tiers, so it does something useful in EVERY environment:
#    1. Structural lint (python3 only)  — always runs. Catches unbalanced
#       brackets, region mismatches, missing phases. Fails the gate on error.
#    2. Real C# compile (dotnet + Carbon/Rust reference DLLs) — runs only when
#       both are available. This is the genuine type-check.
#
#  In Claude Code on the web the .NET toolchain hosts are blocked by egress
#  policy, so tier 2 is skipped there and the real compile happens on your
#  Carbon dev box / CI (see build/README.md). Tier 1 still guards every session.
#
#  Usage:  bash plugins/RiftStorm/build/check.sh
#  Env:    RIFTSTORM_REFS=/path/to/Managed   (overrides build/refs)
# ============================================================================
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/.."
SRC="$ROOT/dist/RiftStorm.cs"
REFS="${RIFTSTORM_REFS:-$HERE/refs}"

echo "== RiftStorm check =="

# ---- tier 1: structural lint (authoritative for structure) ----------------
if ! python3 "$HERE/lint.py" "$SRC"; then
  echo "== FAILED (structural lint) =="
  exit 1
fi

# ---- tier 2: real compile, when possible ----------------------------------
if command -v dotnet >/dev/null 2>&1 && ls "$REFS"/*.dll >/dev/null 2>&1; then
  echo "[compile] dotnet + reference DLLs found in $REFS — building..."
  if dotnet build "$HERE/RiftStorm.csproj" -p:RefsDir="$REFS" --nologo -v quiet; then
    echo "[compile] OK — type-check passed."
  else
    echo "== FAILED (compile) =="
    exit 1
  fi
else
  echo "[compile] skipped — dotnet and/or reference DLLs not available."
  echo "          (Expected on Claude Code on the web: .NET download hosts are"
  echo "           blocked by egress policy. Compile on your Carbon dev box / CI;"
  echo "           see plugins/RiftStorm/build/README.md to populate $REFS.)"
fi

echo "== done =="
