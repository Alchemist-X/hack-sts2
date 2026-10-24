# Fixed-seed benchmark (Ironclad A0 / A10, 10 seeds)

`scripts/bench/run_bench.py` plays a fixed seed list through the same live
harness agents use (`scripts/sts2_live.py`), from an identical frozen save, so
policies (stub, LLM, with/without wiki facts) can be compared run-for-run.

## Seed list

`config/bench/ironclad-a0-10seed.json` (`ironclad-a0-10seed-v1`): IRONCLAD,
ascension 0, standard mode, seeds `BENCHA0S01` ... `BENCHA0S10`.
Limits per run: 3000 decisions, 5400 s wall time, 6 consecutive harness/policy
errors (`--max-decisions` / `--max-wall-s` override).

`config/bench/ironclad-a10-10seed.json` (`ironclad-a10-10seed-v1`): IRONCLAD,
**ascension 10**, standard mode, the **same** seed strings `BENCHA0S01` ...
`BENCHA0S10` (kept deliberately so A0 and A10 results compare seed-for-seed;
the `A0` in the name is historical), same limits. It only accepts baseline
`v2-a10` (`default_baseline`, so `--baseline` can be omitted).

### Ascension 10 (interim, until the MCP has `start_run(ascension)`)

The MCP has no ascension control, so A10 comes from the save: the standard
lobby starts at `min(preferred_ascension, max_ascension)` of the character
(`StartRunLobby`, logged as `ascension set to preferred: 10`). Baseline
`v2-a10` is v2 with every character's `preferred_ascension` = 10 (nothing
else differs). The embark path is unchanged. The runner verifies the
ascension for every seed: `verify_seed` requires both the game.log
`Embarking on a singleplayer IRONCLAD run. Ascension: 10 Seed: ...` line and
`current_run.save` `ascension` to equal the config `ascension`, else the run
fails with `seed/ascension not verified` (evidence in `run_start.seed_evidence`,
`ascension_verified`). When a native `start_run(character, ascension, seed)`
lands, swap `start_seeded_run` for it and drop the `v2-a10` baseline.

## Frozen save baseline

Every run (including each determinism start) begins with
`baseline.restore(inst)`:

1. stop the instance (refuses to restore under a live game);
2. verify the baseline tarball sha256 against its manifest;
3. replace `<inst>/home/.local/share/SlayTheSpire2` with the tarball contents
   (safe extraction; refuses the human save and any path outside a
   `worker-pool*` directory);
4. re-verify `progress.save` and `settings.save` hashes (settings holds the
   mod/MCP consent and is part of the baseline, never wiped).

The baselines live outside the repo in `/workspace/private/bench-baseline/`
(override with `STS2_BENCH_BASELINE_ROOT`). Pick one with `--baseline v1|v2`
(default **v2**, or the config's `default_baseline`), or pass a baseline dir / manifest file:

| label | manifest | tarball | what it is |
|---|---|---|---|
| `v1` | `manifest.json` | `sts2-bench-baseline-v1.tar.gz` | original worker profile with the `IRONCLAD3_EPOCH` reveal (4/57 epochs, 106 cards, 76 relics, 34 potions) |
| `v2` | `manifest-v2-unlocked.json` | `sts2-bench-baseline-v2-unlocked.tar.gz` | a copy of v1, fully unlocked: 57/57 epochs revealed (incl. `CUSTOM_AND_SEEDS_EPOCH`, daily), all 5 characters, 578 cards, 297 relics, 65 potions, 61 events, 4 acts, max ascension 10 (preferred 0), all FTUEs done, no pending reveals |
| `v2-a10` | `manifest-v2-a10.json` | `sts2-bench-baseline-v2-a10.tar.gz` | v2 with `preferred_ascension` 10 for every character (lobby starts at A10); `make_unlocked_save.py --preferred-ascension 10` on a restored v2; progress.save `3ab4b56c...` |

The config pins every baseline it accepts (`baselines.v1` / `baselines.v2`,
name + sha256) and the runner refuses any other name or a sha mismatch.
`bench.json` records the baseline name and sha; compare only runs from the
same baseline (card/relic/potion pools differ between v1 and v2).

```bash
python3 scripts/bench/baseline.py verify  [--baseline v1|v2|DIR|MANIFEST]
python3 scripts/bench/baseline.py restore --inst /workspace/private/worker-pool-bench/inst1 [--baseline v1]
```

### Rebuilding the unlocked baseline (v2)

`scripts/bench/make_unlocked_save.py` edits a worker profile's
`progress.save` (plain JSON, schema 21, no checksum; it writes
`progress.save.backup` identically because the game falls back to it). It
mirrors the game's own `unlock all` console command and derives every list
from the live game, never from hard-coded ids: it decompiles
`$STS2_GAME_DATA_DIR/sts2.dll` with `ilspycmd` (cached under
`artifacts/knowledge/decompiled-sts2-<sha12>`, git-ignored) and reads
`ModelDb` (character + shared card/relic/potion pools, starting decks and
relics), `EpochModel.EpochIds`, act events, encounters/monsters and the FTUE
ids. `settings.save` (MCP consent) is never touched. It refuses paths outside
a `worker-pool*` dir.

```bash
python3 scripts/bench/make_unlocked_save.py            # print derived counts
python3 scripts/bench/baseline.py restore --inst /workspace/private/worker-pool-bench2/inst1 --baseline v1
python3 scripts/bench/make_unlocked_save.py \
  --saves-dir /workspace/private/worker-pool-bench2/inst1/home/.local/share/SlayTheSpire2/default/1/modded/profile1/saves
# then tar <inst>/home/.local/share/SlayTheSpire2 (minus logs/sentry/shader_cache)
# into the v2 tarball and write manifest-v2-unlocked.json
```

The edit is deterministic (fixed `obtain_date` for newly revealed epochs), so
re-running it on v1 reproduces the same `progress.save` sha256
(`fc61ff05...` for game v0.107.1). Validated in game: loads without a
reset/backup fallback (`Epochs: 57` in game.log), Singleplayer ->
standard/daily/custom enabled, timeline 57/57 revealed with nothing pending,
all characters selectable, lobby ascension 0. Determinism on v2:
`--determinism BENCHA0S01 --starts 2` gave identical Neow (Nutritious Oyster /
Phial Holster / Cursed Pearl), 50-node act-1 map and first fight (Nibbit 44 HP).

`v2-a10` was validated the same way: game.log `ascension set to preferred: 10`
and `Ascension: 10 Seed: BENCHA0S01`, `current_run.save` ascension 10, and
`--determinism BENCHA0S01 --starts 2` identical across starts.

## Seeded start

The runner drives the real menus over the MCP: `menu_select singleplayer` ->
`standard` -> `IRONCLAD` -> `embark` with `"seed": "<SEED>"`. It asserts the
lobby is `standard` mode at the configured ascension (there is no MCP
ascension setter; the baseline profile defaults to A0).

The stock game ignores seeds for standard runs. With v2 the native seed input
is available, but only on the Custom Mode screen (`NCustomRunScreen`, game
mode `custom`, "cannot unlock epochs/achievements"); the MCP does not model
that screen, so it is GUI-only and not a standard run. Therefore, even on v2, the worker MCP carries the
`sts2mcp-seeded-standard-embark` patch (hack-sts2-cloud). It sets the game's
own `NGame.DebugSeedOverride` (the path its AutoSlayer uses) to the
canonicalized seed just before clicking embark, and clears it on unseeded
embarks. The runner then checks what the game actually used: the
`Embarking on a singleplayer IRONCLAD run. Ascension: 0 Seed: <SEED>` line in
the instance `game.log` and `rng.seed` in `current_run.save`. A run whose seed
cannot be verified is recorded as a runner error, never scored.

Determinism check: `run_bench.py --determinism SEED [--starts 2]` restores and
starts the seed N times and compares Neow options, the full act-1 map, the
first combat (enemies, HP, intents) and the opening hand (stub policy up to
the first fight).

## Running

```bash
export STS2_GAME_INSTALL_DIR=/workspace/private/sts2-linux-0.107.1   # STS2_GAME_EXECUTABLE / _DATA_DIR derived
python3 scripts/bench/run_bench.py --instances 1,2,3 --policy stub
python3 scripts/bench/run_bench.py --instances 1,2,3 --policy 'cmd:python3 my_agent.py' --wiki-facts
python3 scripts/bench/score.py /workspace/private/bench-runs/<run> [<run> ...]
```

* `--instances` 1-3 worker instances of `--pool` (default
  `/workspace/private/worker-pool-bench`, ports 15611-15613); seeds are pulled
  from a shared queue. worker-pool-v2 / ports 15601-15603 are refused.
* `--launcher` (or `STS2_BENCH_LAUNCHER`): hack-sts2-cloud
  `scripts/headless_launch.sh`.
* `--wiki-facts` starts the live session with `--wiki-facts`, adding
  `wiki_facts` (FactDB `facts_for_state`, <= 4000 chars) to every
  observation; compare runs with and without it.
* `--mode practice` (default) passes `--allow-unverified-authority`, recorded
  in `run_start.harness`. `--require-verified-authority` disables that.
* Output goes to `/workspace/private/bench-runs/<timestamp>-<policy>[-facts]/`:
  `bench.json`, `results.jsonl`, and per seed `trajectory.jsonl`,
  `result.json`, `live/` (the sts2_live run dir: `observations.jsonl`,
  `decisions.jsonl`, ...). Run data is never committed.

## Policies

`scripts/bench/policies.py`. A policy receives the `sts2_live observe` JSON
(`hash`, `state`, `legal_actions[*].ref`, `decision_context`, optional
`wiki_facts`) plus a context `{seed, index, policy, avoid, last_error,
wiki_facts_enabled}` and returns `{"action": <ref | [refs] | shorthand>,
"reason": "<public rationale>"}`. The runner submits it with
`sts2_live act <action> <reason> <hash>`, so stale frames, selection
confirmation, settle checks, etc. are exactly what an agent experiences.

* `stub`: deterministic floor baseline. Combat: first playable card, else end
  turn. Shop: leave. Otherwise the first legal action. Never uses or discards
  potions. Skips refs in `avoid` (refs that just failed on the same frame).
* `cmd:<command>`: LLM / CLI hook. One process per decision; stdin is
  `{"schema": "sts2-bench-policy-request/v1", "context": {...},
  "observation": {...}}`; the last stdout line must be the reply JSON. Env
  `BENCH_SEED` and `BENCH_DECISION_INDEX` are set.
* `py:<module>:<func>`: in-process `func(observation, context)`.

## Trajectory schema (`trajectory.jsonl`, `sts2-bench-trajectory/v1`)

Every line has `schema` and `type`.

| type | fields |
|---|---|
| `run_start` | `seed`, `instance`, `port`, `started_at`, `policy`, `wiki_facts`, `character`, `ascension`, `benchmark`, `baseline` (restore verification: tarball/progress/settings sha256), `embark` (MCP reply incl. `seed_mechanism`), `seed_evidence` (`log`, `save`, `verified`), `first_state_type`, `harness` |
| `decision` | `index`, `t_wall_s`, `state_ref` (`run_dir`, `observations` file, `decision_hash` -> the full observation in `live/observations.jsonl`), `state_type`, `act`, `floor`, `hp`, `max_hp`, `gold`, `round`, `enemies`, `legal_actions` (refs), `wiki_facts_chars`, `chosen`, `rationale`, `policy`, `outcome`, `latency_s` (`policy`, `act`) |
| `friction` | `kind` (`not_settled`, `pending_unresolved`, `policy_error`, `harness_crash`, `transient_observe_retry`), `index`, `message` |
| `terminal` | the `game_over` frame summary |
| `history` | the game's run-history file name, `win`, `killed_by_encounter` |
| `run_end` | `metrics` (same as `result.json`) |

`outcome`: `ok`, `error` (sts2_live error kind: `stale_hash`,
`unresolvable_action`, `selection_incomplete`, `action_rejected`,
`action_unresolved`, ...), `message`, `steps`, `harness_followups`
(e.g. `auto_confirm_selection`, `treasure_reclick`), `warnings`,
`authority_verified`, `post_hash`, `post_state_type`, `post_hp`.

On `action_unresolved` / `pending_unresolved` the runner reconciles
automatically and records it; failed refs on an unchanged frame go to `avoid`.

## Metrics (`result.json`)

Authoritative outcome fields come from the game's own history file
(`saves/history/<start>.run`, matched by seed and start time,
`source: game_history`); if the run was cut off they fall back to the
trajectory (`source: trajectory`).

* `floor` (map points visited), `act`, `boss_kills` (boss points survived),
  `win`, `boss_entry_hp` (list; `hp_at_boss_entry` = first) computed as
  `current_hp + damage_taken - hp_healed` at the boss point,
  `death_cause` (`killed_by_encounter`/`killed_by_event`, or
  `not_finished:<end_reason>`), `game_run_time_s`;
* `frictions` (counts by error kind) and `friction_total`,
  `harness_followups`, `decisions`, `wall_time_s`, `end_reason`
  (`game_over`, `max_decisions`, `max_wall_s`, `harness_stuck`,
  `policy_error`, `runner_error`), `error`, `seed_evidence`.

`score.py` prints a per-seed table and an aggregate per
(policy, wiki_facts): runs, runner errors, wins/win rate, mean/median floor,
mean act, boss kills, mean HP at boss entry, death causes, frictions per run
(and by kind), mean wall time. `--json` for machine-readable output.

## Known gaps

* Practice mode: rule authority is unverified for this build, so runs pass
  `--allow-unverified-authority` (recorded per run).
* No MCP ascension setter; A0 is asserted from the lobby, not set.
* The seeded embark depends on the hack-sts2-cloud MCP patch; an unpatched
  worker refuses the seeded embark and the runner stops.
* v1 has only the IRONCLAD3 reveal; v2 has everything unlocked, so card,
  relic and potion pools differ between them (and from older worker
  profiles); compare only runs from the same baseline sha256.
* Native seeds (v2) exist only in Custom Mode, which the MCP cannot drive;
  standard seeded runs still need the MCP seed patch.
* Each decision costs ~2-8 s of harness/game settling, so a full stub run is
  minutes, an LLM run dominated by model latency.

### Transient HTTP stalls

A crashed `observe` caused by the game's HTTP endpoint not answering (TimeoutError, connection refused/reset) is retried up to 3 times with 10/20/30 s back-off. Each retry is recorded as a `transient_observe_retry` friction. `observe` is read-only, so this is safe. An `act` is never re-sent. Non-transient crashes still end the seed as `runner_error`.
