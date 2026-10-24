# Fixed-seed benchmark (Ironclad A0, 10 seeds)

`scripts/bench/run_bench.py` plays a fixed seed list through the same live
harness agents use (`scripts/sts2_live.py`), from an identical frozen save, so
policies (stub, LLM, with/without wiki facts) can be compared run-for-run.

## Seed list

`config/bench/ironclad-a0-10seed.json` (`ironclad-a0-10seed-v1`): IRONCLAD,
ascension 0, standard mode, seeds `BENCHA0S01` ... `BENCHA0S10`.
Limits per run: 3000 decisions, 5400 s wall time, 6 consecutive harness/policy
errors (`--max-decisions` / `--max-wall-s` override).

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

The baseline lives outside the repo, e.g. `/workspace/private/bench-baseline/`
(`manifest.json` + `sts2-bench-baseline-v1.tar.gz`); the config pins its
sha256 and the runner refuses a mismatch. The baseline save is a copy of a
worker profile with the `IRONCLAD3_EPOCH` timeline epoch revealed (via the
in-game Timeline), so the card/relic pools are the same for every run.

```bash
python3 scripts/bench/baseline.py verify  [--baseline DIR]
python3 scripts/bench/baseline.py restore --inst /workspace/private/worker-pool-bench/inst1
```

## Seeded start

The runner drives the real menus over the MCP: `menu_select singleplayer` ->
`standard` -> `IRONCLAD` -> `embark` with `"seed": "<SEED>"`. It asserts the
lobby is `standard` mode at the configured ascension (there is no MCP
ascension setter; the baseline profile defaults to A0).

The stock game ignores seeds for standard runs, so the worker MCP carries the
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
| `friction` | `kind` (`not_settled`, `pending_unresolved`, `policy_error`, `harness_crash`), `index`, `message` |
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
* The baseline profile has the IRONCLAD3 reveal (3 relics unlocked), so
  relic pools differ from older un-revealed worker profiles; compare only
  runs from the same baseline sha256.
* Each decision costs ~2-8 s of harness/game settling, so a full stub run is
  minutes, an LLM run dominated by model latency.
