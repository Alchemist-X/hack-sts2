# Multi-model LLM evaluation

Several LLMs play the fixed-seed benchmark (`docs/benchmark.md`) on the same
seeds. Each model sees the same compact text view of the game and gets the
same harness. Three new files make up the path, and the harness changes are
minimal:

| file | role |
|---|---|
| `scripts/bench/text_state.py` | Deterministic, complete text encoding of one observation, plus a token-measurement CLI |
| `scripts/bench/llm_policy.py` | `py:llm_policy:policy` for run_bench: one client with two providers, any OpenAI-compatible chat API (default xAI) or the native Anthropic Messages API, with the model as a parameter, JSON-action validation, retries, a timeout and per-decision token, latency and $ telemetry in the same shape for both |
| `scripts/bench/compare_models.py` | Runs the same seeds across N models in parallel, providers mixed per model (`anthropic:claude-opus-5-5`), and writes `summary.md` / `summary.json` |

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

The policy is configured through environment variables. Where two defaults
are given, the first is for `LLM_PROVIDER=openai`, the second for
`LLM_PROVIDER=anthropic`.

| var | default | meaning |
|---|---|---|
| `LLM_PROVIDER` | `openai` | `openai`: any OpenAI-compatible `POST {base}/chat/completions`. `anthropic`: the native Messages API, `POST {base}/messages` |
| `XAI_API_KEY` / `ANTHROPIC_API_KEY` | required | API key of the provider in use. It is sent only in the `Authorization: Bearer` header (openai) or the `x-api-key` header (anthropic) and is never printed or logged. Error text is scrubbed of key-shaped tokens and of the configured key itself. |
| `LLM_MODEL` | required | model id, e.g. `grok-4.3`, `claude-opus-5-5` (`--list-models` queries `GET {base}/models`) |
| `LLM_BASE_URL` | `https://api.x.ai/v1` / `https://api.anthropic.com/v1` | base URL including `/v1` |
| `LLM_API_KEY_ENV` | `XAI_API_KEY` / `ANTHROPIC_API_KEY` | name of the env var that holds the key |
| `LLM_TIMEOUT_S` | 120 | per HTTP request. Requests are not streamed, so the whole answer, thinking included, must arrive within it |
| `LLM_MAX_RETRIES` | 2 | re-asks after an invalid answer; the validation error is sent back to the model |
| `LLM_HTTP_RETRIES` | 3 | backoff (2, 4, 8 s) on 408/409/429/5xx, including Anthropic's 529 overloaded, and on network errors |
| `LLM_REASONING_EFFORT` | unset | `low` / `medium` / `high` (and `xhigh` / `max` where the model has them). Sent as `reasoning_effort` (openai) or `output_config.effort` (anthropic) |
| `LLM_MAX_TOKENS` | unset / 16000 | openai: passed through when set. anthropic: always sent, because the API requires it; thinking counts against it, and only generated tokens are billed |
| `LLM_TEMPERATURE` | unset | openai only. With anthropic the policy refuses it before any request: from `anthropic-version` 2026-10-21 `temperature`, `top_p` and `top_k` are a 400 on every model |
| `LLM_THINKING` | unset | anthropic only: `thinking.type` (`adaptive`, `disabled`, `between_tools`). Unset leaves the model default |
| `LLM_ANTHROPIC_VERSION` | `2026-10-21` | anthropic only: the `anthropic-version` header |
| `LLM_HISTORY` | 8 | recent decisions (action and reason) shown per seed |
| `LLM_PRICES_JSON` | built-in | `{"model": [in, cached, out]}` or `[in, cached, out, cache_write]` in $/1M tokens |

The model must reply with `{"action": <n>, "reason": "<short>"}`, or with
`[n1, n2, ...]` on multi-select screens. The number maps back to one of the
enumerated legal refs, so an LLM policy can never send a menu, save/load or
eval-API action. On an invalid answer the policy asks again. If every attempt
fails it raises `LLMPolicyError` (a `PolicyError`), which run_bench records as
a `policy_error` friction. The tokens spent on the failed decision are carried
into the next decision's `llm.carried_from_failed`. Configuration mistakes
that would fail every request (no model, an unknown provider, a `claude-*` id
on the default xAI URL, `LLM_TEMPERATURE` with anthropic) raise the same error
before any request is sent.

Each decision record gets `llm: {provider, model, served_model, attempts,
validation_errors, latency_s, api_latency_s, prompt_chars, prompt_tokens,
cached_tokens, completion_tokens, reasoning_tokens, output_tokens, cost_usd}`,
plus `reasoning_effort` and `thinking` when set. The anthropic provider adds
`cache_write_tokens`. Cost comes from xAI's `usage.cost_in_usd_ticks` (1e-10
USD) when the API returns it. Otherwise it is computed from `PRICES`.
Reasoning tokens are billed as output.

Prefer `py:llm_policy:policy`. A `cmd:` policy works too, but it starts one
process per decision and therefore loses the recent-decision history.

### Anthropic provider

`LLM_PROVIDER=anthropic` calls the Messages API directly over HTTP with
`urllib`, not the Anthropic SDK. The bench scripts run on a bare `python3` on
the worker hosts and `sts2rec` has no runtime dependencies, so both providers
share one stdlib transport with the same timeout, backoff and redaction.

- **Request.** Each request sends `model`, `max_tokens`, the system prompt as
  top-level `system`, and the user turn, plus `output_config.effort` and
  `thinking` when they are set. It sends no tools and no sampling parameters.
  It also sets no `cache_control`: the system prompt is far below the minimum
  cacheable prefix, and the state text changes every decision.
- **Effort defaults differ by model.** Claude Opus 5.5 and Claude Haiku 5.5
  default to `medium` and the other current models to `high`. Claude Opus
  4.8, 4.7 and 4.6 and Claude Sonnet 4.6 run without thinking unless
  `LLM_THINKING=adaptive` is set. Claude Haiku 4.5 only has budgeted thinking,
  which this policy does not send, and rejects `output_config.effort`. Set
  `LLM_REASONING_EFFORT`, and `LLM_THINKING` for the 4.x models, explicitly
  so a comparison is like-for-like. At `high` and above, raise
  `LLM_TIMEOUT_S` (for example to 300).
- **Re-asks.** After an invalid answer, the reply's content blocks go back
  unchanged as the assistant turn. That includes the signed thinking blocks:
  the API checks replayed thinking, and edited or dropped blocks can be a 400.
  The validation error follows as a user turn. A reply with no text, such as
  `stop_reason: max_tokens` while thinking or a `refusal`, is re-asked as a
  user turn only. Its stop reason (and refusal category) appears in
  `validation_errors`.
- **No server-side fallbacks.** On a refusal the API could otherwise
  re-run the request on another model, which would let a different model play
  the benchmark. `served_model` records which model answered every decision.
- **Telemetry mapping.** The Anthropic usage fields map onto the shared
  fields as follows:
  - `prompt_tokens` is `input_tokens + cache_read_input_tokens + cache_creation_input_tokens` (Anthropic's `input_tokens` excludes the cache).
  - `cached_tokens` is the cache reads, and `cache_write_tokens` the cache writes.
  - `completion_tokens` and `output_tokens` are both Anthropic's `output_tokens`, which includes thinking.
  - `reasoning_tokens` stays 0, because the API does not report thinking separately.

  The API returns no per-request cost, so `cost_usd` is computed as fresh
  input × in + cache reads × cached + cache writes × cache write + output ×
  out. A price without a fourth element bills cache writes at 1.25× input.

Built-in Claude prices, checked against
[platform.claude.com/docs/en/about-claude/pricing](https://platform.claude.com/docs/en/about-claude/pricing)
on 2026-10-30, in $ per 1M tokens:

| model | input | cache read | 5-min cache write | output |
|---|---:|---:|---:|---:|
| `claude-fable-5-1` | 10.00 | 0.25 | 12.50 | 50.00 |
| `claude-fable-5` | 10.00 | 1.00 | 12.50 | 50.00 |
| `claude-opus-5-5` | 4.00 | 0.20 | 5.00 | 20.00 |
| `claude-opus-5`, `claude-opus-4-8`, `claude-opus-4-7`, `claude-opus-4-6` | 5.00 | 0.50 | 6.25 | 25.00 |
| `claude-sonnet-5-5` | 2.00 | 0.10 | 2.50 | 10.00 |
| `claude-sonnet-5` | 2.00 | 0.20 | 2.50 | 10.00 |
| `claude-sonnet-4-6` | 3.00 | 0.30 | 3.75 | 15.00 |
| `claude-haiku-5-5` (prompts up to 100k tokens) | 0.10 | 0.01 | 0.125 | 0.50 |
| `claude-haiku-4-5` | 1.00 | 0.10 | 1.25 | 5.00 |

`LLM_PRICES_JSON` overrides any row. `--list-models` with the anthropic
provider sends `GET /v1/models?limit=1000`, because the API pages by 20.

## Pure text vs a headless simulator (v0.107.1)

An offline engine now exists: `compare_models.py --engine sim` drives spire-mentor (pinned, external) with the same text_state + llm_policy path; seed BENCHA0S01 reproduces the live Neow, map and first fight exactly. See [sim-engine.md](sim-engine.md).

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
2. Set the key of each provider you compare and check the model ids:
   ```bash
   export XAI_API_KEY=...        # never echo it
   export ANTHROPIC_API_KEY=...  # only for anthropic: models; never echo it
   python3 scripts/bench/llm_policy.py --list-models
   python3 scripts/bench/llm_policy.py --provider anthropic --list-models
   ```
3. Smoke-test one model on one seed:
   ```bash
   export STS2_GAME_INSTALL_DIR=/workspace/private/sts2-linux-0.107.1
   python3 scripts/bench/compare_models.py --models grok-4.3 --instances 1 \
       --seeds BENCHA0S01 --max-decisions 40
   python3 scripts/bench/compare_models.py --models anthropic:claude-haiku-5-5 --instances 1 \
       --seeds BENCHA0S01 --max-decisions 40 --reasoning-effort low
   ```
4. Run the full comparison (one instance per model, the same 10 seeds):
   ```bash
   python3 scripts/bench/compare_models.py \
       --models grok-4.3,grok-4.20-0309-reasoning,anthropic:claude-sonnet-5-5,anthropic:claude-opus-5-5 \
       --instances 1,2,3,4 [--reasoning-effort medium] [--wiki-facts]
   python3 scripts/bench/compare_models.py --aggregate /workspace/private/bench-runs/<stamp>-models
   ```

On the macOS workstation (game v0.111.0, this repo's provisioner and
launcher, baselines under `artifacts/bench-baseline/v0.111.0`):

```bash
POOL=$PWD/artifacts/worker-pool-llm
STS2_HEADLESS_FRESH_PROFILE=1 STS2_HEADLESS_PORT_BASE=15640 scripts/headless_provision.sh 4 $POOL   # once; fresh worker profiles
export STS2_BENCH_BASELINE_ROOT=/Users/Aincrad/dev-proj/hack-sts2/artifacts/bench-baseline
export ANTHROPIC_API_KEY=...   # and/or XAI_API_KEY; never echo them
python3 scripts/bench/compare_models.py --models anthropic:claude-opus-5-5,grok-4.3 \
    --pool $POOL --instances 1,2,3,4 --instances-per-model 2 \
    --config config/bench/ironclad-a10-10seed.json --start-method eval \
    --game-settings '{"fast_mode":"fast","screenshake":0,"text_effects":false,"skip_tutorials":true}' \
    --out <dir>/<stamp>-a10-models
```

`--start-method` and `--game-settings` are passed to every `run_bench.py`
(live engine only). `--out` is required off the cloud hosts (the default is
under `/workspace/private`).

A `<provider>:` prefix in `--models` (`anthropic:`, `openai:`) sets
`LLM_PROVIDER` for that model only. A bare id uses `LLM_PROVIDER` from the
environment, which defaults to `openai`. A bare `claude-*` id is refused
because it would be sent to xAI. Each model's output directory is named after
the bare id, so the same id may appear only once. When mixing providers,
leave `LLM_API_KEY_ENV`, `LLM_BASE_URL` and `LLM_TEMPERATURE` unset: they
apply to every model, and every provider then reads its own default key
variable. Before anything starts, `compare_models` resolves each model's
configuration and checks, by name only, that its key variable is set.

`--dry-run` prints the plan, with each model's provider, without a key or a
game. The summary columns are: model, runs, wins, mean floor, mean act, boss
kills, decisions, policy errors, retries, input/cached/output tokens, tokens
per decision, LLM s per decision, act s per decision, total $ and $ per run.
`summary.json` also carries each model's `provider`.
