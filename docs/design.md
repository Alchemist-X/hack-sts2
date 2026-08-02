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
│  conversion to hack-balatro-style {meta, steps[]} trajectories, plus     │
│  report (markdown walkthrough) and pack (gzip old sessions)              │
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
  states.jsonl[.gz]  # full snapshots at stable decision points, hash-deduped
  actions.jsonl[.gz] # hooked human actions
  events.jsonl[.gz]  # fine-grained combat/run history entries
  native/
    run_history.run  # copy of the game's own .run file (post-run)
    replay.mcr       # copy of replays/latest.mcr (post-run, before next run clobbers it)
```

Streams are plain `.jsonl` while a run is live and become `.jsonl.gz` when the
session finalizes cleanly (see "Compression" below); `manifest.json`, `native/`
and `recorder.log` always stay plain.

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
  incomplete, part, compression, perf, degraded_hooks, mods}` — `part`
  (optional, default 1): resumed runs recorded across game restarts open a new
  session directory with a `-part2`/`-part3`… suffix on the run_id and carry
  the part number here. `compression` (recorder ≥ 0.2): `"gz"` when the
  streams were compressed at completion (or later by `sts2rec pack`), `null`
  for plain streams. `perf` (recorder ≥ 0.2, see "Snapshot throttling & perf
  counters"): `{snapshot_build_ms:{count, avg, max}, bytes_written:{states,
  actions, events}}`, refreshed on every Flush/Complete manifest rewrite;
  `bytes_written` counts uncompressed JSONL bytes.
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
offline. Live streams stay uncompressed for appendability and crash salvage;
finished sessions are gzip-packed (next section).

## Compression (gzip-at-complete)

When `Complete(result)` finalizes a session the recorder closes the three
JSONL streams, compresses each to `<stream>.jsonl.gz`
(`System.IO.Compression.GZipStream`, `CompressionLevel.Optimal`), decompresses
the result and byte-compares it against the original, and deletes the plain
files **only after every stream round-trips byte-identically**. On any failure
(write error, verification mismatch) the partial `.gz` files are removed, the
plain files are kept, the error is logged through the session's error channel
(`recorder.log`), and the manifest records `compression: null`. `manifest.json`,
`native/` and `recorder.log` are never compressed.

- **Why gzip, not Brotli**: Python must read sessions with zero third-party
  dependencies (repo policy — `sts2rec` is stdlib-only), and CPython has no
  stdlib Brotli. gzip is stdlib on both sides (`GZipStream` / `gzip`); Brotli's
  ~15% better ratio on JSONL does not beat keeping the toolchain
  dependency-free. Measured on the FakeGame synthetic session: 66% saved
  (2478 B → 836 B); real sessions with large repeated state payloads compress
  substantially better.
- **Crash-terminated sessions stay plain by design**: compression only runs on
  the clean `Complete()` path, so a session that never finalized remains
  directly greppable/salvageable plain JSONL. `sts2rec pack` (below) can
  compress it later, after salvage.
- **Post-Complete records are dropped**: once the streams are frozen and
  compressed, a late-firing game handler (between `OnEnded` and `CleanUp`)
  must not reopen them — such records are counted and the first drop leaves a
  rate-limited `recorder.log` notice. The finalized manifest counts stay
  authoritative.
- **Python reads both forms transparently**: `iter_jsonl` accepts `.jsonl` and
  `.jsonl.gz` (given a plain path whose file is missing it reads the `.gz`
  twin), and `validate` / `canonical` / `watch` / `report` resolve each stream
  via `resolve_stream_path`, preferring `.gz` when both exist (the compressed
  copy is the byte-verified one). `watch` treats a `.gz` stream as immutable
  and emits it once; if a live session compresses mid-watch, already-emitted
  records are skipped by count.
- `sts2rec pack <session> | --all [--root R] [--force]` applies the same
  compress → verify → delete semantics to old plain sessions offline and sets
  `manifest.compression = "gz"`. Incomplete sessions are refused without
  `--force` (never pack while the game is writing). `sts2rec sessions` shows a
  SIZE column and a COMP marker (`gz` / `-`).

## Snapshot throttling & perf counters

`StateTracker.CombatStateChanged` can burst many times per frame during action
resolution. Snapshot builds are the recorder's main CPU cost, so bursts are
**trailing-coalesced** (Core `SnapshotThrottle`, unit-tested without Godot):

- Window = `snapshot_min_interval_ms` from `Sts2Recorder.conf` (default 150;
  0 disables the throttle).
- A throttled request outside the window is taken immediately. Inside the
  window it is **never dropped**: the first one schedules exactly ONE trailing
  snapshot via the existing main-thread ProcessFrame pump (re-deferred
  frame-by-frame until the window expires); later requests inside the window
  coalesce into it. The last state of a burst is therefore always recorded —
  an action can at worst reference a snapshot ≤ window ms stale, and the
  unconditional post-action snapshot still captures the settled state.
- **Bypass list** (unconditional, throttle-free; they still reset the window):
  the RunStarted header snapshot, CombatSetUp, TurnStarted/TurnEnded,
  CombatEnded/CombatWon, RoomEntered/RoomExited, and the post-action deferred
  snapshot. Only the CombatStateChanged poll path is throttled.

Perf counters accumulate per session in Core and are written to the manifest
`perf` object on every Flush/Complete: `snapshot_build_ms` (count/avg/max of
the PassiveStateBuilder wall time per build, including builds whose snapshot
hash-deduped) and `bytes_written` (uncompressed bytes appended per stream).

## Run reports

`sts2rec report <session> [-o out.md]` renders a human-readable markdown
walkthrough (also printed to stdout): header (character, seed, ascension,
result, duration, versions), per-floor timeline (from `room_entered` /
`floor_summary` events + map/shop/rest actions), per-combat summaries
(enemies, turns, cards played in order — cancelled plays excluded — damage
dealt/taken, HP/gold from the first post-combat snapshot), acquisitions and
deck evolution (from `*_obtained` / card-removal entries), and a counts +
storage footprint table (records, on-disk vs raw bytes, compression format
per stream). It is deliberately lenient: live/incomplete and crash-terminated
sessions render with `(no … recorded)` placeholders and a partial-data note
instead of failing, and unrecognized entry names simply do not populate the
heuristic sections.

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

## Headless text environments

N isolated processes (`scripts/headless_provision.sh` + `headless_launch.sh`,
`sts2rec.env.Sts2Env`/`launch_pool`) share one ~172 MiB overlay runtime. Its
executable is an APFS clonefile and immutable Resources/Frameworks are symlinked
from the configured game installation. Each worker owns
only HOME, port, PID, log and trajectory directories. The sandbox MCP reads
`STS2_MCP_PORT`; the recorder reads `STS2_RECORDER_*`, so per-worker app/mod
clones are unnecessary. Launch is `--headless --force-steam=off`; policies use
`Sts2TextEnv`/`VectorSts2TextEnv` observations and legal actions, never pixels.

Verified on macOS arm64 v0.107.1: two concurrent workers bound ports 15601 and
15602 from the same runtime, used isolated Godot user-data directories, exposed
independent JSON states, and remained alive through a soak check at roughly
383 MiB RSS each at the main menu. See `docs/text-environment.md` for commands, training API,
information boundaries, and the explicit mid-combat checkpoint limitation.

## Provenance & licensing

- `mod/src/StateBuilder*.cs` and parts of `Helpers` are derived from
  [STS2MCP](https://github.com/Gennadiyev/STS2MCP) (MIT, © 2026 Yikun Ji
  (Kunologist)) — notice retained in `LICENSE` and file headers.
- This repo: MIT.
