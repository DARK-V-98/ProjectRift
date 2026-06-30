#!/bin/bash
# SessionStart hook for PROJECT RIFT.
#
# Prepares a Claude Code on the web session:
#   1. installs the Next.js website dependencies (npm)
#   2. runs the RiftStorm plugin compile gate (structural lint always; real C#
#      compile only where the .NET toolchain + Carbon reference DLLs exist —
#      those download hosts are blocked by the web egress policy, so the gate
#      degrades to the structural lint here and the full compile runs on CI / a
#      Carbon dev box).
#
# Synchronous + idempotent + non-interactive.
set -uo pipefail

# Only run setup in the remote (Claude Code on the web) environment.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-.}"

# 1) Website dependencies (registry.npmjs.org is reachable through the proxy).
#    --no-package-lock so a session never rewrites package-lock.json (npm would
#    otherwise churn optional/peer metadata and leave the working tree dirty).
if [ -f package.json ]; then
  echo "[session-start] installing npm dependencies..."
  npm install --no-audit --no-fund --no-package-lock || echo "[session-start] WARN: npm install failed (continuing)."
fi

# 2) RiftStorm plugin compile gate (best-effort; never blocks startup).
if [ -f plugins/RiftStorm/build/check.sh ]; then
  echo "[session-start] running RiftStorm compile gate..."
  bash plugins/RiftStorm/build/check.sh || echo "[session-start] WARN: RiftStorm check reported issues (see above)."
fi

echo "[session-start] done."
