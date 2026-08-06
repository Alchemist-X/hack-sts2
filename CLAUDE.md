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

## Snapshot throttle (perf)

`mods/Sts2Recorder.conf` sets `snapshot_min_interval_ms: 800` (default is 150).
Raised to reduce main-thread snapshot cost during combat. Only throttles the
intra-combat `StateTracker.CombatStateChanged` snapshots; per-action, per-turn,
combat/room-boundary snapshots and the whole events stream are unthrottled, so
no decision-relevant data is lost. `install_mod.sh` does not overwrite this
conf — recreate it after a fresh mods-dir wipe.

TODO (proper fix): move snapshot building off the main thread so intra-combat
snapshots don't hitch even at 150 ms, then drop the throttle back down.

## Backlog: explicit `legal_actions` in the canonical trajectory

The recorded state already contains everything needed to derive the legal action
set — per hand card `can_play` / `unplayable_reason` / `target_type` / `index`,
plus energy, enemies (with `intents`) for targets; `map.next_options` (with
`leads_to` lookahead) for routing; `rewards.items` for reward screens. But
`sts2rec canonical` does not yet emit an explicit `legal_actions` array per step
(hack-balatro's schema has one).

Why it matters: scoring a decision requires the counterfactual set (what else was
available), and agents need an action space. Derive it offline in `sts2rec`
(state → legal_actions) rather than recording it — the state is the source of
truth and this keeps the mod passive.

Known gap to check while implementing: card-reward picks may record only the
CHOSEN card, not the full offered set. If so, the offered set must be recovered
from the `rewards` state snapshot preceding the pick, or the recorder needs a
`card_reward` state trigger.

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
