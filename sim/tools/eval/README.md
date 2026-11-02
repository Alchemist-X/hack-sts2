# sts2eval: concurrent evaluation runner for Sts2Sim

Plays fixed seed sets of full runs against the HTTP play server (`sim/Sts2Sim.Server`, see
`sim/PLAY.md`), dozens of games in parallel, with a uniform-random policy or a Claude model called
directly through the official Anthropic Python SDK. Every game is transcribed step by step for audit,
and the run ends with an aggregated report (win rate, floors, steps, illegal actions, crashes, tokens,
cost, wall time).

## Install

```bash
dotnet build sim/Sts2Sim.sln -c Release        # builds the server dll that --spawn launches
uv sync --project sim/tools/eval               # Python >= 3.11, anthropic SDK, pytest (dev group)
```

## Run

Random smoke (no API key needed; random play usually dies in act 1, the pipeline is what is tested):

```bash
uv run --project sim/tools/eval sts2eval run --seedset a0-s10 --policy random --concurrency 10 --spawn \
    --out tmp/eval/smoke-random
```

Claude baseline (Sonnet 5.5, low effort, 10 games in flight):

```bash
ANTHROPIC_API_KEY=... uv run --project sim/tools/eval sts2eval run --seedset a0-s10 --policy claude \
    --model claude-sonnet-5-5 --effort low --concurrency 10 --spawn
```

Useful flags:

| Flag | Default | Meaning |
| --- | --- | --- |
| `--seedset NAME\|ID\|PATH` | required | frozen sets (protocol v0.2): `std-a0-v1` = `a0-s20`, `std-a10-v1` = `a10-s20` (official, 20 games each), `smoke-a0-v1` = `a0-s10` (smoke); or a JSON path |
| `--policy random\|claude` | `random` | |
| `--model` | `claude-sonnet-5-5` | also `claude-haiku-5-5`, `claude-opus-5-5`, `claude-fable-5-1` |
| `--effort low\|medium\|high\|xhigh\|max` | model default | sent as `output_config.effort` |
| `--thinking adaptive\|off` | `adaptive` | `off` = `{"type":"between_tools"}` on Sonnet 5.5, `{"type":"disabled"}` on Haiku 5.5 (both need effort <= high); Opus 5.5 and Fable 5.1 cannot turn thinking off (rejected) |
| `--max-tokens` | 1024 | per call; thinking counts toward it. Watch `max_tokens stops` in the report; raise it at higher effort |
| `--concurrency N` | 10 | games in flight (one shared `AsyncAnthropic`, `max_retries=3`) |
| `--rps-limit R` | off | at most R API request starts per second |
| `--step-cap N` | 1200 | accepted steps per game, then `capped` |
| `--loop-repeat K` | 6 | when one observation has come up K times on a floor, the next prompt gets a one-time loop warning (once per floor; 0 = off) |
| `--floor-step-cap N` | 80 | non-combat steps per floor (rewards, shop, event, map, rest, prompts outside combat); the next one ends the game as `stuck` before any API call (0 = off) |
| `--reveal-moves` | off | create games with `"reveal_moves": true` (intents and the enemy-turn log name the enemy's move; sim/PLAY.md "Hidden information"). Off is the game's view; the runner always sends the flag, so the server's own default does not matter |
| `--game-minutes M` | 60 | wall clock per game, then `capped` |
| `--step-timeout S` | 300 | seconds per decision incl. SDK retries; a timeout counts as an API error |
| `--max-cost-usd X` | off | stop the run once spend reaches X (`aborted: budget`) |
| `--max-minutes M` | off | stop the run after M minutes (`aborted: time`) |
| `--url URL` / `--spawn` | `http://127.0.0.1:8765` | use a running server, or launch the built dll on a free port (stopped at the end, also on Ctrl-C) |
| `--out DIR` | `tmp/eval/<run_id>` | `run_id = <seedset>-<policy>-<model>-<YYYYmmdd-HHMM>` |
| `--resume` | off | skip games whose summary exists and is not `aborted` |
| `--api-key-file PATH` | | read once, never logged; otherwise the SDK reads `ANTHROPIC_API_KEY` |
| `--prices PATH` | `sts2eval/prices.json` | price table |

Re-aggregate a run directory: `uv run --project sim/tools/eval sts2eval report tmp/eval/<run_id>`. It always
recomputes the protocol v0.2 progress; for runs written before v0.2 it rebuilds the per-act room counts from the
transcripts (`act`/`floor` of each step and the act-map grid of Map-phase `obs_text`).

Seed sets: `seedsets/*.json` carry `id`, `version`, `role`, `frozen` and `sha256` (sha256 of the compact sorted-key JSON
of `{ascension, content, games[{character, seed}]}`); `seedsets.load` rejects a file whose content no longer matches its
checksum. Frozen sets never change: publish a new id instead.

Exit codes: 0 done, 2 bad arguments / server missing, 3 run aborted (budget, time, fatal API error,
server down, 25 API errors in a row), 130 Ctrl-C (summaries so far are kept; `--resume` continues).

Tests: `uv run --project sim/tools/eval pytest -q sim/tools/eval` (the integration tests spawn the
server and are skipped when the Release dll is not built). Without the path argument, pytest run from
the repo root collects every test in the repository.

## How a step works

1. The observation `text` from the server (header, deck, enemies, hand, the screen, the legal
   tokens; rules text once loaded) plus `Recent history` (last 6 actions, last 12 log lines) is the
   user message. The system prompt is static (`sts2eval/prompt.py`, about 1.4k tokens: goal, phases,
   full token grammar, strategy, keyword glossary) with `cache_control: ephemeral` on its block, above
   the 512-token minimum cacheable prefix of the 5.x models. Each step is one stateless request.
   No temperature / top_p / top_k, no prefill, no server-side fallbacks.
2. The reply's first line is the token (an optional `TOKEN:` prefix, backticks, bold and quotes are
   stripped), normalised with the server's rules (case and whitespace insensitive, `end turn` = `end`,
   `play 0 1` = `play 0 -> 1`, `take` / `next` / `leave` / `proceed` / `continue` aliases) and checked
   against `legal_actions`.
3. Not legal or unparseable: re-ask once with the server's error wording and the legal list. Still
   not legal: a seeded random legal token is played and the game's `illegal` counter goes up.
4. `stop_reason: refusal`, an API error after the SDK's retries, or a step timeout: no re-ask, a
   random legal token is played (`refusals` / `api_errors` / `timeouts`). The game always continues.
   HTTP 401/402/403/404 abort the run (every later call would fail the same way).
5. `stop_reason: max_tokens`: counted; the token is used if the first line was finished before the
   cut-off, otherwise it is treated as unparseable (step 3).
6. Loop guard (`runner.LoopGuard`), before each decision: per floor it counts every
   `(phase, sha256(obs_text))` and the non-combat steps. The first time one observation comes up
   `--loop-repeat` times on a floor, that step's prompt gets an `== Loop warning` section, once per
   floor: `The last K observations were identical: you are not making progress.` plus, on the
   Rewards / CardReward screens, `Skipping a card reward keeps the offer; use "proceed" to leave the
   rewards screen.` (elsewhere: choose an action that changes the state). A floor that would take more
   than `--floor-step-cap` non-combat steps ends the game as `stuck` (truncated, reward 0). The system
   prompt also states the rule: `skip` keeps the card reward on the rewards screen, `proceed` leaves,
   reward -> skip -> reward chains change nothing (the Sonnet A0 baseline looped 700 steps on it).

The random policy uses the same loop with a per-game seeded rng (sha256 of `CHARACTER:SEED`), so a
random run is reproducible; the Claude policy differs only in the client object.

## Output layout (`tmp/eval/<run_id>/`, git-ignored)

| File | Content |
| --- | --- |
| `config.json` | `protocol_version`, seed set (and `seedset_id`/`_version`/`_sha256`/`_frozen`), policy, model, effort, thinking (and the exact `thinking` field sent), max_tokens, prompt hash, `reveal_moves`, server URL and `/health` version, repo git sha, price table, limits (incl. `loop_repeat`, `floor_step_cap`), concurrency |
| `run.json` | wall seconds, abort reason, API calls, spend |
| `server.log`, `crashes.jsonl` | spawned server output and its crash repros (`--spawn` only) |
| `games/<CHARACTER>-<SEED>.jsonl` | `start` record, one `step` record per accepted step, `end` record |
| `games/<CHARACTER>-<SEED>.summary.json` | per-game result and counters (below) |
| `report.json`, `report.md` | the aggregate |

Step record: `step, phase, act, floor, turn, hp, max_hp, obs_text, legal, token` (the token sent),
`fallback` (null / illegal / api_error / refusal / timeout), `retries`, `attempts[]` (raw reply,
stop_reason, error, usage {input, cache_creation_input, cache_read_input, output}, latency_ms,
cost_usd, request_id, exception, refusal_category), `log` (server log lines of the step), `loop_hint`
(only on the step whose prompt carried the loop warning), `map_row` and `act_rooms` (Map-phase steps: the current
room's row or null, and the act map's max row + 1). The `start` record also has `reveal_moves`; the `end` record has
`act`, `phase`, `progress`, `stuck` and `loop_hints`.

Summary: `result` (`win`, `loss`, `capped`, `stuck`, `crashed`, `aborted`, `error`), `cap_reason`,
`abort_reason`, `error`, `stuck` (`{phase, floor, noncombat_steps}` of a stuck game, else null),
`truncated` (capped or stuck), `reward` (1 for a win, else 0), `reveal_moves`, `floor`, `act`,
`progress_pct` (floor / 48 or 49, v0.1), `progress` (v0.2), `rooms_reached`, `rooms_cleared`, `act_rooms_current`,
`act_rooms_source` (`map` / `default` / `win`), `act_start_floor` and `act_rooms` (per act), `final_phase`,
`protocol_version`, `steps`, `turns`, `illegal`, `illegal_replies`, `retries`, `api_errors`, `refusals`,
`timeouts`, `max_tokens_stops`, `fallbacks`, `api_calls`, `late_line_tokens`, `loop_hints`, `tokens`,
`cost_usd`, `seconds`, `final_hp`, `max_hp`, `death` (floor, act, room type of the last travel, enemy
ids of the last combat), `history_tokens` (the server's canonical token list: replay with
`POST /runs` + the same seed and these tokens), `history_steps`, `replayable` (token list length =
accepted steps), `server_version`.

## Metrics

Headline (protocol v0.2, `docs/sts2-bench-protocol-v0.2-2026-11-01.md`): **progress** mean with a 95% interval and
**win rate** with a Wilson interval.

- **progress** (v0.2, `sts2eval/progress.py`): 1.0 for a win, else `((act - 1) + rooms_cleared / L) / 3`, where `L` is the
  act's room count (its map's max row + 1: 17 / 16 / 15 at A0, 16 for act 3 at A10+ with the second boss, 18 for a
  GOLDEN_COMPASS golden path), `rooms_reached = floor - floor at the act's first observation` (act 1: `floor`) and
  `rooms_cleared = rooms_reached - 1` (the room the run ended in is not cleared). Each act weighs 1/3. The interval is
  mean +- 1.96 sd / sqrt(n), clipped to [0, 1].
- **win rate**: wins / games, with a Wilson 95% interval (n = 10 is wide: 0 wins -> [0%, 27.8%]).
- **floor**: total floor reached (1-based, as in the observation). A full run is 48 floors at A0
  and 49 at A10; `floor / 48` (49 at A10) is kept as the secondary v0.1 progress column.
- **act reached**: distribution of the act at the end of the game.
- **steps**: accepted actions (tokens in the server history); **turns**: combat turns seen.
- **illegal actions per game**: steps where the reply and its one re-ask were both not legal (a random
  token was played). `illegal_replies` counts every rejected reply, `retries` every re-ask.
- **crashes**: games whose server step returned HTTP 500 (repro in `crashes.jsonl`).
- **stuck games**: games ended by `--floor-step-cap` (a loop outside combat), counted apart from
  `capped`; the per-game cause reads `stuck: <phase> floor N`. **loop hints**: loop warnings given
  (at most one per floor and game).
- **api errors / refusals / timeouts / max_tokens stops / random fallbacks**: as defined above.
- **tokens**: sums of the SDK `usage` fields over every call (re-asks included). `output` includes
  thinking tokens.
- **cost**: per call `(input * in + cache_creation_input * cache_write + cache_read_input * cache_read
  + output * out) / 1e6` with the per-MTok prices of `prices.json` (recorded in `config.json`).
- **minutes per game**, **total wall time** (the whole run, with games in parallel).

## Cost estimate

Per step at Sonnet 5.5 ($2 in, $10 out, $0.20 cache read): system prompt about 1.4k cached tokens
(`1400 * 0.20 / 1e6 = $0.0003`), observation plus history about 500-1000 uncached tokens
(`$0.001-0.002`, more once rules text is in the observation), output about 100-500 tokens including
low-effort thinking (`$0.001-0.005`): roughly **$0.003-0.007 per step**. A game costs
`steps * per-step`: random-like early deaths take ~50 steps, a run into act 2 a few hundred, a full
run ~800-1200. Budget for `games * expected steps * $0.005` and set `--max-cost-usd` accordingly
(for example `--max-cost-usd 30` for a first a0-s10 Sonnet baseline). The report gives the measured
per-game cost; Haiku 5.5 is ~20x cheaper, Opus 5.5 ~2x and Fable 5.1 ~5x more expensive.
