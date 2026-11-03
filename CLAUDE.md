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

Recorder 0.3.1 (commit fa86de0, 2026-11-02) carries the main-thread lag fix:
lazy popup probe, streaming hash + dedup-before-serialize, burst coalescing of
phase/action snapshots (`burst_min_interval_ms`, default 100), async heartbeat
flush off the main thread, `sp_checksum_enabled` (default off), and a
`recorder_config` event per part recording the effective settings. Snapshot
building itself still runs on the main thread; `snapshot_build_ms` now covers
build + hash + serialize, so it is not directly comparable with <=0.3.0 parts
(0.3.0 baseline, build only: avg 7.9 ms / max 31 ms).

`mods/Sts2Recorder.conf` still sets `snapshot_min_interval_ms: 800` (default
150). It only throttles the intra-combat `StateTracker.CombatStateChanged` poll
snapshots; per-action, per-turn, combat/room-boundary snapshots and the whole
events stream are unthrottled (coalesced inside the burst window), so no
decision-relevant data is lost. `install_mod.sh` does not overwrite this conf
— recreate it after a fresh mods-dir wipe.

TODO: after a real 0.3.1 session, compare its manifest `perf` block with the
0.3.0 baseline; if build cost is well under a frame, step the throttle back
toward 150. Lesson: the first version of this fix sat uncommitted in a worktree
for four months — commit perf work on the branch as soon as the gates pass.

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

## Mainline: Route C — source-ported rules simulator (since 2026-10-28)

The A10 training mainline is a headless, clonable C# re-implementation of the
game's rules kernel, ported from the decompiled tree (the game's author has
given written permission; this is an academic research project). Simulator
code: `sim/`. Plan and gates: `docs/p0-sim-port-plan-2026-10-28.md`.
Rationale: `docs/a10-first-principles-review-2026-10-27.md`. The real engine
remains oracle + exam hall only. Decompile trees stay git-ignored; ported
source under `sim/` is committed.

## Agent orchestration: Fable orchestrates, Opus executes (ultracode)

To balance personal-plan economics with peak intelligence and parallel
throughput, and to ride out the 5-hour usage cap without losing session
cache:

1. Engineering work in this repo runs under the ultracode execution shell
   (the Workflow tool), with the main session on Fable as orchestrator.
2. Hard rule (harness/hook annotation): every subagent dispatched by
   ultracode — every Workflow `agent()` call and every Agent tool call —
   MUST set `model: 'opus'` (Opus 5.5). Subagents never run on Fable. (A
   `fork` inherits the parent model by design — do not use forks for
   delegated execution work.)
3. Division of labor: Fable does precise orchestration — briefs, synthesis,
   final review. Opus does cost-effective execution — reading, porting,
   tests, verification.
