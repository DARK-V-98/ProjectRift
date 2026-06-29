#!/usr/bin/env python3
"""Portable structural validator for the RiftStorm plugin source.

This is NOT a C# compiler. It is a dependency-free sanity check (python3 only)
that catches the structural mistakes which are easy to introduce by hand and
which would otherwise only surface on a real `dotnet`/Carbon build:

  * unbalanced { } ( ) [ ]  (via a string/comment-aware state machine)
  * mismatched #region / #endregion
  * every `: IRiftPhase` implementer is present
  * a single top-level `namespace Oxide.Plugins`

For a genuine type-check you still need to compile against the Carbon/Rust
reference assemblies — see build/README.md. The SessionStart hook runs this
check everywhere, and runs the real compile only where the toolchain exists.

Usage:  python3 build/lint.py [path-to-.cs]   (defaults to dist/RiftStorm.cs)
Exit code 0 = OK, 1 = problem found.
"""
import sys
import os
import re

def walk(src):
    """String/comment-aware bracket-balance walk. Returns (errors, depths)."""
    i, n = 0, len(src)
    line = 1
    state = "code"  # code | line_c | block_c | str | vstr | char
    pairs = {"{": "}", "(": ")", "[": "]"}
    closing = {v: k for k, v in pairs.items()}
    stack = []
    errors = []
    while i < n:
        ch = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if ch == "\n":
            line += 1
        if state == "code":
            if ch == "/" and nxt == "/":
                state = "line_c"; i += 2; continue
            if ch == "/" and nxt == "*":
                state = "block_c"; i += 2; continue
            if ch == "@" and nxt == '"':
                state = "vstr"; i += 2; continue
            if ch == '"':
                state = "str"; i += 1; continue
            if ch == "'":
                i += 1
                if i < n and src[i] == "\\":
                    i += 2
                else:
                    i += 1
                if i < n and src[i] == "'":
                    i += 1
                continue
            if ch in pairs:
                stack.append((ch, line))
            elif ch in closing:
                if not stack:
                    errors.append(f"line {line}: unexpected '{ch}'")
                else:
                    op, ln = stack.pop()
                    if pairs[op] != ch:
                        errors.append(f"line {line}: '{ch}' closes '{op}' from line {ln}")
            i += 1; continue
        if state == "line_c":
            if ch == "\n": state = "code"
            i += 1; continue
        if state == "block_c":
            if ch == "*" and nxt == "/":
                state = "code"; i += 2; continue
            i += 1; continue
        if state == "str":
            if ch == "\\": i += 2; continue
            if ch == '"': state = "code"
            i += 1; continue
        if state == "vstr":
            if ch == '"' and nxt == '"': i += 2; continue
            if ch == '"': state = "code"
            i += 1; continue
    for op, ln in stack:
        errors.append(f"line {ln}: unclosed '{op}'")
    if state != "code":
        errors.append(f"file ended inside {state}")
    return errors


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "dist", "RiftStorm.cs")
    path = os.path.abspath(path)
    if not os.path.exists(path):
        print(f"[lint] FAIL: file not found: {path}")
        return 1

    src = open(path, encoding="utf-8").read()
    problems = []

    problems += walk(src)

    nreg, nend = src.count("#region"), src.count("#endregion")
    if nreg != nend:
        problems.append(f"#region/#endregion mismatch: {nreg} vs {nend}")

    ns = len(re.findall(r"^\s*namespace\s+Oxide\.Plugins\b", src, re.M))
    if ns != 1:
        problems.append(f"expected exactly one 'namespace Oxide.Plugins', found {ns}")

    phases = set(re.findall(r"class\s+(\w+)\s*:\s*IRiftPhase", src))
    expected = {"DetectionPhase", "StormPhase", "RiftSpawnPhase", "WavesPhase",
                "ObjectivesPhase", "BossPhase", "VictoryPhase"}
    missing = expected - phases
    if missing:
        problems.append(f"missing IRiftPhase implementers: {sorted(missing)}")

    if not re.search(r"\[Info\(\"RiftStorm\"", src):
        problems.append("missing [Info(\"RiftStorm\", ...)] plugin attribute")

    name = os.path.basename(path)
    if problems:
        print(f"[lint] FAIL ({name}):")
        for p in problems:
            print(f"   - {p}")
        return 1

    print(f"[lint] OK: {name} — brackets balanced, regions matched, "
          f"{len(phases)} phases, single namespace.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
