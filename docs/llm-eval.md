# Multi-model LLM evaluation

Several LLMs play the fixed-seed benchmark (`docs/benchmark.md`) on the same
seeds. Each model sees the same compact text view of the game and gets the
same harness. Three new files make up the path, and the harness changes are
minimal:

| file | role |
|---|---|
| `scripts/bench/text_state.py` | Deterministic, complete text encoding of one observation, plus a token-measurement CLI |
| `scripts/bench/llm_policy.py` | `py:llm_policy:policy` for run_bench: an OpenAI-compatible chat client (default xAI) with the model as a parameter, JSON-action validation, retries, a timeout and per-decision token, latency and $ telemetry |
| `scripts/bench/compare_models.py` | Runs the same seeds across N models in parallel and writes `summary.md` / `summary.json` |

Hooks: `policies.decide` passes a reply's `llm` dict through, and
`run_bench` writes it on the trajectory `decision` record.

## Text state

`python3 scripts/bench/text_state.py encode <observations.jsonl|observe.json> [--index N] [--facts]`

The output has these sections:
- **Header and status:** a header line, then you (HP, block, energy, stars, gold), powers, relics, potions and piles (grouped as `Strike x4`).
- **Hand:** one line per card, `H# Name [cost] Type/->Target: description`. An `(engine: dmg @cid=N)` suffix appears only when the engine preview differs from the card text.
- **Enemies:** HP, block, the intent with its total, and powers.
- **Screen block:** map rows with edges, plus the "Next" options; rewards, card choices with selection limits, shop prices and affordability, event options, rest site, treasure, game over.
- **Legal actions:** a numbered list. The model answers with the number. Multi-select screens get an explicit "list of k numbers" hint.
- **Glossary:** keywords and powers, deduplicated.
- **Optional:** `wiki_facts`.

Any field the encoder does not know is appended as compact JSON under
`Other`, so nothing is silently dropped. Ids, model ids, icon file names and
duplicate keyword lists are removed.

Token savings were measured with tiktoken `o200k_base` on 4649 real
observations from two full 10-seed stub benchmark runs:
`python3 scripts/bench/text_state.py measure <run>/*/live/observations.jsonl`.
Without tiktoken it falls back to chars/4.

| screen | n | observe JSON tok | text tok | ratio |
|---|---:|---:|---:|---:|
| **all** | 4649 | 2557 | **454** (median 446, max 1097) | **0.18 (−82%)** |
| monster | 2821 | 2849 | 469 | 0.165 |
| elite | 380 | 3935 | 661 | 0.168 |
| boss | 188 | 4029 | 707 | 0.176 |
| map | 288 | 2649 | 575 | 0.217 |
| card_select | 53 | 1895 | 411 | 0.217 |
| card_reward | 140 | 1057 | 303 | 0.286 |
| rewards | 480 | 680 | 199 | 0.292 |
| event | 207 | 784 | 226 | 0.288 |
| shop | 16 | 2414 | 744 | 0.308 |
| rest_site | 48 | 881 | 275 | 0.312 |

For comparison, the earlier hand-built model packets
(`llm-runs/*/model-packets.jsonl`) averaged about 6.5 kB, roughly 1.6k tokens.
A full run of about 400 decisions, with the system prompt and 8 lines of
history, costs about 0.25M input tokens per model per seed.

## LLM policy

The policy is configured through environment variables:

| var | default | meaning |
|---|---|---|
| `XAI_API_KEY` | required | API key. It is sent only in the `Authorization` header and is never printed or logged; errors are redacted. |
| `LLM_MODEL` | `grok-4.3` | model id (`--list-models` queries `GET /v1/models`) |
| `LLM_BASE_URL` | `https://api.x.ai/v1` | any OpenAI-compatible base URL |
| `LLM_API_KEY_ENV` | `XAI_API_KEY` | name of the env var that holds the key |
| `LLM_TIMEOUT_S` | 120 | per HTTP request |
| `LLM_MAX_RETRIES` | 2 | re-asks after an invalid answer; the validation error is sent back to the model |
| `LLM_HTTP_RETRIES` | 3 | backoff on 408/409/429/5xx and network errors |
| `LLM_REASONING_EFFORT` | unset | `low` / `medium` / `high` (sent as `reasoning_effort`) |
| `LLM_MAX_TOKENS`, `LLM_TEMPERATURE` | unset | passed through |
| `LLM_HISTORY` | 8 | recent decisions (action and reason) shown per seed |
| `LLM_PRICES_JSON` | built-in | `{"model": [in, cached, out]}` in $/1M tokens |

The model must reply with `{"action": <n>, "reason": "<short>"}`, or with
`[n1, n2, ...]` on multi-select screens. On an invalid answer the policy asks
again. If every attempt fails it raises `LLMPolicyError` (a `PolicyError`),
which run_bench records as a `policy_error` friction. The tokens spent on the
failed decision are carried into the next decision's `llm.carried_from_failed`.

Each decision record gets `llm: {model, served_model, attempts,
validation_errors, latency_s, prompt_tokens, cached_tokens,
completion_tokens, reasoning_tokens, output_tokens, cost_usd}`. Cost comes
from xAI's `usage.cost_in_usd_ticks` (1e-10 USD) when the API returns it.
Otherwise it is computed from `PRICES`. Reasoning tokens are billed as output.

Prefer `py:llm_policy:policy`. A `cmd:` policy works too, but it starts one
process per decision and therefore loses the recent-decision history.

## Pure text vs a headless simulator (v0.107.1)

Live timing over 1715 stub decisions on the headless worker pool:

| | per decision |
|---|---|
| mean | 4.48 s |
| median | 3.12 s |
| observe | ~0.8 s (0.5 s quiet window) |
| play_card | 2.6 s |
| end_turn | 9.1 s |
| map | 5.2 s |
| rewards/events | ~1.7 s |
| rest | 4.0 s |

The HTTP request itself takes 0.045 s. Nearly all of the time is game
animation plus settlement quiet windows. An LLM adds 1–20 s per call, so with
LLMs the live game costs roughly 2× the pure model time.

A real-engine headless port (tfriedel/spire-mentor, Rust, v0.107.1) is
reported at about 10 ms per action, roughly 300× faster, but it has to be
parity-checked.

**Recommendation.** Use pure text over the live MCP game now. It is the
authoritative engine and the encoder is engine-agnostic. Cut the animation
cost with the MCP's instant fast mode and shorter quiet windows.

For cheap multi-model sweeps, or anything RL-scale, adapt an existing
headless engine rather than writing a new one:
- `wuhao21/sts2-cli` runs the real `sts2.dll` with Godot stubs. Its target is v0.111, so it needs v0.107.1 adapters.
- `tfriedel/spire-mentor` is a v0.107.1 Rust port, parity-tested against the headless game.

Feed either one through `text_state.py`.

## Related work (verified 2026-10-24)

| project | what | lang | sim or wrapper | speed | license |
|---|---|---|---|---|---|
| [gamerpuppy/sts_lightspeed](https://github.com/gamerpuppy/sts_lightspeed) | STS1 RNG-accurate re-implementation | C++ | simulator | ~1M playouts / 5 s on 16 threads | MIT |
| [ForgottenArbiter/CommunicationMod](https://github.com/ForgottenArbiter/CommunicationMod) + [spirecomm](https://github.com/ForgottenArbiter/spirecomm) | STS1 JSON over stdin/stdout + Python client | Java / Py | live wrapper | game speed | MIT |
| [kronion/CommunicationModPlus](https://github.com/kronion/CommunicationModPlus), [gym-sts](https://github.com/kronion/gym-sts) | CommunicationMod fork / Gym env (needs the game) | Java / Py | live wrapper | game speed | MIT / none |
| [xaved88/bottled_ai](https://github.com/xaved88/bottled_ai) | STS1 heuristic bot with its own combat lookahead | Py | live wrapper + partial sim | game speed | MIT |
| [jahabrewer/decapitate-the-spire](https://github.com/jahabrewer/decapitate-the-spire) | headless STS1 clone (partial) | Py | simulator | fast | GPL-3.0 |
| [minispire](https://pypi.org/project/minispire/) ([repo](https://github.com/rob-pitkin/mini-spire)) | STS1 combat env | C++/Py | simulator (combat) | ~259k steps/s | MIT |
| [alavico/sts-bench](https://github.com/alavico/sts-bench) | STS1 LLM benchmark, 12 models | Py | live wrapper | game speed | MIT |
| arXiv [2410.02829](https://arxiv.org/abs/2410.02829) | LLMs playing STS1 via CommunicationMod | – | live wrapper | – | – |
| [wuhao21/sts2-cli](https://github.com/wuhao21/sts2-cli) | real `sts2.dll` headless (IL patches, Godot stubs), JSON over stdin/stdout, full runs, 5 characters | C# (.NET 9) | real engine, headless | ms/action | MIT |
| [tfriedel/spire-mentor](https://github.com/tfriedel/spire-mentor) | **v0.107.1** full-game port with PyO3 bindings, parity-tested against the headless game, MCTS/PPO | Rust/Py | simulator (port) | ~10 ms/action incl. bindings; 59k fights/s | MIT |
| [sansb/sts2-sim](https://github.com/sansb/sts2-sim) | v0.111 combat simulator | Rust | simulator (combat) | fast | MIT |
| [lain-wood/sts2core](https://github.com/lain-wood/sts2core) | v0.107.1 combat core (mostly Ironclad) | Rust | simulator (combat) | fast | MIT |
| [zhiyue/sts2-rl-agent](https://github.com/zhiyue/sts2-rl-agent) | Python STS2 sim + Gym env and a C# live bridge | Py/C# | simulator + wrapper | ~28k steps/s | none |
| [Gennadiyev/STS2MCP](https://github.com/Gennadiyev/STS2MCP) | live STS2 REST/MCP mod | C#/Py | live wrapper | game speed | MIT |
| [Alex5418/STS2-Agent](https://github.com/Alex5418/STS2-Agent), [ttxttx1111/sts2-llm](https://github.com/ttxttx1111/sts2-llm), [BMingSY/sts2-ai-mcp](https://github.com/BMingSY/sts2-ai-mcp) | LLM agents over a live mod | Py / C# | live wrapper | ~10 s/action | MIT / MIT / unstated |
| [SpireBench-Lite](https://www.spirebench.com/protocol) | STS2 v0.104 LLM benchmark (5 models × 5 characters, death-floor metric) | PowerShell IPC mod | live wrapper | game speed | – |

`shoyammishra/slay-the-spire-bench` now returns 404.

## Running a comparison

1. Provision a dedicated pool. Ports 15641 and up are the only ones used, and the other pools are refused:
   ```bash
   cd /workspace/hack-sts2-cloud
   STS2_HEADLESS_PORT_BASE=15640 scripts/headless_provision.sh 3 /workspace/private/worker-pool-llm
   ```
2. Set the key and check the model ids:
   ```bash
   export XAI_API_KEY=...        # never echo it
   python3 scripts/bench/llm_policy.py --list-models
   ```
3. Smoke-test one model on one seed:
   ```bash
   export STS2_GAME_INSTALL_DIR=/workspace/private/sts2-linux-0.107.1
   python3 scripts/bench/compare_models.py --models grok-4.3 --instances 1 \
       --seeds BENCHA0S01 --max-decisions 40
   ```
4. Run the full comparison (one instance per model, the same 10 seeds):
   ```bash
   python3 scripts/bench/compare_models.py \
       --models grok-4.3,grok-4.20-0309-non-reasoning,grok-4.20-0309-reasoning \
       --instances 1,2,3 [--reasoning-effort low] [--wiki-facts]
   python3 scripts/bench/compare_models.py --aggregate /workspace/private/bench-runs/<stamp>-models
   ```

`--dry-run` prints the plan without a key or a game. The summary columns are:
model, runs, wins, mean floor, mean act, boss kills, decisions, policy
errors, retries, input/cached/output tokens, tokens per decision, LLM s per
decision, act s per decision, total $ and $ per run.
