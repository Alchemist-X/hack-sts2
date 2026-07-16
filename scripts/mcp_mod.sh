#!/usr/bin/env bash
# Toggle the STS2_MCP injection mod (agent control channel) in the game's mods dir.
#   mcp_mod.sh on     - install for agent/LLM eval runs
#   mcp_mod.sh off    - remove for pure-human recording (default state)
#   mcp_mod.sh status - show current state
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODS="${STS2ModsDir:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods}"
STASH="$REPO_ROOT/.mcp-stashed"
SETTINGS="$HOME/Library/Application Support/SlayTheSpire2/steam/76561198323101015/settings.save"
FILES=(STS2_MCP.dll STS2_MCP.json STS2_MCP.conf)

sync_settings() { # $1 = add|remove
  python3 - "$1" "$SETTINGS" <<'EOF'
import json, shutil, sys
mode, path = sys.argv[1], sys.argv[2]
d = json.load(open(path))
ms = d.get("mod_settings") or {"mods_enabled": True, "mod_list": []}
ms["mod_list"] = [m for m in ms.get("mod_list", []) if m.get("id") != "STS2_MCP"]
if mode == "add":
    ms["mod_list"].append({"id": "STS2_MCP", "is_enabled": True, "source": "mods_directory"})
ms["mods_enabled"] = True
d["mod_settings"] = ms
json.dump(d, open(path, "w"), indent=2)
shutil.copy(path, path + ".backup")
EOF
}

case "${1:-status}" in
  on)
    mkdir -p "$MODS"
    for f in "${FILES[@]}"; do [[ -f "$STASH/$f" ]] && mv "$STASH/$f" "$MODS/"; done
    sync_settings add
    echo "STS2_MCP installed (agent eval mode). Remember: mcp_mod.sh off when done."
    ;;
  off)
    mkdir -p "$STASH"
    for f in "${FILES[@]}"; do [[ -f "$MODS/$f" ]] && mv "$MODS/$f" "$STASH/"; done
    sync_settings remove
    echo "STS2_MCP removed (pure-human recording mode)."
    ;;
  status)
    if [[ -f "$MODS/STS2_MCP.dll" ]]; then echo "STS2_MCP: INSTALLED (agent eval mode)"
    else echo "STS2_MCP: not installed (pure-human recording mode)"; fi
    ls "$MODS" 2>/dev/null | sed 's/^/  mods\//'
    ;;
  *) echo "usage: $0 on|off|status" >&2; exit 2 ;;
esac
