# sts2train-smoke — entity-encoder PPO smoke test on sts2vec + the NN protocol-eval entry point

Proves the batched training interface (`sim/VEC.md`, `sim/tools/vec`) works end to end for learning code, cheaply. It is
a smoke test, not a training recipe: results and numbers are in `docs/train-smoke-2026-11-02.md`.

```bash
dotnet build sim/Sts2Sim.sln -c Release                      # Sts2Sim.Vec.dll (needs the E3 aux columns)
uv sync --project sim/tools/train                            # numpy, torch, matplotlib, sts2vec + sts2eval (path deps)
uv run --project sim/tools/train pytest -q                   # gate (from the repo root or sim/tools/train)

# smoke run (SMOKE-ONLY shaped objective; outputs under tmp/, never committed)
uv run --project sim/tools/train sts2train-smoke train --minutes 52 --shaped --device mps --out tmp/train-smoke/run1

# protocol v0.2 NN entry point: greedy policy on a frozen seed set -> run dir + report.md (same format as sts2eval)
uv run --project sim/tools/train sts2train-smoke eval --seedset sim/tools/eval/seedsets/a0-s20.json \
    --checkpoint tmp/train-smoke/run1/checkpoint.pt --out tmp/eval/nn-smoke-a0-s20
uv run --project sim/tools/eval sts2eval report tmp/eval/nn-smoke-a0-s20      # recomputes the same report
```

(`eval` also answers to `evaluate`.)

## Pieces

| Module | Content |
| --- | --- |
| `features.py` | `ModelSpec` (vocab / enum sizes, column maps, shapes, encoder_version from the published schema; stored in checkpoints), `obs_to_torch` (selects the 27 buffers the model reads; cuts `cand` / `prompt_options` to the rows the batch needs), `RolloutStore` ([T, N, ...] numpy copies, 643 MiB at 32 × 1024 with 64 candidate / prompt rows) |
| `model.py` | `Policy` (948,750 parameters at d = 96, s = 256): id embeddings for cards, relics, potions, powers, monsters, encounters, events, orbs, enchantments, afflictions, card vars and every enum; per-table row MLPs (hand / card reward share one card encoder; deck and piles are embedding bags; map nodes, prompt options single layer) with masked mean pooling; a global head; candidate scorer whose first layer is factored per source (kind, A/B, hand_slot → hand row, target_slot → enemy row, potion_slot → potion row, option_index → the option bank of its kind, card_id → card embedding, state) and scored bilinearly against the state; masked to `j < cand_n`; value head |
| `ppo.py` | PPO (GAE, clipped policy and value loss, entropy bonus, Adam), `--shaped` flag, 5-minute curve windows, checkpoints, uniform-random baseline on the same env |
| `progress.py` | `ProgressTracker`: protocol v0.2 progress of finished auto-reset episodes from the previous call's `aux` (act_floor, act_rooms) + `last_episode`, via `sts2eval.progress.compute` |
| `evaluate.py` | `eval`: one pool entry and one slot per seed (`auto_reset=false`), greedy, protocol limits (step_cap 1200 → `capped`, floor_step_cap 80 non-combat steps → `stuck`), writes `config.json` / `run.json` / `games/<KEY>.summary.json` and `report.md` through `sts2eval.report` |

Objective: the default is the contract's (reward 1 at a Victory terminal, else 0; gamma = 1; floor / progress logged
only). `--shaped` adds `delta(total_floor) / 48` per step — a **smoke-only** objective so a one-hour run on one Mac sees
any signal; it is not the benchmark objective.

Training restricts actions to the first 64 candidates (`max_rows`; rare overflow is counted as `cand_over_max_rows`);
eval passes every candidate row. With `--device mps` the model runs on the Apple GPU (CPU fwd at N = 1024 ≈ 63 ms,
MPS ≈ 26 ms; fwd+bwd 139 ms vs 55 ms on an M4 Pro under load).

Outputs (`--out`, default `tmp/train-smoke`): `curve.csv` (5-minute windows), `updates.csv` (per update), `summary.json`,
`checkpoint.pt` (`spec`, weights, config, `weights_hash`), `curve.png` (`sts2train-smoke plot DIR` redraws it).
