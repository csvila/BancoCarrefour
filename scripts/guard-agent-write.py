#!/usr/bin/env python3
"""Claude Code PreToolUse guard: restrict Write/Edit targets per authorized agent."""
import json, sys, os
from pathlib import Path

role = sys.argv[1] if len(sys.argv) > 1 else "unknown"
try:
    event = json.load(sys.stdin)
except Exception as exc:
    print(f"Unable to parse hook input: {exc}", file=sys.stderr); sys.exit(2)
tool_input = event.get("tool_input") or {}
raw = tool_input.get("file_path") or tool_input.get("path") or ""
if not raw:
    print("Blocked: missing target path; fail closed.", file=sys.stderr); sys.exit(2)
path = Path(raw)
try:
    resolved = path.resolve()
except Exception:
    resolved = path
# Normalize to workspace-relative path when possible.
try:
    rel = resolved.relative_to(Path.cwd().resolve()).as_posix()
except Exception:
    rel = str(resolved).replace('\\','/')

if role == "developer":
    allowed = rel.startswith("src/") or rel.startswith("infra/") or rel.startswith("deploy/")
elif role == "test-analyst":
    allowed = rel.startswith("tests/")
else:
    allowed = False
if not allowed:
    print(f"Blocked write by {role}: {rel}. Developer may write only src/, infra/, deploy/; test-analyst only tests/.", file=sys.stderr)
    sys.exit(2)
sys.exit(0)
