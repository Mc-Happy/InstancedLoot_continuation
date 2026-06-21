#!/usr/bin/env python3
# PreToolUse guard: block any interaction with Windows-mounted (/mnt/*) paths,
# EXCEPT the InstancedLoot r2modman deploy directory. Keeps Claude working inside
# the WSL filesystem and only reaching out to the one folder it deploys into.
import json
import re
import sys

ALLOWED = "/mnt/c/Users/Radek/AppData/Roaming/r2modmanPlus-local/RiskOfRain2/profiles/Default/BepInEx/plugins/InstancedLoot/InstancedLoot"

# Deploy-dir paths contain no spaces, so they are captured in full. A disallowed
# Windows path with spaces is truncated at the first space but still fails the
# prefix test below, so it is still denied.
MNT_RE = re.compile(r"/mnt/[a-zA-Z]/[^\s\"']*")


def deny(reason):
    json.dump(
        {
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "permissionDecision": "deny",
                "permissionDecisionReason": reason,
            }
        },
        sys.stdout,
    )
    sys.exit(0)


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        sys.exit(0)  # don't break tools on malformed input

    ti = data.get("tool_input") or {}
    candidates = [
        ti.get("file_path"),
        ti.get("command"),
        ti.get("path"),
        ti.get("notebook_path"),
    ]

    for value in candidates:
        if not isinstance(value, str):
            continue
        for path in MNT_RE.findall(value):
            if ".." in path:
                deny(f"Blocked: path traversal ('..') into a Windows mount is not allowed ({path}).")
            if path == ALLOWED or path.startswith(ALLOWED + "/"):
                continue
            deny(
                "Blocked: access to Windows-mounted paths (/mnt/*) is restricted to the "
                f"InstancedLoot deploy directory. Got: {path}"
            )

    sys.exit(0)


if __name__ == "__main__":
    main()
