# hack-sts2 Design

Passive full-trajectory recorder for **Slay the Spire 2** (Godot 4 .NET, Early Access).
Goal: while a human plays the legally-owned game locally, record **every state and every
action** to versioned, append-only artifacts suitable for building an AI benchmark —
the STS2 counterpart of `hack-balatro`'s real-client trajectory pipeline.

Research basis (2026-07-14): see `docs/research-notes.md`. Data-cooperation agreement
with the developer is in place; recording is local-only, nothing is uploaded.

## Architecture

Two components, producer/analyzer split (balatrollm/balatrobench pattern):

```
┌────────────────────────────── game process ──────────────────────────────┐
│  Slay the Spire 2 (Godot 4 + .NET 9)                                     │
│  official mod loader ── loads ──► Sts2Recorder.dll  [ModInitializer]     │
│    • Harmony postfix patches on human-action funnels  → actions.jsonl    │
│    • CombatHistory tap (draws/damage/energy/…)        → events.jsonl     │
│    • throttled stable-state snapshots (hash-deduped)  → states.jsonl     │
│    • run-end archiver (.run + latest.mcr copies)      → native/          │
└───────────────────────────────────────────────────────────────────────────┘
                    writes to <output_root>/sessions/<run_id>/
┌──────────────────────────── offline tooling ─────────────────────────────┐
│  sts2rec (Python CLI): list / validate / archive-fallback / canonical    │
│  conversion to hack-balatro-style {meta, steps[]} trajectories           │
└───────────────────────────────────────────────────────────────────────────┘
```

Key architectural decisions, and why:

1. **Official mod, not injection.** STS2 ships a first-party mod loader
   (`MegaCrit.Sts2.Core.Modding`, `[ModInitializer]` entry point, consent dialog,
   `mods/` folder). The game even ships `0Harmony.dll` 2.4.2. No proxy DLLs, no
   Lovely-style injectors needed — unlike Balatro.
2. **Event-driven capture, not state-diff inference.** hack-balatro's observer had to
   poll at 5 Hz and *reconstruct* actions by diffing states (lossy: <200 ms inputs
   slipped, round boundaries created phantom events). Here both the human UI and
   programmatic play funnel through the same game methods
   (`ActionQueueSynchronizer.RequestEnqueue(PlayCardAction)`, `PlayerCmd.EndTurn`,
   `NMapScreen.OnMapPointSelectedLocally`, reward/event/rest button handlers), so
   Harmony postfix patches capture the *exact* human action stream.
3. **Reuse STS2MCP's StateBuilder (MIT) as the observation serializer**, stripped of
   its two side effects (shop `OpenInventory()`, treasure `ForceClick()`). It is
   ~2,400 lines of hard-won screen→JSON mapping covering every screen type.
4. **Piggyback native artifacts.** The game already writes floor-level run summaries
   (`saves/history/<start_time>.run`, plain JSON, schema v8) and a deterministic
   binary command replay (`replays/latest.mcr`, overwritten each run). We archive
   both per session: the `.run` file cross-validates our recording; the `.mcr` is a
   free ground-truth action stream if we ever decode it.
5. **Two data channels.** Agent-visible observation (draw pile order hidden, seed
   hidden — matches what STS2MCP exposes to agents) vs privileged ground truth
   (seed, true draw order from `CardDrawnEntry`). Stored distinctly so benchmark
   evals can't leak hidden info.

## Session format (schema_version 1)

```
<output_root>/sessions/<run_id>/          # run_id = <start_unix>-<seed>
  manifest.json      # versions, run meta; finalized at run end (incomplete=true until)
  states.jsonl       # full snapshots at stable decision points, hash-deduped
  actions.jsonl      # hooked human actions
  events.jsonl       # fine-grained combat/run history entries
  native/
    run_history.run  # copy of the game's own .run file (post-run)
    replay.mcr       # copy of replays/latest.mcr (post-run, before next run clobbers it)
```

Every JSONL line shares the envelope `{seq, t, type, ...}`:
`seq` = monotonically increasing per session (single writer thread), `t` = unix epoch
seconds (float). One session = one run (menu time between runs is not recorded).

- `states.jsonl`: `{seq, t, type:"state", trigger:"action"|"poll"|"phase",
  screen:"combat"|"map"|…, hash, state:{…full passive StateBuilder output…}}` —
  `hash` = first 16 hex chars of the SHA-256 of the CANONICAL serialization of
  the state (object keys sorted ordinally at every depth, arrays preserved), so
  hash equality — and therefore snapshot dedup — is independent of the state
  builder's key insertion order; consecutive identical snapshots are
  hash-deduped (not re-written). The stored `state` payload keeps the builder's
  original key order.
- `actions.jsonl`: `{seq, t, type:"action", source:"hook:<PatchId>",
  action:{kind, params:{…}}, status, state_seq:<seq of latest snapshot before
  the action>}` — `status` (recorder ≥ 0.1, optional for older sessions) is the
  GameAction lifecycle: `"committed"` (already-final decisions such as map/shop
  picks) | `"executed"` | `"cancelled"` (enqueued then backed out). `sts2rec`
  accepts both the nested `params` object and the older inline spread.
  `state_seq` is `null` when the action fired before the first snapshot of the
  session (legacy recorders wrote `0` for this; `sts2rec` normalizes 0 → null).
  This is valid data, not a dangling reference: `sts2rec validate` accepts it,
  and `sts2rec canonical` uses the NEXT snapshot after the action as a
  best-effort `state_before`, flagging the step with
  `info.state_before_estimated = true` (`state_before` is null when the session
  has no later snapshot).
- `events.jsonl`: `{seq, t, type:"event", entry:"card_drawn"|"damage_received"|…,
  data:{…}}` — privileged channel (true draw order lives here, never in states).
- `manifest.json`: `{schema_version, recorder_version, game:{version, commit,
  build_id, untested}, platform, profile, run:{seed, character, ascension,
  game_mode, start_time}, result:{win, abandoned, end_time}|null, counts,
  incomplete, part, degraded_hooks, mods}` — `part` (optional, default 1):
  resumed runs recorded across game restarts open a new session directory with
  a `-part2`/`-part3`… suffix on the run_id and carry the part number here.
  `seq` is session-global and strictly monotonic across all three streams.
  The canonical trajectory surfaces `status` as `steps[].info.action_status`
  (when recorded) and `part` as `meta.part`.

Validation contract for crash-terminated sessions: the recorder flushes all
JSONL streams before every manifest rewrite, but between rewrites the OS may
persist more lines than the last manifest checkpoint recorded (and a hard crash
can truncate the final line). Therefore `sts2rec validate` treats manifest
count mismatches as **errors only when `incomplete=false`** (cleanly finalized —
counts must match exactly); when `incomplete=true` the manifest is a periodic
checkpoint and count mismatches degrade to **warnings**, preserving the
crash-salvage story. Line-level integrity violations (malformed/truncated
lines, seq regressions, dangling `state_seq` references) remain errors in both
cases. Each session directory also contains a zero-byte `.claim` marker: it is
created with O_EXCL semantics by the recorder to atomically claim the directory
(two concurrent `Begin()` calls with the same run id get distinct `-partN`
directories), and tooling should ignore it.

Rationale: append-only JSONL per stream (NLE/MineRL/BASALT consensus + STS2MCP
issue #91's proposed shape), raw-fidelity capture with derived views generated
offline. Uncompressed on disk (sessions are MBs, not GBs); `sts2rec` can zstd-pack
finished sessions for distribution.

## Canonical trajectory (offline, derived)

`sts2rec canonical <session>` merges the three streams into the same shape
hack-balatro standardized (`env/canonical_trajectory.py` there):

```json
{"meta": {"source": "sts2-real-client", "seed": "...", "game_version": "...",
          "result": {...} | null, ...},
 "prelude_events": [...],
 "steps": [{"step_idx": 0, "ts": ..., "state_before": {...}, "action": {"type": "...",
            "params": {...}}, "state_after": {...}, "reward": ..., "terminal": false,
            "info": {"events": [...]}}]}
```

Events are partitioned by action boundaries: step *i* gets exactly the events
with `action_i.seq < seq < action_{i+1}.seq` (unbounded above for the final
step), and events before the first action land in top-level `prelude_events` —
every event appears in exactly one place. `meta.result` always mirrors the
manifest result (or null), so zero-action sessions keep their outcome.

Unlike hack-balatro's observer, `action` here is exact (hooked), never
`reconstructed: true`.

## Version-drift policy (Early Access reality)

STS2 moved v0.98.1 → v0.108.0 in 4 months and updates broke STS2MCP twice
(v0.103, v0.107 — upstream issue #114). Therefore:

- The mod reads `release_info.json` at startup and stamps `game.version/commit`
  plus Steam `build_id` into every manifest.
- `KNOWN_GOOD_VERSIONS` allowlist in the mod: unknown version ⇒ still record, but
  set `manifest.game.untested = true` and log a loud warning in-game log.
- Every hook is applied individually inside try/catch: a missing method after a
  game update disables that one hook, records `manifest.degraded_hooks[]`, and the
  snapshot/poll fallback keeps the session usable (graceful degradation to
  hack-balatro-observer fidelity instead of total loss).
- The installed game build is pinned per machine; `scripts/install_mod.sh` rebuilds
  against whatever `sts2.dll` is present and records its hash.

## Scope of v1

- Singleplayer only (mod hard-disables recording in multiplayer runs; MP replay
  sync + mod-mismatch checks are not worth the risk — STS2MCP marks its own MP
  support beta).
- macOS-first (this machine), but no platform-specific code paths outside
  `paths.py` / csproj probing — the DLL is platform-agnostic.
- No screenshots in v1 (STS2 is not deterministic-replayable from our side yet;
  revisit after .mcr decoding).
- HTTP API: none. This mod is write-only-to-disk by design. Live inspection =
  `sts2rec watch` tailing the JSONL. (Run STS2MCP alongside if an agent needs to
  *play*; the two mods are independent.)

## Provenance & licensing

- `mod/src/StateBuilder*.cs` and parts of `Helpers` are derived from
  [STS2MCP](https://github.com/Gennadiyev/STS2MCP) (MIT, © 2026 Yikun Ji
  (Kunologist)) — notice retained in `LICENSE` and file headers.
- This repo: MIT.
