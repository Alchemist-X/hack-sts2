"""sts2train CLI (also installed as ``sts2train-smoke``): ``train`` (PPO), ``eval`` (protocol v0.2 NN entry point; exit
1 when the replay audit fails, like ``sts2vec eval``; greedy / sampled, loop breaker on / off, repeats, slices),
``compare`` (per-seed paired comparison of evaluations), ``plot`` (curves of a run directory), ``bench-update``
(synthetic timing of the PPO update and the rollout forward), ``report-card`` / ``pack`` / ``probe`` (model card,
weights bundle and per-decision readout of a checkpoint: ``sts2train.reportcard``).

    sts2train train --preset large --device cuda --envs 2048 --max-hours 6 --eval-every 50 --out RUN_DIR
    sts2train train --resume auto --out RUN_DIR          # continue from the newest checkpoint in RUN_DIR
                                                         # (also across encoder versions: model inputs must match)
    sts2train train --characters SILENT --start-saves SAVES_DIR --save-mix 0.5 ...   # half the starts from saves
    sts2train train ... --harvest boss:1536,elite:512,act2:1024 --harvest-prob 0.25  # self-anchor starts
    sts2train train --start-mode stream ... --harvest boss:1536,elite:512,act2:1024 --harvest-start-share 0.33
    sts2train eval --seedset std-a0-v1 --checkpoint RUN_DIR/checkpoints/best.pt --out EVAL_DIR
    sts2train eval ... --loop-breaker off                # the plain argmax (no-op loops run into the floor cap)
    sts2train report-card --checkpoint CKPT --out CARD_DIR [--seedsets silent-a10-v1,...] [--lookahead-dir DIR ...]
    sts2train pack --checkpoint CKPT --out FILE.tar.gz [--card CARD_DIR]
    sts2train probe --checkpoint CKPT --eval-dir EVAL_DIR [--game KEY] [--out FILE.jsonl]
    sts2train eval ... --policy sample --sample-seed 1 --repeats 3 --breaker off --chunk 0/2   # execution modes
    sts2train compare RUN_A0,RUN_A1 RUN_B0,RUN_B1 --labels a,b      # per-seed paired comparison (sts2train.compare)

``train`` flags that are not given keep the default (fresh run) or the checkpoint's value (``--resume``).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

from .model import PRESETS


class _Log:
    """stdout (a closed pipe, e.g. an interrupted `| tee`, is ignored) + RUN_DIR/train.log with timestamps."""

    def __init__(self):
        self.f = None

    def open(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self.f = open(path, "a", encoding="utf-8")

    def __call__(self, m: str) -> None:
        try:
            print(m, flush=True)
        except (BrokenPipeError, OSError):
            pass
        if self.f is not None:
            try:
                self.f.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} {m}\n")
                self.f.flush()
            except OSError:
                pass


def _plot(out: Path) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    from .ppo import curve

    rows, evals = curve(out)
    rows = [r for r in rows if r.get("progress_mean") is not None]
    if not rows:
        return
    x = [r["env_steps"] / 1e6 for r in rows]
    k = 20

    def smooth(v):
        return [sum(v[max(0, i - k + 1):i + 1]) / len(v[max(0, i - k + 1):i + 1]) for i in range(len(v))]

    fig, ax = plt.subplots(1, 2, figsize=(11, 4))
    saves = any("save_episodes" in r for r in rows)

    def series(key):            # rolling mean over the updates that finished such episodes (NaN = none yet)
        v = [r.get(key) for r in rows]
        out = []
        for i in range(len(v)):
            w = [y for y in v[max(0, i - k + 1):i + 1] if y is not None]
            out.append(sum(w) / len(w) if w else float("nan"))
        return out

    if saves:                   # --start-saves: fresh starts (comparable to a run without saves) vs save starts
        ax[0].plot(x, series("fresh_progress_mean"), color="#1f4e8c", lw=1.4,
                   label=f"fresh starts: progress (rolling {k})")
        ax[0].plot(x, series("save_progress_mean"), color="#c77d2a", lw=1.1, ls="--",
                   label="save starts: progress at the end")
        ax[0].plot(x, series("save_progress_gain_mean"), color="#8a4f0e", lw=1.4,
                   label="save starts: progress gained")
        if any("harvest_episodes" in r for r in rows):
            ax[0].plot(x, series("harvest_progress_gain_mean"), color="#5b3a8c", lw=1.2, ls=":",
                       label="self-anchor starts: progress gained")
    else:
        p = [r["progress_mean"] for r in rows]
        ax[0].plot(x, p, color="#9db4d6", lw=0.6, label="per update (training episodes)")
        ax[0].plot(x, smooth(p), color="#1f4e8c", lw=1.4, label=f"rolling mean ({k} updates)")
    ev = [e for e in evals if e.get("progress_mean") is not None]
    if ev:
        ax[0].plot([e["env_steps"] / 1e6 for e in ev], [e["progress_mean"] for e in ev], "o-", color="#b04a3a",
                   ms=4, label=f"protocol eval ({ev[-1].get('seedset')})")
    ax[0].set_xlabel("env steps (M)")
    ax[0].set_ylabel("progress v0.2")
    ax[0].legend(frameon=False)
    ax[0].set_title("protocol progress")
    if saves:
        ax[1].plot(x, series("fresh_floor_mean"), color="#2e6b2a", lw=1.4, label=f"fresh starts (rolling {k})")
        ax[1].plot(x, series("save_floor_mean"), color="#c77d2a", lw=1.4, label="save starts")
    else:
        f = [r.get("floor_mean") or 0.0 for r in rows]
        ax[1].plot(x, f, color="#a9cfa4", lw=0.6, label="per update")
        ax[1].plot(x, smooth(f), color="#2e6b2a", lw=1.4, label=f"rolling mean ({k} updates)")
    ax[1].set_xlabel("env steps (M)")
    ax[1].set_ylabel("floor reached (finished episodes)")
    ax[1].legend(frameon=False)
    ax[1].set_title("floor reached")
    for a in ax:
        a.spines[["top", "right"]].set_visible(False)
    fig.tight_layout()
    fig.savefig(out / "curve.png", dpi=130)


def _speed_flags(g, compile_default: str = "off") -> None:
    g.add_argument("--compile", choices=["off", "default", "reduce-overhead"],
                   help="torch.compile the PPO minibatch forward + loss and the rollout forward: default (fused "
                        "kernels) or reduce-overhead (+ CUDA graphs); implies --static-rows; the first update "
                        f"compiles (default {compile_default})")
    g.add_argument("--static-rows", dest="static_rows", action=argparse.BooleanOptionalAction,
                   help="cand / prompt_options always --max-rows wide (fixed shapes; default: on with --compile)")


def _train_parser(sub) -> argparse.ArgumentParser:
    from .ppo import TrainConfig

    D = TrainConfig()
    tr = sub.add_parser("train", help="PPO on sts2vec (fresh or --resume)", argument_default=argparse.SUPPRESS,
                        description="Flags not given keep the default (fresh run) or the checkpoint's value (--resume).")
    g = tr.add_argument_group("run")
    g.add_argument("--out", help=f"run directory (default {D.out}-<time>)")
    g.add_argument("--resume", help="checkpoint path, or 'auto' = the newest checkpoint under --out (fresh if none)")
    g.add_argument("--device", help=f"cpu | mps | cuda[:i] | auto (default {D.device})")
    g.add_argument("--amp", action=argparse.BooleanOptionalAction, help="bf16 autocast on CUDA (default off)")
    _speed_flags(g, compile_default=D.compile)
    g.add_argument("--grad-checkpoint", dest="grad_ckpt", action=argparse.BooleanOptionalAction,
                   help="recompute the transformer blocks in backward: ~4x less activation memory for the large "
                        "preset, more compute (default off)")
    g.add_argument("--allow-big-local", dest="allow_big_local", action="store_true",
                   help="allow cpu / mps training of models above 3M parameters or the large preset (refused otherwise)")
    g.add_argument("--torch-threads", dest="torch_threads", type=int, help="torch CPU threads (0 = auto)")
    g.add_argument("--seed", type=int,
                   help=f"derives --init-seed / --sample-seed / --env-seed / --start-seed when they are not given "
                        f"(default {D.seed}); on a resume the checkpoint's resolved seeds are kept")
    g.add_argument("--init-seed", dest="init_seed", type=int,
                   help="model initialisation (its own generator; a resume must keep the checkpoint's)")
    g.add_argument("--sample-seed", dest="sample_seed", type=int,
                   help="action sampling (torch) and minibatch shuffles (numpy); on a resume a different value "
                        "re-seeds them instead of restoring the checkpoint's RNG states")
    g.add_argument("--env-seed", dest="env_seed", type=int,
                   help="environment seeds: the stream prefix SP<env_seed> (--start-mode stream) or the fixed pool's "
                        "family TRAIN<env_seed> (pool)")
    g.add_argument("--start-seed", dest="start_seed", type=int,
                   help="--start-mode pool only: the slots' start offsets into the pool and the save copies")
    g = tr.add_argument_group("budget (whichever comes first)")
    g.add_argument("--max-hours", dest="max_hours", type=float,
                   help=f"wall-clock budget of this session, hours (default {D.max_hours}; 0 = none)")
    g.add_argument("--minutes", type=float, help="LEGACY: --max-hours in minutes")
    g.add_argument("--max-updates", dest="max_updates", type=int, help="total PPO updates incl. resumed (0 = none)")
    g.add_argument("--max-env-steps", dest="max_env_steps", type=int, help="total env steps incl. resumed (0 = none)")
    g = tr.add_argument_group("checkpoints, logs, eval")
    g.add_argument("--ckpt-every", dest="ckpt_every", type=int, help=f"checkpoint every N updates (default {D.ckpt_every})")
    g.add_argument("--ckpt-every-min", dest="ckpt_every_min", type=float,
                   help=f"and every M minutes (default {D.ckpt_every_min})")
    g.add_argument("--keep-ckpts", dest="keep_ckpts", type=int, help=f"keep the last K (default {D.keep_ckpts})")
    g.add_argument("--eval-every", dest="eval_every", type=int,
                   help="protocol eval every E updates (default 0 = off); also once at the end of a budget-ended session")
    g.add_argument("--eval-seedset", dest="eval_seedset", help=f"(default {D.eval_seedset})")
    g.add_argument("--eval-threads", dest="eval_threads", type=int, help="(0 = auto)")
    g.add_argument("--loop-breaker", dest="loop_breaker", choices=["off", "on"],
                   help="loop breaker of the periodic eval's greedy policy (sts2train.loopbreak): back at an "
                        "observation a game already decided at (a no-op or a cycle), mask the candidates taken there; "
                        "from the --loop-breaker-k-th return prefer leaving the screen (default on)")
    g.add_argument("--eval-at", dest="eval_at", metavar="U1,U2,...",
                   help="fixed-endpoint evaluations at these absolute update numbers: --eval-at-seedset in every "
                        "--eval-at-modes mode into evals/u<N>/<mode>/, one evals.jsonl row per mode (mode, eval_at), "
                        "the checkpoint kept as checkpoints/pinned-<N>.pt; they never select best.pt (default none)")
    g.add_argument("--eval-at-seedset", dest="eval_at_seedset", help=f"(default {D.eval_at_seedset})")
    g.add_argument("--eval-at-modes", dest="eval_at_modes", metavar="MODES",
                   help="comma-separated greedy-on / greedy-off / sample-on / sample-off (default "
                        f"{','.join(D.eval_at_modes)}; sample = one play per game)")
    g.add_argument("--eval-at-sample-seed", dest="eval_at_sample_seed", type=int,
                   help=f"play RNG seed of the sample-mode eval-at runs (default {D.eval_at_sample_seed})")
    g.add_argument("--no-best", dest="no_best", action="store_true",
                   help="never select best.pt (the periodic --eval-every eval stays a monitor; compare fixed "
                        "endpoints with --eval-at instead)")
    g.add_argument("--best", dest="no_best", action="store_false", help="undo --no-best on --resume")
    g.add_argument("--log-every", dest="log_every", type=int, help=f"stdout line every N updates (default {D.log_every})")
    g.add_argument("--episodes-log", dest="episodes_log", action=argparse.BooleanOptionalAction,
                   help="RUN_DIR/episodes.jsonl: one row per episode (seed, result, act, floor, steps, progress; "
                        "sts2train.episodes; default on)")
    g.add_argument("--allow-blind", dest="allow_blind", action="store_true",
                   help="let the periodic eval use a seed set with games of a frozen blind set (refused otherwise; "
                        "needs --purpose; never inherited by a resume); logged to RUN_DIR/blind-access.jsonl")
    g.add_argument("--purpose", dest="blind_purpose", help="why a blind set is evaluated (with --allow-blind)")
    g.add_argument("--baseline-steps", dest="baseline_steps", type=int, help="uniform-random baseline steps first")
    g = tr.add_argument_group("model")
    g.add_argument("--preset", choices=sorted(PRESETS),
                   help="architecture preset: " + "; ".join(f"{k} {v}" for k, v in PRESETS.items()))
    g.add_argument("--d-model", dest="d", type=int, help=f"(default {D.d})")
    g.add_argument("--state-dim", dest="s", type=int, help=f"(default {D.s})")
    g.add_argument("--depth", type=int, help=f"transformer blocks (default {D.depth} = pooled-concat model)")
    g.add_argument("--heads", type=int, help=f"(default {D.heads})")
    g.add_argument("--ff-mult", dest="ff_mult", type=int, help=f"(default {D.ff_mult})")
    g.add_argument("--deck-counts", dest="deck_counts", action=argparse.BooleanOptionalAction,
                   help="count-aware deck / pile encoders: embedding-bag sum with log(1 + copies) per card instead of "
                        "the mean embedding, deck size / distinct / upgraded / enchanted / afflicted counts, and the "
                        "copies of each candidate's card in the deck; part of the architecture (default off)")
    g.add_argument("--aux-heads", dest="aux_heads", action=argparse.BooleanOptionalAction,
                   help="auxiliary heads on the state: win_prob (BCE to the episode outcome) and combat_hp_pred (hp "
                        "fraction at the end of the current combat, squared error), trained with the PPO loss; part "
                        "of the architecture (a checkpoint resumes only with its own setting); `sts2train eval` writes "
                        "their per-step readouts into the game logs (default off)")
    g.add_argument("--aux-coef", dest="aux_coef", type=float,
                   help=f"weight of the aux heads' losses (default {D.aux_coef})")
    g.add_argument("--aux-bootstrap", dest="aux_bootstrap", action=argparse.BooleanOptionalAction,
                   help="aux targets of episodes / combats still running at the end of a rollout window: the heads' "
                        "own prediction there (n-step bootstrap; default on) or masked (--no-aux-bootstrap)")
    g = tr.add_argument_group("objective")
    g.add_argument("--objective", choices=["protocol", "win", "shaped"],
                   help="protocol (default): per-step delta of the protocol v0.2 progress + --win-bonus at a win; "
                        "win: 1 at a Victory terminal; shaped: LEGACY smoke objective (win + delta floor / 48)")
    g.add_argument("--win-bonus", dest="win_bonus", type=float, help=f"(default {D.win_bonus})")
    g.add_argument("--shaped", action="store_true", help="LEGACY (= --objective shaped)")
    g.add_argument("--acq-shaping", dest="acq_shaping", action=argparse.BooleanOptionalAction,
                   help="potential-based acquisition shaping added to the objective's reward: phi = deck_coef x "
                        "min(deck - 10, 12) + relic_coef x relics + upgrade_coef x upgrade levels, 0 at the end of an "
                        "episode, F = gamma phi(s') - phi(s) per step (telescopes; the optimal policy is unchanged); "
                        "logged apart from the unshaped reward / return / progress (default off)")
    g.add_argument("--acq-deck-coef", dest="acq_deck_coef", type=float, help=f"(default {D.acq_deck_coef})")
    g.add_argument("--acq-relic-coef", dest="acq_relic_coef", type=float, help=f"(default {D.acq_relic_coef})")
    g.add_argument("--acq-upgrade-coef", dest="acq_upgrade_coef", type=float, help=f"(default {D.acq_upgrade_coef})")
    g.add_argument("--rollout-loop-breaker", dest="rollout_loop_breaker", action=argparse.BooleanOptionalAction,
                   help="apply the eval's loop breaker to the rollout sampling: back at an observation of its last "
                        "64 decisions a slot samples with probability 0 for the candidates taken there (stored "
                        "log-probs are the masked distribution's); logs loop_breaks / no_op_actions / cycle_revisits "
                        "per update (default off)")
    g.add_argument("--loop-breaker-k", dest="loop_breaker_k", type=int,
                   help=f"returns to one observation after which the loop breaker prefers the rows that leave the "
                        f"screen (eval and rollouts; default {D.loop_breaker_k})")
    g.add_argument("--cycle-cost", dest="cycle_cost", type=float,
                   help="subtract this from the trainer's reward of every transition that closes a verified UI "
                        "revisit cycle (RestSite / Shop -> DeckUpgrade / DeckRemove prompt -> back to the same "
                        "observation; sts2train.cycles), once per cycle; the env, gamma / lambda, the logged "
                        "objective and the acquisition shaping are unchanged. Cycles are counted and logged "
                        f"(cycles.jsonl, metrics) in every run (default {D.cycle_cost} = count only)")
    g = tr.add_argument_group("env")
    g.add_argument("--envs", type=int, help=f"(default {D.envs})")
    g.add_argument("--threads", type=int, help="env worker threads (default 0 = cpu count)")
    g.add_argument("--step-cap", dest="step_cap", type=int, help=f"(default {D.step_cap})")
    g.add_argument("--floor-step-cap", dest="floor_step_cap", type=int,
                   help="VecEnv floor_step_cap pass-through: truncate after N non-combat decisions on one floor "
                        "(FLOOR_STALL; 0 = off; an error if this sts2vec has no such parameter). Default unset = the "
                        "env's default (80 with a library that implements the cap); the cap in effect is recorded "
                        "in config.json env.floor_step_cap")
    g.add_argument("--ascension", type=int, help=f"(default {D.ascension})")
    g.add_argument("--start-mode", dest="start_mode", choices=["pool", "stream"],
                   help="pool (default): episodes start from a fixed pool of --pool fresh seeds "
                        "TRAIN<env_seed>-<C>-<i> (+ --start-saves); refused when it holds a frozen eval seed set's "
                        "game. stream: every episode "
                        "starts from a never-used seed <prefix>-<C>-<n>, n from a counter kept in the checkpoint "
                        "(sts2train.seedstream); holdout games are skipped; --start-saves must be none")
    g.add_argument("--seed-stream-prefix", dest="seed_stream_prefix",
                   help="stream: seed text prefix, [A-Z][A-Z0-9_]{0,23}, not TRAIN* (default SP<env_seed>)")
    g.add_argument("--stream-lookahead", dest="stream_lookahead", type=int,
                   help="stream: entries generated ahead (default auto: max(64, 2 x envs), grown to 1.25 x the starts "
                        "of an update; 0 = each start creates its run in the step)")
    g.add_argument("--stream-prefetch", dest="stream_prefetch", action=argparse.BooleanOptionalAction,
                   help="stream: create the lookahead's runs in a thread while the PPO update runs (default on; off = "
                        "synchronously once per update)")
    g.add_argument("--pool", type=int, help=f"fresh start pool entries (default {D.pool})")
    g.add_argument("--characters", type=lambda v: tuple(x.strip() for x in v.split(",") if x.strip()),
                   help="comma separated (default all five)")
    g.add_argument("--start-saves", dest="start_saves",
                   help="directory of game .save files (searched recursively; scripts/train/collect_start_saves.py) "
                        "to start episodes from, mixed with --pool fresh starts; unsupported saves are skipped and "
                        "reported; 'none' turns it off on --resume (default: fresh starts only)")
    g.add_argument("--save-mix", dest="save_mix", type=float,
                   help=f"fraction of episode starts from --start-saves, via pool entry weights (default {D.save_mix})")
    g.add_argument("--save-copies", dest="save_copies", choices=["determinized", "exact"],
                   help="further pool copies of a save: determinized = same player view, hidden state (draw order, "
                        f"RNG streams) resampled per copy; exact = every start replays the save's RNG (default "
                        f"{D.save_copies})")
    g = tr.add_argument_group("self-anchor starts (sts2train.harvest)")
    g.add_argument("--harvest", metavar="SPEC",
                   help="ring sizes kind:N,... with kind boss / elite / act2 / any (e.g. "
                        "boss:1536,elite:512,act2:1024): at a combat's first decision in a fresh-started training "
                        "episode, keep a determinized clone "
                        "(same player view, hidden state resampled) as a live pool entry in that kind's FIFO ring; the "
                        "env's auto-reset start sequence starts episodes from them. any = every other combat "
                        "(tests / smoke). 'off' turns it off on --resume (default off)")
    g.add_argument("--harvest-prob", dest="harvest_prob", type=float,
                   help=f"probability of keeping a detected combat entry (default {D.harvest_prob})")
    g.add_argument("--harvest-max-per-step", dest="harvest_max_per_step", type=int,
                   help=f"anchors captured per env step at most (default {D.harvest_max_per_step})")
    g.add_argument("--harvest-start-share", dest="harvest_start_share", type=float,
                   help="--start-mode stream: probability that a finished slot starts from a uniformly drawn live "
                        "anchor instead of the next stream seed (default 0.33 with --harvest; pool mode: not used, "
                        "the anchors take the auto-reset sequence's share)")
    g = tr.add_argument_group("PPO")
    for flag, dest, typ in (("--rollout", "rollout", int), ("--epochs", "epochs", int),
                            ("--minibatch", "minibatch", int), ("--lr", "lr", float), ("--gamma", "gamma", float),
                            ("--lam", "lam", float), ("--clip", "clip", float), ("--vf-coef", "vf_coef", float),
                            ("--ent-coef", "ent_coef", float), ("--max-grad-norm", "max_grad_norm", float),
                            ("--max-rows", "max_rows", int)):
        g.add_argument(flag, dest=dest, type=typ, help=f"(default {getattr(D, dest)})")
    return tr


def _bench_parser(sub) -> argparse.ArgumentParser:
    b = sub.add_parser("bench-update", help="time the PPO minibatch update and the rollout forward on random inputs",
                       description="Synthetic benchmark of the trainer's PPO minibatch update (gather, forward + loss, "
                                   "backward, clip, Adam) and rollout inference step on random observations of the "
                                   "schema's shapes. The numbers that matter come from the training GPU (A100); "
                                   "cpu / mps runs only validate the code paths with small settings.")
    b.add_argument("--preset", choices=sorted(PRESETS), help="architecture preset (default smoke)")
    b.add_argument("--d-model", dest="d", type=int)
    b.add_argument("--state-dim", dest="s", type=int)
    b.add_argument("--depth", type=int)
    b.add_argument("--heads", type=int)
    b.add_argument("--ff-mult", dest="ff_mult", type=int)
    b.add_argument("--device", default="auto", help="cpu | mps | cuda[:i] | auto (default auto)")
    b.add_argument("--amp", action=argparse.BooleanOptionalAction, default=False, help="bf16 autocast (CUDA only)")
    _speed_flags(b)
    b.add_argument("--modes", help="comma-separated compile modes to run one after another in this process (e.g. "
                                   "off,default,reduce-overhead) with a speed-up summary; overrides --compile")
    b.add_argument("--grad-checkpoint", dest="grad_ckpt", action="store_true", help="trunk activation checkpointing")
    b.add_argument("--deck-counts", dest="deck_counts", action="store_true",
                   help="the --deck-counts model (count-aware deck / pile pooling)")
    b.add_argument("--aux-heads", dest="aux_heads", action="store_true",
                   help="the --aux-heads model and loss (random aux targets and masks)")
    b.add_argument("--minibatch", type=int, default=2048, help="(default 2048)")
    b.add_argument("--envs", type=int, default=None, help="rollout inference batch (default = --minibatch)")
    b.add_argument("--max-rows", dest="max_rows", type=int, default=64, help="(default 64)")
    b.add_argument("--steps", type=int, default=20, help="timed minibatch updates (default 20)")
    b.add_argument("--warmup", type=int, default=5, help="untimed minibatch updates first (default 5)")
    b.add_argument("--infer-steps", dest="infer_steps", type=int, default=20, help="timed rollout steps (default 20)")
    b.add_argument("--no-check", dest="check", action="store_false",
                   help="skip the compiled-vs-eager numerics check on one minibatch")
    b.add_argument("--profile", action="store_true", help="torch.profiler table of the top ops after the timing")
    b.add_argument("--profile-steps", dest="profile_steps", type=int, default=3)
    b.add_argument("--top", type=int, default=15, help="profile rows (default 15)")
    b.add_argument("--torch-threads", dest="torch_threads", type=int, default=0)
    b.add_argument("--seed", type=int, default=0)
    b.add_argument("--spec", help="checkpoint (.pt) or spec JSON for the shapes (default: the sts2vec library schema)")
    b.add_argument("--json", help="also write the results to this JSON file")
    b.set_defaults(compile=None, static_rows=None)
    return b


def _card_parsers(sub) -> None:
    from .reportcard import BASELINES, DEFAULT_BASELINE_CACHE, DEFAULT_SEEDSETS

    def common(p) -> None:
        p.add_argument("--seedsets", default=",".join(DEFAULT_SEEDSETS),
                       help="comma-separated seed sets (name, id or path; default %(default)s)")
        p.add_argument("--threads", type=int, default=4, help="env worker threads and torch threads (default 4)")
        p.add_argument("--device", default="cpu", help="device of the model: cpu | cuda[:i] | mps (default cpu)")
        p.add_argument("--lookahead-dir", dest="lookahead_dirs", nargs="+", action="extend", default=[],
                       metavar="DIR", help="run directories of `python -m sts2train.lookahead eval` (read only): a "
                                           "search run is a 'lookahead, determinized' row, paired per seed against a "
                                           "--greedy run of the same weights (else the card's greedy row); a --greedy "
                                           "run is an 'NN greedy, loop breaker off' row")
        p.add_argument("--baselines", default=",".join(BASELINES),
                       help="rule policies run with sts2vec eval on the same seed sets for context, or 'none' "
                            "(default %(default)s)")
        p.add_argument("--baseline-cache", default=DEFAULT_BASELINE_CACHE,
                       help="baseline run directories, shared by every card (default %(default)s)")
        p.add_argument("--allow-blind", dest="allow_blind", action="store_true",
                       help="allow seed sets with games of a frozen blind set (refused otherwise; needs --purpose); "
                            "logged to OUT/blind-access.jsonl")
        p.add_argument("--purpose", dest="blind_purpose", default=None,
                       help="why a blind set is evaluated (with --allow-blind)")
        p.add_argument("--modes", default="greedy-on",
                       help="comma-separated execution modes (greedy-on, greedy-off, sample-on, sample-off): each one "
                            "after greedy-on is an NN row paired per seed against the greedy row (default %(default)s)")
        p.add_argument("--sample-seed", dest="sample_seed", type=int, default=1,
                       help="play RNG seed of the sample-mode rows (one play per game; default %(default)s)")

    rc = sub.add_parser("report-card", help="model card of a checkpoint: greedy evals (run or reused), baselines, "
                                            "staged tables with Wilson intervals",
                        description="Runs (or reuses) `sts2train eval` per seed set into OUT/evals/<seed set>, the "
                                    "rule baselines into --baseline-cache, and writes OUT/model-card.md + "
                                    "OUT/model-card.json. Exit 1 when a greedy eval fails the replay audit.")
    rc.add_argument("--checkpoint", required=True)
    rc.add_argument("--out", required=True, help="card directory")
    common(rc)
    pk = sub.add_parser("pack", help="weights bundle: checkpoint, spec / config, model card, library version, "
                                     "README-weights.md, sha256 manifest",
                        description="Writes FILE.tar.gz (top directory FILE) and verifies it. The model card comes "
                                    "from --card (default FILE.card next to the archive) when it is this checkpoint's, "
                                    "else it is made there with the report-card options below.")
    pk.add_argument("--checkpoint", required=True)
    pk.add_argument("--out", required=True, help="FILE.tar.gz")
    pk.add_argument("--card", help="model card directory (default FILE.card next to FILE.tar.gz)")
    common(pk)
    pr = sub.add_parser("probe", help="replay games of an eval run and read the checkpoint at every decision",
                        description="Replays the recorded actions of an eval run directory's games and prints, per "
                                    "decision, the action taken, the model's top candidates with probabilities, the "
                                    "value head (and win_prob / combat_hp_pred with aux heads).")
    pr.add_argument("--checkpoint", required=True)
    pr.add_argument("--eval-dir", dest="eval_dir", required=True, help="protocol run directory (sts2train eval)")
    pr.add_argument("--game", dest="games", action="append", metavar="KEY",
                    help="game key (SILENT-C1A10SI01) or seed; repeatable (default every game)")
    pr.add_argument("--top", type=int, default=3, help="candidates shown per decision (default 3)")
    pr.add_argument("--out", help="also write the rows as JSON lines to this file")
    pr.add_argument("--threads", type=int, default=4, help="(default 4)")
    pr.add_argument("--device", default="cpu", help="(default cpu)")


def _card_cmd(a) -> int:
    import torch

    from sts2vec.protocol import BlindSetError

    from . import reportcard as RC
    from .features import SpecMismatch

    torch.set_num_threads(max(1, a.threads))

    def log(m: str) -> None:
        print(m, flush=True)

    def csv(v: str) -> list[str]:
        return [x.strip() for x in v.split(",") if x.strip()]

    try:
        if a.cmd == "probe":
            rows = RC.probe(a.checkpoint, a.eval_dir, games=a.games, top=a.top, out=a.out, threads=a.threads,
                            device=a.device, log=log)
            print(json.dumps({"decisions": len(rows), "games": len({r["key"] for r in rows}), "out": a.out}))
            return 0
        kw = dict(seedsets=csv(a.seedsets), threads=a.threads, device=a.device, lookahead_dirs=a.lookahead_dirs,
                  baselines=[] if a.baselines.strip().lower() == "none" else csv(a.baselines),
                  baseline_cache=a.baseline_cache, modes=csv(a.modes), sample_seed=a.sample_seed, log=log,
                  allow_blind=a.allow_blind, blind_purpose=a.blind_purpose)
        res = {}
        if a.cmd == "report-card":
            card = RC.report_card(a.checkpoint, a.out, **kw)
            res["out"] = a.out
        else:
            res = RC.pack(a.checkpoint, a.out, card_dir=a.card, **kw)
            card = res.pop("card")
    except (SpecMismatch, BlindSetError) as e:
        print(f"error: {e}", flush=True)
        return 2
    res["seedsets"] = []
    failed = []
    looks = []
    for sec in card["seedsets"]:
        for r in sec["runs"]:
            if r["kind"] == "lookahead":
                p = r.get("paired") or {}
                looks.append({"seedset": sec["id"], "label": r["label"], "games": r["headline"]["games"],
                              "progress_mean": r["headline"]["progress_mean"],
                              "replayable": r["headline"]["replayable"],
                              "paired": {k: p[k] for k in ("vs", "like_for_like", "delta_mean", "t_ci95", "better",
                                                           "worse", "same", "error") if k in p} or None})
            if r["kind"] != "nn":
                continue
            h = r["headline"]
            res["seedsets"].append({"seedset": sec["id"], "games": h["games"], "progress_mean": h["progress_mean"],
                                    "progress_ci95": h["progress_ci95"], "wins": h["wins"],
                                    "win_rate_ci95": h["win_rate_ci95"], "reached_act2": r["staged"]["act2"]["count"],
                                    "replayable": h["replayable"], "reused": r["reused"]})
            if h["replayable"] != h["games"]:
                failed.append(f"{sec['id']} {h['replayable']}/{h['games']}")
    if looks:
        res["lookahead"] = looks
    print(json.dumps(res, indent=1))
    if failed:
        print(f"error: replay audit failed: {', '.join(failed)} games replayable (model-card.md lists the runs)",
              file=sys.stderr, flush=True)
        return 1
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="sts2train")
    sub = ap.add_subparsers(dest="cmd", required=True)
    _train_parser(sub)
    ev = sub.add_parser("eval", aliases=["evaluate"],
                        help="play a protocol seed set with a checkpoint's greedy policy (protocol v0.2 NN entry)")
    ev.add_argument("--seedset", required=True, help="seed set name, id or path (sim/tools/eval/seedsets)")
    ev.add_argument("--checkpoint", required=True)
    ev.add_argument("--out", default=None, help="run directory (default tmp/eval/nn-<checkpoint>-<seedset>-<time>)")
    ev.add_argument("--threads", type=int, default=8)
    ev.add_argument("--device", default="cpu")
    ev.add_argument("--loop-breaker", "--breaker", dest="loop_breaker", choices=["off", "on"], default="on",
                    help="back at an observation a game already decided at (a no-op or a cycle), mask the candidates "
                         "taken there; from the --loop-breaker-k-th return prefer the rows that leave the screen "
                         "(default on; off = the plain argmax / the plain distribution)")
    ev.add_argument("--loop-breaker-k", type=int, default=3, help="(default 3)")
    ev.add_argument("--allow-blind", action="store_true",
                    help="allow a seed set with games of a frozen blind set (refused otherwise; needs --purpose); "
                         "logged to OUT/blind-access.jsonl")
    ev.add_argument("--purpose", default=None, help="why a blind set is evaluated (with --allow-blind)")
    ev.add_argument("--policy", choices=["greedy", "sample"], default="greedy",
                    help="greedy = argmax (default); sample = one draw per decision from the model's distribution over "
                         "the legal candidates, the play's RNG seeded from (--sample-seed, game key, repeat)")
    ev.add_argument("--sample-seed", dest="sample_seed", type=int, default=0, help="(sample; default 0)")
    ev.add_argument("--repeats", type=int, default=1,
                    help="sample: plays per game; > 1 writes OUT/r<k>/ per repeat and OUT/repeats.json (default 1)")
    ev.add_argument("--chunk", default=None, metavar="I/N",
                    help="play only the I-th of N equal slices of the seed set (config.json parent_seedset records "
                         "the full set; `sts2train compare DIR0,DIR1 ...` merges slices)")
    pl = sub.add_parser("plot", help="draw curve.png of a train run directory")
    pl.add_argument("out")
    cp = sub.add_parser("compare", help="per-seed paired comparison of protocol evaluations of one seed set "
                                        "(modes, checkpoints; slices and repeats merged)",
                        description="Each ARM is a comma-separated list of run directories (sts2train eval: slices "
                                    "with --chunk, repeat parents with --repeats) of the same seed set. Prints per arm "
                                    "progress [95%% t], clear rate, stalls, UI revisit cycles, loop-breaker plays, "
                                    "the stage funnel, and per pair the per-seed differences (t and bootstrap 95%%).")
    cp.add_argument("arms", nargs="+", metavar="ARM")
    cp.add_argument("--labels", default=None, help="comma-separated arm labels")
    cp.add_argument("--json", default=None, help="also write the full result (per-game differences) to this file")
    _bench_parser(sub)
    _card_parsers(sub)
    a = ap.parse_args(argv)
    if a.cmd in ("report-card", "pack", "probe"):
        return _card_cmd(a)
    if a.cmd == "bench-update":
        from .bench import BenchSizeError, bench_update

        try:
            bench_update(a)
        except (BenchSizeError, ValueError) as e:
            print(f"error: {e}", flush=True)
            return 2
        return 0
    if a.cmd == "train":
        from .ppo import plan_run, train

        given = {k: v for k, v in vars(a).items() if k != "cmd"}
        resume = given.pop("resume", None)
        if "minutes" in given:
            given["max_hours"] = given.pop("minutes") / 60.0
        if "loop_breaker" in given:
            given["loop_breaker"] = given["loop_breaker"] == "on"
        if "start_saves" in given:      # absolute, so a resume from another working directory finds it
            v = given["start_saves"]
            given["start_saves"] = None if v.lower() in ("", "none") else os.path.abspath(os.path.expanduser(v))
        if "harvest" in given:          # "off" / "none" turn it off (on --resume)
            v = str(given["harvest"]).strip()
            given["harvest"] = "" if v.lower() in ("", "off", "none") else v
        if "out" not in given and resume in (None, "auto"):
            if resume == "auto":
                ap.error("--resume auto needs --out RUN_DIR")
            given["out"] = f"tmp/train/run-{time.strftime('%Y%m%d-%H%M%S')}"
        from sts2vec.protocol import BlindSetError

        from .features import SpecMismatch
        from .isolation import IsolationError
        from .ppo import BigLocalRunError

        log = _Log()
        try:
            cfg, ck, ck_path = plan_run(given, resume, log=log)
        except ValueError as e:         # config errors (incl. --start-mode stream with start saves, --init-seed)
            print(f"error: {e}", flush=True)
            return 2
        try:                            # refused before the run directory exists (isolation, blind gate)
            from sts2vec.protocol import SeedSetError

            from .isolation import IsolationGuard, check_blind_eval, check_legacy_pool
            from .ppo import eval_seedsets

            for name in eval_seedsets(cfg):     # --eval-every's set and --eval-at's set
                check_blind_eval(name, allow_blind=cfg.allow_blind, purpose=cfg.blind_purpose)
            if cfg.start_mode == "pool":
                check_legacy_pool(cfg, IsolationGuard.from_seedsets(extra=eval_seedsets(cfg)))
        except (IsolationError, SeedSetError) as e:     # SeedSetError: also BlindSetError, an unknown eval set
            print(f"error: {e}", flush=True)
            return 2
        log.open(Path(cfg.out) / "train.log")
        if cfg.objective == "shaped":
            log("NOTE: --shaped is the LEGACY smoke objective (floor-reached shaping); the default is --objective protocol.")
        log(f"run directory: {cfg.out}")
        try:
            s = train(cfg, log=log, resume=ck, resume_path=ck_path, given=given)
        except (BigLocalRunError, SpecMismatch, IsolationError, BlindSetError) as e:
            log(f"error: {e}")
            return 2
        try:
            _plot(Path(cfg.out))
        except ImportError:
            pass
        log(json.dumps({k: v for k, v in s.items() if k not in ("config",)}, indent=1, default=str))
        return 0
    if a.cmd in ("eval", "evaluate"):
        from sts2vec.protocol import BlindSetError

        from .evaluate import evaluate
        from .features import SpecMismatch

        if a.repeats < 1 or (a.repeats > 1 and a.policy != "sample"):
            ap.error("--repeats must be >= 1, and > 1 only with --policy sample (greedy plays are identical)")
        if a.chunk is not None:
            ci, _, cn = a.chunk.partition("/")
            if not (ci.isdigit() and cn.isdigit() and int(ci) < int(cn)):
                ap.error(f"--chunk {a.chunk}: expected I/N with 0 <= I < N")
        out = a.out or f"tmp/eval/nn-{Path(a.checkpoint).stem}-{Path(a.seedset).stem}-{time.strftime('%Y%m%d-%H%M%S')}"
        try:
            rep = evaluate(a.seedset, a.checkpoint, out, threads=a.threads, device=a.device,
                           loop_breaker=a.loop_breaker == "on", loop_breaker_k=a.loop_breaker_k, policy=a.policy,
                           sample_seed=a.sample_seed, repeats=a.repeats, chunk=a.chunk,
                           log=lambda m: print(m, flush=True), allow_blind=a.allow_blind, blind_purpose=a.purpose)
        except (SpecMismatch, BlindSetError) as e:
            print(f"error: {e}", flush=True)
            return 2
        h = rep["headline"]
        print(json.dumps({"out": out, "progress_mean": h["progress_mean"], "progress_ci95": h["progress_ci95"],
                          "wins": h["wins"], "games": h["games"], "results": h["results"],
                          "floor_mean": h["floor_mean"], "act_reached": h["act_reached"],
                          "replayable": h["replayable"],
                          **({"loop_breaker": lb["mode"], "loop_breaks": lb["loop_breaks"],
                              "no_op_actions": lb["no_op_actions"], "cycle_revisits": lb["cycle_revisits"]}
                             if (lb := rep.get("loop_breaker")) else {}),
                          **({"eval_mode": rep["eval_mode"]["name"]} if "eval_mode" in rep else {}),
                          **({"cycles": {k: cy[k] for k in ("cycles", "cycles_rest", "cycles_shop",
                                                            "games_with_cycle", "stuck")}}
                             if (cy := rep.get("cycles")) else {}),
                          **({"repeats": {k: rep["repeats"][k] for k in ("repeats", "plays", "progress_mean_pooled",
                                                                         "runs")}} if "repeats" in rep else {}),
                          **({"head_readout": rep["head_readout"]} if "head_readout" in rep else {})}, indent=1))
        runs = rep["repeats"]["runs"] if "repeats" in rep else [{"replayable": h["replayable"], "games": h["games"]}]
        bad = ", ".join(f"{r['replayable']}/{r['games']}" for r in runs if r["replayable"] != r["games"])
        if bad:      # the replay audit failed: the score is not reproducible (sts2vec eval)
            print(f"error: replay audit failed: {bad} games replayable (report.md lists them)", file=sys.stderr,
                  flush=True)
            return 1
        return 0
    if a.cmd == "plot":
        _plot(Path(a.out))
        return 0
    if a.cmd == "compare":
        from .compare import compare, render

        if len(a.arms) < 2:
            ap.error("compare needs at least two arms")
        try:
            r = compare(a.arms, [x for x in (a.labels or "").split(",") if x] or None)
        except (ValueError, FileNotFoundError) as e:
            print(f"error: {e}", file=sys.stderr, flush=True)
            return 2
        print(render(r))
        if a.json:
            Path(a.json).write_text(json.dumps(r, indent=1) + "\n", encoding="utf-8")
        return 0
    return 2


if __name__ == "__main__":
    sys.exit(main())
