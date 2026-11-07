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

## Decision data: legal actions and pre-choice state (recorder 0.4.0)

- `sts2rec canonical` already emits `info.legal_actions` per step (derived offline from
  `state_before` in `sts2rec/legal_actions.py`; the mod stays passive), plus
  `info.decision_audit`; `sts2rec validate` warns on decision-coverage issues.
- The card-reward offered set is recorded since 0.3.0 (rewards state + `reward_taken.offer`);
  since 0.4.0 decisions reference the pinned pre-choice snapshot (`state_seq`,
  `params._decision`), with full candidate lists and `choice_index`.
- `mods/Sts2Recorder.conf` key `decision_capture_combat_inputs` (default `false`): when on,
  manual card play / end turn / in-combat potion use also take a synchronous pre-input
  snapshot (~8 ms main thread per input). Off = 0.3.1 behaviour for combat inputs.
  Non-combat decision captures are always on.
- Acceptance TODO before relying on 0.4.0 data: real-machine p95/p99 of the `decision:*`
  captures on an isolated instance (not the user's install), long-deck scroll, reroll, event
  sub-selection; rerun the E3 gates, because `state_seq` now points at the pre-choice snapshot.
  Details: `docs/recorder-decision-capture-2026-10-16.md`.

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

## Before recording human runs

- Run `scripts/recording_preflight.sh` (read-only); record only when it exits 0.
- The game must be on v0.111.0 (the only version `sim/` implements); if not, ask the user to switch the Steam branch (agents never change the install).
- mods/ holds only Sts2Recorder (STS2_MCP stashed per the rule above, no QuickSL), with
  `decision_capture_combat_inputs: true` in `mods/Sts2Recorder.conf`.
