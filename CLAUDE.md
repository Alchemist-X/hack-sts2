# hack-sts2 — operating rules

## STS2_MCP mod: agent-eval only, never left installed

The game's `mods/` dir has two possible occupants:

- `Sts2Recorder` — the passive trajectory recorder. **Always installed.**
- `STS2_MCP` — the agent control channel (HTTP action injection). **Only installed
  while an agent/LLM eval is actively running.**

Rule: the default state is pure-human recording (`STS2_MCP` stashed in
`.mcp-stashed/`). When starting agent/LLM eval work, install it; when the eval
session ends, remove it again — do this without being asked:

```bash
scripts/mcp_mod.sh on      # begin agent/LLM eval
scripts/mcp_mod.sh off     # ALWAYS run when the eval session ends
scripts/mcp_mod.sh status  # check current state
```

Why: human trajectories are the benchmark's core asset. Leaving the injection
channel installed while the user plays contaminates provenance (MCP drives the
same UI code paths as a human, so its actions can be indistinguishable in the
`human` flag). Keep the two data sources physically separated by install state.

## Other conventions

- Recorder output root: `~/Library/Application Support/Sts2Recorder/sessions/`.
  Never commit trajectories; never modify the game's own save files (copy only).
- Rebuild + reinstall the recorder after any game update:
  `scripts/install_mod.sh` (then re-verify hooks against a fresh decompile —
  see docs/hook-map.md drift addendum for the process).
- Offline tooling lives in `sts2rec/` (uv project): `sessions`, `report`,
  `validate`, `canonical`, `watch`, `pack`.
- Full architecture and format contract: `docs/design.md`. Verified hook map:
  `docs/hook-map.md`.
