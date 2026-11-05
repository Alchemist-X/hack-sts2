"""Model card and weights bundle of a checkpoint: ``sts2train report-card``, ``sts2train pack``, ``sts2train probe``.

``report_card`` (``sts2train report-card``). Per seed set (default ``silent-a10-v1``, ``silent-a10-dev-v1``,
``silent-a10-test-v1``): the checkpoint's greedy protocol evaluation (``sts2train.evaluate``: loop breaker on,
protocol limits) in ``OUT/evals/<seed set>/``, reused when that directory already holds a finished evaluation of the
same weights, seed set, library, limits and loop breaker (else set aside as ``*.stale-<time>`` and re-run); the rule
baselines ``random`` / ``first`` / ``heuristic`` (``sts2vec eval``, policy seed 0) in a cache shared by every card
(``baseline_cache``, default ``tmp/eval/baselines``, one directory per policy, seed set and library); the run
directories of ``python -m sts2train.lookahead eval`` given (``lookahead_dirs``, read only; ``lookahead_row``): a search
run is a "lookahead, determinized" row with its search declaration and counters (``stats.json``), paired per seed
(``lookahead.paired``) against a ``--greedy`` run of the same weights (loop breaker off, like the lookahead outside
its search), else against the card's greedy row; a ``--greedy`` run is an "NN greedy, loop breaker off" row; a seed
set outside ``seedsets`` gets its own section with the greedy row and the baselines. Writes ``OUT/model-card.md`` and
``OUT/model-card.json``:

- checkpoint: file, sha256, weights_hash (recomputed from the stored weights and checked against the record), update,
  env steps, episodes, encoder_version of the checkpoint and of the library (``features.check_spec_compat``:
  identical or widened model inputs), architecture and preset, aux heads, parameter count, the training flags of the
  checkpoint's config (a flag the config predates is shown at its default, marked);
- per seed set and policy: the protocol v0.2 headline (``sts2vec.protocol.aggregate``: progress mean [95% CI], win
  rate [Wilson 95%], floor, results, replay audit), the staged table (``stages``; each stage count / N [Wilson 95%]),
  the floor histogram, how the games ended (deaths by encounter type: boss / elite / normal / event fight; stuck,
  capped, crashed), deck size and relics at death when the summaries carry them, the win-probability head's readouts
  at the first and the last decision (aux heads only; a calibration hint, not a calibration test).

Stages (the act / floor bookkeeping of ``sts2vec.protocol.compute``: k = rooms entered in the act, L = its rooms, the
boss room is k = L): *reached the Act-N boss* = entered act N's boss room or got further; *beat the Act-N boss* = left
act N (reached act N + 1 or won), or ended ``stuck`` in its boss room (the floor step cap counts only non-combat
decisions, which a boss room offers only after the fight is won; a ``capped`` game there is not counted); *reached
Act N* = final act >= N or won; *cleared* = won.

Execution modes (``modes``, ``sts2train report-card --modes greedy-on,greedy-off,sample-off``; default greedy-on
alone, the card as before): every further mode of ``sts2train.evaluate`` (greedy / sampled one play per game with
``sample_seed``, loop breaker on / off) is an "NN <mode>" row computed or reused in ``OUT/evals/<seed set>@<mode>/``,
paired per seed (``lookahead.paired``) against the card's NN greedy row (loop breaker on), with its stalls, UI revisit
cycles (``sts2train.cycles``) and loop breaks.

``pack`` (``sts2train pack``): a tar.gz ``<name>/`` with the checkpoint file as is, its spec and config, the model
card (``card_dir``: reused when its checkpoint sha256 matches, else made there), the library's encoder_version / ABI
version, ``README-weights.md`` (commands: verify, evaluate, re-make the card, probe, run it in the real game through
the live twin runner ``scripts/live/nn_twin_play.py``) and a sha256 manifest (``manifest.json`` with sizes,
``SHA256SUMS`` for ``shasum -a 256 -c``); the written archive is re-read and checked (``verify_bundle``).

``probe`` (``sts2train probe``): replay games of a protocol run directory from their recorded actions and read the
checkpoint at every decision: the action taken, the model's top candidates with probabilities, the value head and,
with aux heads, win_prob / combat_hp_pred (one JSON row per decision; a text table on stdout).
"""

from __future__ import annotations

import hashlib
import io
import json
import statistics
import tarfile
import time
from collections import Counter
from dataclasses import asdict
from pathlib import Path
from typing import Callable

import numpy as np
import torch
from sts2vec import protocol as PG
from sts2vec.evaluate import FLOOR_STEP_CAP, STEP_CAP

from .lookahead import paired as paired_runs
from .loopbreak import DEFAULT_K
from .model import ARCH_KEYS, PRESETS, arch_of, param_count
from .runlog import atomic_write_bytes, atomic_write_json

CARD_VERSION = 1
DEFAULT_SEEDSETS = ("silent-a10-v1", "silent-a10-dev-v1", "silent-a10-test-v1")
BASELINES = ("random", "first", "heuristic")
DEFAULT_BASELINE_CACHE = "tmp/eval/baselines"
LIMITS = {"step_cap": STEP_CAP, "floor_step_cap": FLOOR_STEP_CAP}
LIVE_TWIN = "scripts/live/nn_twin_play.py"
GREEDY_LABEL = "NN greedy"
LOOKAHEAD_LABEL = "lookahead, determinized"
DETAIL_KINDS = ("nn", "nn-mode", "greedy-ref", "lookahead")  # rows with the per-game tables (floor, endings, ...)
DEFAULT_MODES = ("greedy-on",)
SEARCH_STAT_KEYS = ("searched", "live_decisions", "changed", "rollout_errors", "no_valid_candidate", "desyncs",
                    "rebuilds", "api", "wall_seconds", "torch_threads")
SEARCH_DERIVED_KEYS = ("changed_rate", "searched_share_of_live", "ms_per_searched_decision")
PAIRED_KEYS = ("games", "delta_mean", "delta_sd", "t_ci95", "bootstrap_ci95", "bootstrap_resamples", "better", "worse",
               "same")

STAGES = (
    ("act1_boss", "reached the Act-1 boss"),
    ("act1_boss_beaten", "beat the Act-1 boss"),
    ("act2", "reached Act 2"),
    ("act2_boss", "reached the Act-2 boss"),
    ("act2_boss_beaten", "beat the Act-2 boss"),
    ("act3", "reached Act 3"),
    ("cleared", "cleared (won the run)"),
)
ENDINGS = (
    ("won", "won"),
    ("died_boss", "died: boss"),
    ("died_elite", "died: elite"),
    ("died_normal", "died: normal fight"),
    ("died_event", "died: event fight"),
    ("died_unknown", "died: no encounter recorded"),
    ("stuck", "stuck (floor step cap)"),
    ("capped", "capped (step cap)"),
    ("crashed", "crashed"),
    ("error", "error"),
    ("aborted", "aborted"),
    ("unknown", "no result recorded"),
)
# summary keys read for "deck size / relics at death" (top level or under `death`); sts2vec eval records none of them
DECK_KEYS = ("deck_n", "deck_size", "final_deck_n")
RELIC_KEYS = ("relic_n", "relic_count", "relics", "final_relic_n")
# training flags shown on the card, grouped (the JSON keeps the whole config)
FLAG_GROUPS = (
    ("objective", ("objective", "win_bonus", "acq_shaping", "cycle_cost")),
    ("starts", ("characters", "ascension", "pool", "start_saves", "save_mix", "save_copies", "harvest", "harvest_prob",
                "harvest_max_per_step", "harvest_start_share")),
    ("PPO", ("envs", "rollout", "epochs", "minibatch", "lr", "gamma", "lam", "clip", "vf_coef", "ent_coef",
             "max_grad_norm", "max_rows")),
    ("model flags", ("aux_heads", "aux_coef", "deck_counts")),
    ("rollouts", ("rollout_loop_breaker", "step_cap", "floor_step_cap")),
    ("runtime", ("device", "amp", "compile", "seed", "max_hours")),
    ("training evals", ("eval_seedset", "eval_every", "loop_breaker", "eval_at", "no_best")),
)


def mode_label(mode: str, sample_seed: int) -> str:
    """Row label of an execution mode (``sts2train.evaluate.parse_mode`` names)."""
    pol, _, br = mode.partition("-")
    what = GREEDY_LABEL if pol == "greedy" else f"NN sampled (seed {int(sample_seed)}, 1 play)"
    return f"{what}, loop breaker {br}"


class BundleError(ValueError):
    """A weights bundle whose files do not match its manifest."""


# ------------------------------------------------------------------------------------------------ per-game derivation


def _int_keys(d) -> dict[int, int]:
    return {int(k): v for k, v in (d or {}).items()}


def _mean(xs):
    xs = [x for x in xs if x is not None]
    return statistics.fmean(xs) if xs else None


def act_frame(s: dict, act: int, ascension: int) -> tuple[int, int]:
    """(base, L) of `act` in a game summary, as ``protocol.compute`` scores it: base = total floor at the act's first
    observation (act 1: 0), L = rooms in the act (its last map, else the standard table). The act's boss room is
    floor base + L (A10 act 3: the second boss; the first is one room earlier)."""
    starts, rooms = _int_keys(s.get("act_start_floor")), _int_keys(s.get("act_rooms"))
    L = rooms.get(act) or PG.default_act_rooms(act, ascension)
    if act == 1:
        base = 0
    elif act in starts:
        base = starts[act]
    else:
        base = sum(rooms.get(b) or PG.default_act_rooms(b, ascension) for b in range(1, act))
    return int(base), int(L)


def stages(s: dict, ascension: int | None = None) -> dict[str, bool]:
    """The staged milestones of one game summary (keys of ``STAGES``; definitions in the module docstring)."""
    asc = int(s.get("ascension") or 0) if ascension is None else int(ascension)
    res = s.get("result")
    won = res == "win"
    act, floor = int(s.get("act") or 0), int(s.get("floor") or 0)

    def boss_room(a: int) -> bool:
        if won or act > a:
            return True
        if act < a:
            return False
        base, L = act_frame(s, a, asc)
        return floor - base >= L

    def beaten(a: int) -> bool:
        return won or act > a or (act == a and res == "stuck" and boss_room(a))

    return {"act1_boss": boss_room(1), "act1_boss_beaten": beaten(1), "act2": won or act >= 2,
            "act2_boss": boss_room(2), "act2_boss_beaten": beaten(2), "act3": won or act >= 3, "cleared": won}


def encounter_type(name: str | None) -> str:
    """boss / elite / event / normal from the encounter id's suffix (``*_BOSS``, ``*_ELITE``, ``*_EVENT_ENCOUNTER``;
    everything else, ``*_NORMAL`` / ``*_WEAK`` included, is a normal fight); unknown without a name."""
    n = (name or "").upper()
    if n in ("", "?"):
        return "unknown"
    if n.endswith("_BOSS"):
        return "boss"
    if n.endswith("_ELITE"):
        return "elite"
    if "_EVENT" in n:
        return "event"
    return "normal"


def ending(s: dict) -> tuple[str, str | None]:
    """How a game ended: (key of ``ENDINGS``, detail): the encounter of a death, the phase of a stuck game."""
    r = s.get("result")
    if r == "win":
        return "won", None
    if r == "loss":
        enemies = (s.get("death") or {}).get("enemies") or []
        name = enemies[0] if enemies else None
        return f"died_{encounter_type(name)}", name
    if r == "stuck":
        return "stuck", (s.get("stuck") or {}).get("phase")
    return (r if r in ("capped", "crashed", "error", "aborted") else "unknown"), None


# ------------------------------------------------------------------------------------------------ tables


def wilson_row(count: int, games: int) -> dict:
    lo, hi = PG.wilson(count, games)
    return {"count": int(count), "games": int(games), "rate": count / games if games else None, "ci95": [lo, hi]}


def staged_table(summaries: list[dict], ascension: int | None = None) -> dict:
    """{stage: {label, count, games, rate, ci95 (Wilson 95%)}} in ``STAGES`` order."""
    flags = [stages(s, ascension) for s in summaries]
    return {key: {"label": label, **wilson_row(sum(f[key] for f in flags), len(summaries))} for key, label in STAGES}


def floor_histogram(summaries: list[dict]) -> list[dict]:
    """Games per final (act, floor), in order."""
    c = Counter((int(s.get("act") or 0), int(s["floor"])) for s in summaries if s.get("floor") is not None)
    return [{"act": a, "floor": f, "games": k} for (a, f), k in sorted(c.items())]


def endings_table(summaries: list[dict]) -> dict:
    """{ending: {label, games, share, detail: [{name, games}] most common first}} for the endings that occur."""
    n = len(summaries)
    groups: dict[str, Counter] = {}
    for s in summaries:
        key, detail = ending(s)
        groups.setdefault(key, Counter())[detail] += 1
    out = {}
    for key, label in ENDINGS:
        if key in groups:
            c = groups[key]
            k = sum(c.values())
            out[key] = {"label": label, "games": k, "share": k / n if n else None,
                        "detail": [{"name": d, "games": m} for d, m in c.most_common() if d is not None]}
    return out


def _count(s: dict, keys) -> float | None:
    for src in (s.get("death") or {}, s):
        for k in keys:
            v = src.get(k)
            if isinstance(v, (list, tuple)):
                return float(len(v))
            if isinstance(v, (int, float)) and not isinstance(v, bool):
                return float(v)
    return None


def at_death(summaries: list[dict]) -> dict | None:
    """Mean deck size / relic count of the lost games when their summaries carry them (``DECK_KEYS`` /
    ``RELIC_KEYS``), else None (the card then omits it)."""
    losses = [s for s in summaries if s.get("result") == "loss"]
    deck = [v for s in losses if (v := _count(s, DECK_KEYS)) is not None]
    relics = [v for s in losses if (v := _count(s, RELIC_KEYS)) is not None]
    if not deck and not relics:
        return None
    return {"deaths": len(losses), "deck_mean": _mean(deck), "deck_games": len(deck), "relics_mean": _mean(relics),
            "relic_games": len(relics)}


def win_prob_readout(summaries: list[dict]) -> dict | None:
    """The aux win-probability head at each game's first and last decision (``sts2train eval`` writes ``win_prob``
    per decision and ``win_prob_first`` / ``win_prob_last``): mean at the first decision next to the observed win
    rate, mean at the last decision of lost games (just before the death), of won games and of the rest. None when no
    summary has a readout."""
    rows = []
    for s in summaries:
        wp = s.get("win_prob") or []
        first = s.get("win_prob_first", wp[0] if wp else None)
        last = s.get("win_prob_last", wp[-1] if wp else None)
        if first is not None or last is not None:
            rows.append((s.get("result"), first, last))
    if not rows:
        return None
    out = {"games": len(rows), "first_mean": _mean([f for _, f, _ in rows]),
           "observed_win_rate": sum(r == "win" for r, _, _ in rows) / len(rows), "last": {}}
    for group, keep in (("loss", lambda r: r == "loss"), ("win", lambda r: r == "win"),
                        ("other", lambda r: r not in ("loss", "win"))):
        xs = [last for r, _, last in rows if keep(r) and last is not None]
        if xs:
            out["last"][group] = {"mean": statistics.fmean(xs), "games": len(xs)}
    return out


HEADLINE_KEYS = ("games", "results", "wins", "win_rate", "win_rate_ci95", "progress_mean", "progress_ci95",
                 "progress_median", "floor_mean", "floor_median", "floor_max", "act_reached", "steps_mean",
                 "turns_mean", "illegal_total", "replayable")


def run_stats(summaries: list[dict], ascension: int) -> dict:
    """Every table of one policy on one seed set from its game summaries (``protocol.load_summaries`` form)."""
    h = PG.aggregate(summaries, ascension)
    return {"headline": {k: h[k] for k in HEADLINE_KEYS}, "staged": staged_table(summaries, ascension),
            "floor_histogram": floor_histogram(summaries), "endings": endings_table(summaries),
            "at_death": at_death(summaries), "win_prob": win_prob_readout(summaries)}


# ------------------------------------------------------------------------------------------------ run directories


def _read_json(p: Path):
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def read_run(run_dir) -> tuple[dict, list[dict], dict]:
    """(config, summaries of its seed set's games with progress recomputed, report) of a protocol run directory."""
    d = Path(run_dir)
    cfg = _read_json(d / "config.json") or {}
    rep = _read_json(d / "report.json") or {}
    summaries = PG.load_summaries(d)
    keys = {PG.game_key(g) for g in (cfg.get("seedset") or {}).get("games") or []}
    if keys:
        summaries = [s for s in summaries if (s.get("key") or f"{s.get('character')}-{s.get('seed')}") in keys]
    return cfg, summaries, rep


def stale_reason(run_dir, ss: dict, *, library_version: str, weights_hash: str | None = None,
                 policy: str | None = None, loop_breaker: str | None = None, eval_mode: str | None = None,
                 sample_seed: int | None = None) -> str | None:
    """None when `run_dir` holds a finished protocol evaluation of `ss` (same content) on `library_version` with the
    protocol limits (and these weights / this policy with eval seed 0 / this loop breaker mode), every game
    summarised; else why it cannot be reused."""
    d = Path(run_dir)
    cfg = _read_json(d / "config.json")
    if cfg is None:
        return "no evaluation there yet"
    if _read_json(d / "report.json") is None:
        return "unfinished (no report.json)"
    try:
        same = PG.checksum(cfg.get("seedset") or {}) == PG.checksum(ss)
    except (KeyError, TypeError):
        same = False
    if not same:
        return "another seed set"
    if cfg.get("limits") != LIMITS:
        return f"limits {cfg.get('limits')}, not {LIMITS}"
    if cfg.get("server_version") != library_version:
        return f"played on {cfg.get('server_version')}, the library is {library_version}"
    if weights_hash is not None and cfg.get("weights_hash") != weights_hash:
        return f"weights {cfg.get('weights_hash')}, not {weights_hash}"
    if policy is not None and (cfg.get("policy") != policy or cfg.get("eval_seed", 0) != 0):
        return f"policy {cfg.get('policy')} seed {cfg.get('eval_seed')}"
    if loop_breaker is not None:
        lb = cfg.get("loop_breaker") or {}
        if lb.get("mode") != loop_breaker or (loop_breaker == "on" and lb.get("k") != DEFAULT_K):
            return f"loop breaker {lb.get('mode')} k {lb.get('k')}"
    if eval_mode is not None:            # an execution-mode row (sts2train.evaluate): the same mode, seed and play
        m = cfg.get("eval_mode") or {}
        if m.get("name") != eval_mode:
            return f"eval mode {m.get('name')}, not {eval_mode}"
        if m.get("policy") == "sample" and (m.get("sample_seed") != sample_seed or m.get("repeat") != 0):
            return f"sample seed {m.get('sample_seed')} repeat {m.get('repeat')}, not {sample_seed} / 0"
    missing = sum(not (d / "games" / f"{PG.game_key(g)}.summary.json").exists() for g in ss["games"])
    if missing:
        return f"{missing} game summaries missing"
    return None


def _set_aside(d: Path, log) -> None:
    if d.exists() and any(d.iterdir()):
        dst = d.with_name(f"{d.name}.stale-{time.strftime('%Y%m%d-%H%M%S')}")
        d.rename(dst)
        log(f"moved the old {d} to {dst}")


def seedset_id(ss: dict) -> str:
    return str(ss.get("id") or ss.get("name") or PG.checksum(ss)[:16])


def _dirname(s: str) -> str:
    return "".join(ch if ch.isalnum() or ch in "-_." else "_" for ch in s)


def run_entry(run_dir, kind: str, label: str, ascension: int, *, reused: bool, weights_hash: str | None = None,
              library_version: str | None = None) -> dict:
    """One policy row of a seed set section: identity of the run directory + ``run_stats``; `notes` flag a run whose
    weights are not the card's (`weights_hash`), that was played on another library, or that misses games."""
    d = Path(run_dir)
    cfg, summaries, rep = read_run(d)
    expected = len((cfg.get("seedset") or {}).get("games") or [])
    notes = []
    if weights_hash is not None and cfg.get("weights_hash") != weights_hash:
        notes.append(f"played by weights {cfg.get('weights_hash')}, not this checkpoint's {weights_hash}")
    if library_version is not None and cfg.get("server_version") != library_version:
        notes.append(f"played on {cfg.get('server_version')} (the card's library is {library_version})")
    if expected and len(summaries) != expected:
        notes.append(f"{len(summaries)}/{expected} games summarised")
    e = {"label": label, "kind": kind, "policy": cfg.get("policy"), "dir": str(d.resolve()), "reused": reused,
         "weights_hash": cfg.get("weights_hash"), "server_version": cfg.get("server_version"),
         "repo_sha": cfg.get("repo_sha"), "wall_seconds": (_read_json(d / "run.json") or {}).get("wall_seconds"),
         "loop_breaker": rep.get("loop_breaker"), "cycles": rep.get("cycles"), "eval_mode": rep.get("eval_mode"),
         "notes": notes, **run_stats(summaries, ascension)}
    if kind == "lookahead":
        st = _read_json(d / "stats.json") or {}
        dv = st.get("derived") or {}
        e.update(search=cfg.get("search"), information=cfg.get("information"),
                 search_stats={**{k: st.get(k) for k in SEARCH_STAT_KEYS},
                               **{k: dv.get(k) for k in SEARCH_DERIVED_KEYS}, "code": st.get("code")} if st else None)
    return e


def lookahead_row(cfg: dict, run_dir) -> tuple[str, str]:
    """(kind, label) of a ``python -m sts2train.lookahead eval`` run directory from its config.json: a search run
    (``search`` declared) -> ``lookahead``, "lookahead, determinized: la(K,R,horizon,honest) <score>"; a ``--greedy``
    run (an ``sts2train eval`` directory: ``greedy``, policy ``nn:...``) -> ``greedy-ref``, "NN greedy, loop breaker
    <mode>" (a run that predates the loop breaker records none: the plain argmax, off). Anything else: ValueError."""
    pol = str(cfg.get("policy") or "")
    if cfg.get("search"):
        sc = cfg["search"].get("score") if isinstance(cfg["search"], dict) else None
        label = f"{LOOKAHEAD_LABEL}: {pol.split('+', 1)[1]}" if "+" in pol else LOOKAHEAD_LABEL
        return "lookahead", f"{label} {sc}" if sc else label
    if cfg.get("greedy") is True and pol.startswith("nn:"):
        return "greedy-ref", f"{GREEDY_LABEL}, loop breaker {(cfg.get('loop_breaker') or {}).get('mode') or 'off'}"
    raise ValueError(f"{run_dir}: not a `python -m sts2train.lookahead eval` run directory (policy {pol or '?'}: "
                     "neither a search run nor a --greedy run)")


def paired_against(la: dict, runs: list[dict]) -> dict | None:
    """Per-seed progress difference of the lookahead row `la` against its like-for-like greedy row of `runs`: a
    ``greedy-ref`` row of the same weights with the loop breaker off (the lookahead's choices outside the search are
    the plain argmax), else the card's greedy row (loop breaker on: the difference then mixes the search with the loop
    breaker; ``like_for_like`` False)."""
    same = [r for r in runs if r["kind"] == "greedy-ref" and r["weights_hash"] == la["weights_hash"]
            and ((r.get("loop_breaker") or {}).get("mode") or "off") == "off"]
    ref = same[0] if same else next((r for r in runs if r["kind"] == "nn"), None)
    if ref is None:
        return None
    out = {"vs": ref["label"], "vs_kind": ref["kind"], "like_for_like": bool(same)}
    try:
        p = paired_runs(ref["dir"], la["dir"])
    except (OSError, ValueError, KeyError) as e:
        return {**out, "error": str(e)}
    return {**out, **{k: p[k] for k in PAIRED_KEYS}}


# ------------------------------------------------------------------------------------------------ checkpoint identity


def file_sha256(path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def state_weights_hash(state: dict) -> str:
    """``ppo.weights_hash`` of a model whose state_dict is `state` (a checkpoint's ``model``)."""
    h = hashlib.sha256()
    for k, v in sorted(state.items()):
        h.update(k.encode())
        h.update(v.detach().cpu().contiguous().numpy().tobytes())
    return h.hexdigest()[:16]


def preset_of(arch: dict) -> str:
    for name, p in PRESETS.items():
        if all(arch.get(k) == p[k] for k in ARCH_KEYS):
            return name
    return "custom"


def training_flags(config: dict) -> list[dict]:
    """``FLAG_GROUPS`` of a checkpoint config: [{group, flags: [{name, value, recorded}]}]; a flag the config does not
    record (the trainer that wrote it predates the flag) carries the TrainConfig default and recorded False."""
    from .ppo import TrainConfig

    defaults = asdict(TrainConfig())
    out = []
    for group, names in FLAG_GROUPS:
        flags = [{"name": k, "value": config[k] if k in config else defaults.get(k), "recorded": k in config}
                 for k in names if k in config or k in defaults]
        out.append({"group": group, "flags": flags})
    return out


def checkpoint_identity(path, ck: dict, model=None) -> dict:
    p = Path(path)
    arch = arch_of(ck.get("arch") or ck.get("config"))
    recorded = ck.get("weights_hash")
    computed = state_weights_hash(ck["model"])
    counters = ck.get("counters") or {}
    config = ck.get("config") or {}
    return {
        "file": str(p.resolve()), "name": p.name, "bytes": p.stat().st_size, "sha256": file_sha256(p),
        "weights_hash": recorded or computed, "weights_hash_computed": computed,
        "weights_hash_ok": recorded is None or recorded == computed, "format": ck.get("format"),
        "update": ck.get("updates"), "env_steps": ck.get("env_steps"), "episodes": counters.get("episodes"),
        "train_wins": counters.get("wins"), "sessions": counters.get("sessions"),
        "train_hours": round(counters["wall_s"] / 3600.0, 2) if counters.get("wall_s") is not None else None,
        "saved_at": ck.get("saved_at"),
        "encoder_version": (ck.get("spec") or {}).get("encoder_version") or ck.get("encoder_version"),
        "arch": arch, "preset": preset_of(arch), "aux_heads": bool(arch.get("aux_heads")),
        "heads": sorted(ck.get("heads") or {}), "deck_counts": bool(arch.get("deck_counts")),
        "params": ck.get("params") or (param_count(model) if model is not None else None),
        "resumable": "optimizer" in ck, "best_eval": ck.get("best_eval"), "last_eval": ck.get("last_eval"),
        "config": config, "training_flags": training_flags(config),
    }


def library_info() -> dict:
    from sts2vec import ABI_VERSION, load, schema_of

    lib = load()
    return {"version": lib.version(), "encoder_version": schema_of(lib).encoder_version,
            "abi_version": lib.abi_version, "python_abi_version": ABI_VERSION, "mode": getattr(lib, "mode", None),
            "path": str(lib.path)}


def _now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%S%z")


# ------------------------------------------------------------------------------------------------ report card


def report_card(checkpoint, out, *, seedsets=DEFAULT_SEEDSETS, threads: int = 4, device: str = "cpu",
                lookahead_dirs=(), baselines=BASELINES, baseline_cache=DEFAULT_BASELINE_CACHE,
                modes=DEFAULT_MODES, sample_seed: int = 1, log: Callable[[str], None] = print,
                allow_blind: bool = False, blind_purpose: str | None = None) -> dict:
    """Run or reuse the evaluations and write ``out/model-card.json`` + ``out/model-card.md``; -> the card.

    Raises `features.SpecMismatch` (before any game) when the checkpoint cannot drive this library, and
    `sts2vec.protocol.BlindSetError` (before any game) when a seed set (given, or embedded in a lookahead run) holds
    games of a frozen blind set unless `allow_blind` and `blind_purpose` are given; each allowed blind set is then
    logged to ``out/blind-access.jsonl`` (and by every evaluation into its own directory)."""
    from sts2vec import load, schema_of
    from sts2vec.evaluate import evaluate as vec_evaluate
    from sts2vec.evaluate import repo_sha

    from .evaluate import checkpoint_macros, evaluate_model, parse_mode
    from .features import ModelSpec
    from .ppo import load_checkpoint

    extra_modes = [m for m in dict.fromkeys(modes or ()) if m != "greedy-on"]
    for m in extra_modes:
        parse_mode(m)                       # ValueError before any game on an unknown mode
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    lib = library_info()
    model, spec, ck = load_checkpoint(checkpoint, device, library=ModelSpec.from_schema(schema_of(load())))
    ident = checkpoint_identity(checkpoint, ck, model)
    if not ident["weights_hash_ok"]:
        log(f"WARNING: {checkpoint}: recorded weights_hash {ck.get('weights_hash')} != the stored weights' "
            f"{ident['weights_hash_computed']}")
    wh = ident["weights_hash"]

    sets: list[dict] = []

    def add_set(ss: dict) -> None:
        if all(PG.checksum(x) != PG.checksum(ss) for x in sets):
            sets.append(ss)

    refs: dict[str, str] = {}

    def ref_of(ss: dict, given: str | None) -> str | None:
        if given is not None:          # a name / id as given, a file as its absolute path
            return str(Path(given).resolve()) if Path(given).exists() else str(given)
        try:                           # a lookahead run's embedded seed set: the committed set with that content
            return seedset_id(ss) if PG.checksum(PG.load_seedset(seedset_id(ss))) == PG.checksum(ss) else None
        except (PG.SeedSetError, KeyError, TypeError):
            return None

    for name in seedsets:
        ss = PG.load_seedset(str(name))
        refs.setdefault(PG.checksum(ss), ref_of(ss, str(name)))
        add_set(ss)
    lookahead = []
    for d in lookahead_dirs or ():
        cfg = _read_json(Path(d) / "config.json")
        if not cfg or not cfg.get("seedset"):
            raise ValueError(f"{d}: not a protocol run directory (no config.json with a seed set)")
        kind, label = lookahead_row(cfg, d)
        ss = PG.validate_seedset(dict(cfg["seedset"]), str(d))
        lookahead.append((Path(d), kind, label, ss))
        refs.setdefault(PG.checksum(ss), ref_of(ss, None))
        add_set(ss)

    from .isolation import check_blind_eval

    blind = [ss for ss in sets if PG.blind_overlap(ss)]
    for ss in blind:                    # refuse (or log) before any game, the cached / reused runs included
        check_blind_eval(ss, allow_blind=allow_blind, purpose=blind_purpose, log_dir=out, weights_hash=wh)
    sections = []
    used: set[str] = set()
    for ss in sets:
        sid, asc, sha = seedset_id(ss), int(ss["ascension"]), PG.checksum(ss)
        name = _dirname(sid) if _dirname(sid) not in used else f"{_dirname(sid)}-{sha[:12]}"
        used.add(name)
        runs = []
        d = out / "evals" / name
        reason = stale_reason(d, ss, library_version=lib["version"], weights_hash=wh, loop_breaker="on")
        if reason is None:
            log(f"[{sid}] greedy eval: reusing {d}")
        else:
            log(f"[{sid}] greedy eval -> {d} ({reason})")
            _set_aside(d, log)
            evaluate_model(model, spec, ss, str(d), weights_hash=wh, checkpoint=str(checkpoint), threads=threads,
                           device=device, spec_compat=ck.get("spec_compat"), loop_breaker=True,
                           loop_breaker_k=DEFAULT_K, log=log, allow_blind=allow_blind, blind_purpose=blind_purpose,
                           macros=checkpoint_macros(ck) or None)
        runs.append(run_entry(d, "nn", GREEDY_LABEL, asc, reused=reason is None, weights_hash=wh,
                              library_version=lib["version"]))
        for m in extra_modes:               # execution modes: paired per seed against the NN greedy row
            pol, br = parse_mode(m)
            md = out / "evals" / f"{name}@{m}"
            mr = stale_reason(md, ss, library_version=lib["version"], weights_hash=wh,
                              loop_breaker="on" if br else "off", eval_mode=m, sample_seed=int(sample_seed))
            if mr is None:
                log(f"[{sid}] {m} eval: reusing {md}")
            else:
                log(f"[{sid}] {m} eval -> {md} ({mr})")
                _set_aside(md, log)
                evaluate_model(model, spec, ss, str(md), weights_hash=wh, checkpoint=str(checkpoint), threads=threads,
                               device=device, spec_compat=ck.get("spec_compat"), loop_breaker=br,
                               loop_breaker_k=DEFAULT_K, policy=pol, sample_seed=int(sample_seed), log=log,
                               allow_blind=allow_blind, blind_purpose=blind_purpose,
                               macros=checkpoint_macros(ck) or None)
            row = run_entry(md, "nn-mode", mode_label(m, sample_seed), asc, reused=mr is None, weights_hash=wh,
                            library_version=lib["version"])
            try:
                p = paired_runs(d, md)
                row["paired"] = {"vs": GREEDY_LABEL, **{k: p[k] for k in PAIRED_KEYS}}
            except (OSError, ValueError, KeyError) as e:
                row["paired"] = {"vs": GREEDY_LABEL, "error": str(e)}
            runs.append(row)
        for ld, kind, label, lss in lookahead:
            if PG.checksum(lss) == sha:
                if any(r["label"] == label for r in runs):
                    label = f"{label} ({ld.name})"
                runs.append(run_entry(ld, kind, label, asc, reused=True, weights_hash=wh,
                                      library_version=lib["version"]))
        for r in runs:
            if r["kind"] == "lookahead":
                r["paired"] = paired_against(r, runs)
        for pol in baselines:
            bd = Path(baseline_cache) / _dirname(f"{pol}-{sid}-{sha[:12]}-{lib['encoder_version']}")
            reason = stale_reason(bd, ss, library_version=lib["version"], policy=pol)
            if reason is None:
                log(f"[{sid}] baseline {pol}: reusing {bd}")
            else:
                log(f"[{sid}] baseline {pol} -> {bd} ({reason})")
                _set_aside(bd, log)
                vec_evaluate(ss, pol, str(bd), threads=threads, seed=0, log=log, allow_blind=allow_blind,
                             purpose=blind_purpose)
            runs.append(run_entry(bd, "baseline", pol, asc, reused=reason is None, library_version=lib["version"]))
        ident_ss = PG.seedset_identity(ss)
        sections.append({"id": sid, "name": ss.get("name"), "role": ss.get("role"), "ascension": asc,
                         "content": ss.get("content"), "games": len(ss["games"]), "sha256": ident_ss["seedset_sha256"],
                         "frozen": ident_ss["seedset_frozen"], "ref": refs.get(sha),
                         "first_game": PG.game_key(ss["games"][0]), "runs": runs})

    card = {"card_version": CARD_VERSION, "generated_at": _now(), "generator": "sts2train report-card",
            "repo_sha": repo_sha(), "protocol_version": PG.PROTOCOL_VERSION,
            "track": "sim: sts2vec simulator, not the real engine", "checkpoint": ident, "library": lib,
            "spec_compat": ck.get("spec_compat"),
            "settings": {"threads": threads, "device": str(device), "greedy": True,
                         "modes": ["greedy-on", *extra_modes], "sample_seed": int(sample_seed),
                         "loop_breaker": {"mode": "on", "k": DEFAULT_K}, "limits": LIMITS,
                         "baselines": list(baselines), "baseline_seed": 0,
                         "baseline_cache": str(Path(baseline_cache).resolve())},
            "seedsets": sections}
    card = json.loads(json.dumps(card, default=str))
    atomic_write_json(out / "model-card.json", card)
    atomic_write_bytes(out / "model-card.md", render_md(card).encode("utf-8"))
    log(f"wrote {out / 'model-card.md'} and {out / 'model-card.json'}")
    return card


# ------------------------------------------------------------------------------------------------ markdown


def _f(x, nd: int = 3) -> str:
    if x is None:
        return "-"
    if isinstance(x, float):
        return f"{x:.{nd}f}"
    return str(x)


def _pct(x, nd: int = 1) -> str:
    return "-" if x is None else f"{100 * x:.{nd}f}%"


def _n(x) -> str:
    return "-" if x is None else f"{x:,}"


def _wilson_cell(row: dict) -> str:
    lo, hi = row["ci95"]
    return f"{row['count']}/{row['games']} [{_pct(lo)}, {_pct(hi)}]"


def _who(c: dict) -> str:
    cfg = c.get("config") or {}
    chars = list(cfg.get("characters") or [])
    who = "/".join(x.title() for x in chars) if chars and len(chars) < 5 else "all-character"
    return f"{who} A{cfg['ascension']}" if "ascension" in cfg else who


def _endings_detail(e: dict) -> str:
    parts = []
    for key, _ in ENDINGS:
        row = e.get(key)
        if row and row["detail"]:
            names = ", ".join(f"{x['name']} {x['games']}" for x in row["detail"][:6])
            more = len(row["detail"]) - 6
            parts.append(f"{row['label']}: {names}" + (f" (+{more} more)" if more > 0 else ""))
    return "; ".join(parts) or "-"


def render_md(card: dict) -> str:
    c, lib = card["checkpoint"], card["library"]
    lim = card["settings"]["limits"]
    L = [f"# Model card: {_who(c)} policy `{c['name']}` (weights `{c['weights_hash']}`)", "",
         f"sts2train PPO checkpoint at update {_n(c['update'])} ({_n(c['env_steps'])} env steps). Scores are the "
         f"STS2-Bench protocol v{card['protocol_version']} metric on the simulator ({card['track']}). The policy is "
         f"the checkpoint's greedy argmax with the loop breaker (`sts2train eval`), protocol limits (step cap "
         f"{lim['step_cap']}, floor step cap {lim['floor_step_cap']}); baselines are `sts2vec eval` rule policies "
         f"(seed {card['settings']['baseline_seed']}) on the same games.", "",
         f"Generated {card['generated_at']} by `sts2train report-card` (repo `{card['repo_sha']}`, library "
         f"`{lib['version']}`, ABI {lib['abi_version']}).", "",
         "## Results at a glance", "",
         "| seed set | policy | games | progress [95% CI] | win rate [Wilson 95%] | reached the Act-1 boss | "
         "reached Act 2 | reached Act 3 | floor mean | replay |",
         "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for sec in card["seedsets"]:
        for r in sec["runs"]:
            h = r["headline"]
            lo, hi = h["win_rate_ci95"]
            plo, phi = h["progress_ci95"]
            name = f"**{r['label']}**" if r["kind"] == "nn" else r["label"]
            L.append(f"| {sec['id']} | {name} | {h['games']} | {_f(h['progress_mean'])} [{_f(plo)}, {_f(phi)}] "
                     f"| {h['wins']}/{h['games']} [{_pct(lo)}, {_pct(hi)}] "
                     f"| {_wilson_cell(r['staged']['act1_boss'])} | {_wilson_cell(r['staged']['act2'])} "
                     f"| {_wilson_cell(r['staged']['act3'])} | {_f(h['floor_mean'], 1)} "
                     f"| {h['replayable']}/{h['games']} |")
    L += ["", "## Checkpoint", "", "| field | value |", "| --- | --- |",
          f"| file | `{c['name']}` ({c['bytes'] / 2**20:.1f} MiB"
          + (", with optimizer state: resumable) |" if c["resumable"] else ") |"),
          f"| sha256 | `{c['sha256']}` |",
          f"| weights_hash | `{c['weights_hash']}` "
          + ("(recomputed from the stored weights: matches) |" if c["weights_hash_ok"]
             else f"(**the stored weights hash to `{c['weights_hash_computed']}`**) |"),
          f"| update / env steps / episodes | {_n(c['update'])} / {_n(c['env_steps'])} / {_n(c['episodes'])}"
          + (f" (training wins {_n(c['train_wins'])}, {_n(c['sessions'])} sessions, {_f(c['train_hours'], 2)} h)"
             if c["episodes"] is not None else "") + " |",
          f"| saved at | {c['saved_at'] or '-'} |"]
    sc = card.get("spec_compat") or {}
    enc = f"checkpoint `{c['encoder_version']}`, library `{lib['encoder_version']}`"
    if sc.get("widened_enums"):
        w = ", ".join(f"{k} {v['from']} -> {v['to']}" for k, v in sc["widened_enums"].items())
        enc += f": model inputs widened ({w}; the new rows are zero)"
    elif sc:
        enc += ": model inputs identical"
    a = c["arch"]
    L += [f"| encoder_version | {enc} |",
          f"| architecture | {c['preset']} preset: d_model {a.get('d')}, state {a.get('s')}, depth {a.get('depth')}, "
          f"heads {a.get('heads')}, ff_mult {a.get('ff_mult')}; deck_counts {'on' if c['deck_counts'] else 'off'} |",
          f"| aux heads | {', '.join(c['heads']) if c['heads'] else ('on' if c['aux_heads'] else 'none')} |",
          f"| parameters | {_n(c['params'])} |"]
    for key in ("best_eval", "last_eval"):
        e = c.get(key)
        if key == "last_eval" and e == c.get("best_eval"):
            continue
        if e:
            plo, phi = e.get("progress_ci95") or (None, None)
            what = "best_eval = last_eval" if key == "best_eval" and e == c.get("last_eval") else key
            L.append(f"| training-time {what} | update {e.get('update')}, {e.get('seedset')}: progress "
                     f"{_f(e.get('progress_mean'))} [{_f(plo)}, {_f(phi)}], wins {e.get('wins')}/{e.get('games')}, "
                     f"weights `{e.get('weights_hash')}` |")
    L += ["", "Training flags (the checkpoint's config; `*` = not recorded, the trainer predates the flag: its "
              "default):", "", "| group | flags |", "| --- | --- |"]
    for g in c["training_flags"]:
        cells = []
        for fl in g["flags"]:
            v = fl["value"]
            v = (("on" if v else "off") if isinstance(v, bool)
                 else (",".join(map(str, v)) or "none") if isinstance(v, (list, tuple))
                 else "none" if v is None else v)
            cells.append(f"{fl['name']} {v}{'' if fl['recorded'] else '*'}")
        L.append(f"| {g['group']} | {', '.join(cells)} |")

    for sec in card["seedsets"]:
        runs = sec["runs"]
        nn = [r for r in runs if r["kind"] in DETAIL_KINDS]
        L += ["", f"## {sec['id']}: A{sec['ascension']}, {sec['games']} games",
              "", f"Seed set `{sec['id']}` (`{sec['name']}`, role {sec.get('role') or '-'}, sha256 "
                  f"`{(sec['sha256'] or '?')[:16]}`, {'frozen' if sec['frozen'] else 'not a frozen set'}).", "",
              "| policy | progress mean [95% CI] | median | win rate [Wilson 95%] | floor mean / median / max | "
              "results | steps mean | replay |", "| --- | --- | --- | --- | --- | --- | --- | --- |"]
        for r in runs:
            h = r["headline"]
            lo, hi = h["win_rate_ci95"]
            plo, phi = h["progress_ci95"]
            res = ", ".join(f"{k} {v}" for k, v in h["results"].items() if v) or "-"
            L.append(f"| {r['label']} | {_f(h['progress_mean'])} [{_f(plo)}, {_f(phi)}] | "
                     f"{_f(h['progress_median'])} | {_pct(h['win_rate'])} [{_pct(lo)}, {_pct(hi)}] | "
                     f"{_f(h['floor_mean'], 1)} / {_f(h['floor_median'], 1)} / {_f(h['floor_max'])} | {res} | "
                     f"{_f(h['steps_mean'], 1)} | {h['replayable']}/{h['games']} |")
        notes = [f"{r['label']}: {n}" for r in runs for n in r["notes"]]
        for r in nn:
            lb = r.get("loop_breaker")
            if lb:
                notes.append(f"{r['label']}: loop breaker {lb.get('mode')} (k {lb.get('k')}): "
                             f"{lb.get('loop_breaks')} loop breaks in {lb.get('games_with_loop_breaks')} games, "
                             f"{lb.get('no_op_actions')} no-op actions, {lb.get('cycle_revisits')} cycle revisits")
            if r["kind"] == "lookahead" and r.get("search"):
                notes.append(f"{r['label']}: search {json.dumps(r['search'], sort_keys=True)}; information: "
                             f"{r.get('information') or '-'}; no loop breaker (outside the search: the plain argmax)")
        if notes:
            L += [""] + [f"- {x}" for x in notes]
        las = [r for r in runs if r["kind"] == "lookahead"]
        if las:
            L += ["", "### Lookahead: declared search, paired per seed", "",
                  "| row | paired against | Δ progress | t 95% CI | bootstrap 95% CI | better / worse / same | "
                  "searched decisions (share of live) | changed | ms / searched decision | rollout errors / desyncs |",
                  "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
            for r in las:
                p, st = r.get("paired") or {}, r.get("search_stats") or {}
                if p and "delta_mean" in p:
                    (t0, t1), (b0, b1) = p["t_ci95"], p["bootstrap_ci95"]
                    vs = p["vs"] + ("" if p["like_for_like"] else " (loop breaker on)")
                    pc = [vs, f"{p['delta_mean']:+.4f}", f"[{t0:+.4f}, {t1:+.4f}]", f"[{b0:+.4f}, {b1:+.4f}]",
                          f"{p['better']} / {p['worse']} / {p['same']}"]
                else:
                    pc = [p.get("vs") or "-", f"- ({p['error']})" if p.get("error") else "-", "-", "-", "-"]
                sc = ([f"{_n(st.get('searched'))} ({_pct(st.get('searched_share_of_live'), 0)})",
                       _pct(st.get("changed_rate")), _f(st.get("ms_per_searched_decision"), 1),
                       f"{_f(st.get('rollout_errors'))} / {_f(st.get('desyncs'))}"] if st else ["-"] * 4)
                L.append("| " + " | ".join([r["label"], *pc, *sc]) + " |")
            L += ["", "Δ = per-seed protocol progress of the row minus the row it is paired against "
                      "(`sts2train.lookahead.paired`: Student t interval on the per-seed differences, percentile "
                      "bootstrap of the mean, 10,000 resamples). Like for like is a `--greedy` run of the same weights "
                      "(loop breaker off, as the lookahead plays outside its search); paired against the card's NN "
                      "greedy row (loop breaker on), Δ mixes the search with the loop breaker. Counters from the run's "
                      "`stats.json`."]
        mrows = [r for r in runs if r["kind"] == "nn-mode"]
        if mrows:
            L += ["", "### Execution modes, paired per seed against the NN greedy row (loop breaker on)", "",
                  "| row | Δ progress | t 95% CI | bootstrap 95% CI | better / worse / same | stuck | games with a UI "
                  "cycle | UI cycles | loop breaks |", "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
            for r in [x for x in runs if x["kind"] == "nn"] + mrows:
                p, cy, lb = r.get("paired") or {}, r.get("cycles") or {}, r.get("loop_breaker") or {}
                if "delta_mean" in p:
                    (t0, t1), (b0, b1) = p["t_ci95"], p["bootstrap_ci95"]
                    pc = [f"{p['delta_mean']:+.4f}", f"[{t0:+.4f}, {t1:+.4f}]", f"[{b0:+.4f}, {b1:+.4f}]",
                          f"{p['better']} / {p['worse']} / {p['same']}"]
                else:
                    pc = ["(reference)" if r["kind"] == "nn" else f"- ({p.get('error')})", "-", "-", "-"]
                L.append("| " + " | ".join([r["label"], *pc, str(r["headline"]["results"].get("stuck", 0)),
                                            _f(cy.get("games_with_cycle")), _f(cy.get("cycles")),
                                            _f(lb.get("loop_breaks"))]) + " |")
            L += ["", "UI cycles: verified zero-progress RestSite / Shop -> DeckUpgrade / DeckRemove prompt -> cancel "
                      "loops back to the same observation (`sts2train.cycles`, counted, never masked). A sampled row "
                      "is one play per game (the play RNG from the sample seed and the game key)."]
        L += ["", "### Staged results (count / N [Wilson 95%])", "",
              "| stage | " + " | ".join(r["label"] for r in runs) + " |", "| --- |" + " --- |" * len(runs)]
        for key, label in STAGES:
            L.append(f"| {label} | " + " | ".join(_wilson_cell(r["staged"][key]) for r in runs) + " |")
        L += ["", "### Floor reached", "", "| act | floor | " + " | ".join(r["label"] for r in nn) + " |",
              "| --- | --- |" + " --- |" * len(nn)]
        hist = {}
        for j, r in enumerate(nn):
            for row in r["floor_histogram"]:
                hist.setdefault((row["act"], row["floor"]), [0] * len(nn))[j] = row["games"]
        top = max((v[0] for v in hist.values()), default=1) or 1
        for (act, floor), counts in sorted(hist.items()):
            cells = [f"{k} {'#' * max(1, round(30 * k / top))}" if j == 0 and k else str(k)
                     for j, k in enumerate(counts)]
            L.append(f"| {act} | {floor} | " + " | ".join(cells) + " |")
        L += ["", "### How the games ended", "", "| ending | " + " | ".join(r["label"] for r in nn) + " |",
              "| --- |" + " --- |" * len(nn)]
        for key, label in ENDINGS:
            if any(key in r["endings"] for r in nn):
                cells = [(f"{r['endings'][key]['games']} ({_pct(r['endings'][key]['share'], 0)})"
                          if key in r["endings"] else "0") for r in nn]
                L.append(f"| {label} | " + " | ".join(cells) + " |")
        L += [""] + [f"- {r['label']}: {_endings_detail(r['endings'])}" for r in nn]
        deck = [(r, r["at_death"]) for r in nn if r.get("at_death")]
        if deck:
            L += ["", "### Deck and relics at death (lost games)", "",
                  "| policy | deaths | mean deck size (games) | mean relics (games) |", "| --- | --- | --- | --- |"]
            for r, x in deck:
                L.append(f"| {r['label']} | {x['deaths']} | {_f(x['deck_mean'], 1)} ({x['deck_games']}) | "
                         f"{_f(x['relics_mean'], 1)} ({x['relic_games']}) |")
        L += ["", "### Win-probability head (calibration hint)", ""]
        wp = [(r, r["win_prob"]) for r in nn if r.get("win_prob")]
        if not wp:
            L.append("No readout: the checkpoint has no aux heads (`--aux-heads`), so the games record no win_prob.")
        else:
            L += ["| policy | games | mean win_prob, first decision | observed win rate | mean win_prob, last "
                  "decision: lost games (before death) | won games | other |",
                  "| --- | --- | --- | --- | --- | --- | --- |"]
            for r, x in wp:
                last = x["last"]

                def cell(g, last=last):
                    return f"{_f(last[g]['mean'])} ({last[g]['games']})" if g in last else "-"

                L.append(f"| {r['label']} | {x['games']} | {_f(x['first_mean'])} | {_pct(x['observed_win_rate'])} | "
                         f"{cell('loss')} | {cell('win')} | {cell('other')} |")
    L += ["", "## Definitions", "",
          "- Progress v0.2 = ((act - 1) + rooms cleared in the act / rooms in the act) / 3, 1.0 for a win; the 95% "
          "interval is mean ± 1.96 s/sqrt(n), clipped to [0, 1]. Win rate and every staged row carry a Wilson 95% "
          "score interval (0/20 has an upper bound of 16.1%, 0/100 of 3.7%).",
          "- Stages, from each game's final act and floor (k = rooms entered in the act, L = its rooms, the boss "
          "room is k = L): reached the Act-N boss = entered act N's boss room or got further; beat the Act-N boss = "
          "left act N, or ended `stuck` in its boss room (the floor step cap counts only non-combat decisions, "
          "which a boss room offers only after the fight is won; a `capped` game there does not count); reached "
          "Act N = final act >= N; cleared = won.",
          "- Endings: deaths by the encounter id of the last combat (`*_BOSS`, `*_ELITE`, `*_EVENT_ENCOUNTER`, "
          "else a normal fight); stuck = the floor step cap; capped = the step cap.",
          "- Floor reached: the final total floor (rooms entered) per act; `#` scales the first column.",
          "- Win-probability head: mean of `win_prob` at each game's first and last decision; a calibrated head "
          "starts near the observed win rate and ends near 0 in lost games. A hint, not a calibration test.", ""]
    return "\n".join(L)


# ------------------------------------------------------------------------------------------------ weights bundle


def bundle_name(out_file) -> str:
    """FILE.tar.gz -> FILE (the archive's top directory)."""
    n = Path(out_file).name
    for suffix in (".tar.gz", ".tgz"):
        if n.endswith(suffix):
            return n[: -len(suffix)]
    return Path(n).stem


def _jbytes(obj) -> bytes:
    return (json.dumps(obj, ensure_ascii=False, indent=1, default=str) + "\n").encode("utf-8")


CKPT_RECORD_KEYS = ("format", "weights_hash", "updates", "env_steps", "counters", "params", "encoder_version",
                    "saved_at", "arch", "heads", "config", "best_eval", "last_eval")


def readme_weights(card: dict, name: str, ck_file: str) -> str:
    """README-weights.md of a bundle: what it holds and the exact commands (from the repository root)."""
    c, lib = card["checkpoint"], card["library"]
    cfg = c.get("config") or {}
    chars = list(cfg.get("characters") or [])
    character = chars[0] if len(chars) == 1 else "SILENT"
    asc = cfg.get("ascension", 10)
    secs = card["seedsets"]
    ids = [s["id"] for s in secs]
    refs = [s.get("ref") or s["id"] for s in secs]
    first = refs[0] if refs else "silent-a10-v1"
    game = (secs[0].get("first_game") if secs else None) or f"{character}-<SEED>"
    ck = f'"$B/checkpoint/{ck_file}"'
    ev = f"tmp/eval/{name}-{_dirname(ids[0] if ids else first)}"
    la, gr = f"{ev}-la", f"{ev}-greedy-plain"
    sc = card.get("spec_compat") or {}
    compat = ""
    if sc.get("widened_enums"):
        compat = ("; the model's inputs are widened on that library (" + ", ".join(
            f"{k} {v['from']} -> {v['to']}" for k, v in sc["widened_enums"].items()) + ", the new rows zero)")
    elif sc:
        compat = "; the model's inputs are identical on that library"
    heads = ", ".join(c["heads"]) if c["heads"] else "none"
    L = [f"# {name}: {_who(c)} policy weights (sts2train)", "",
         f"Checkpoint `{ck_file}`: weights_hash `{c['weights_hash']}`, sha256 `{c['sha256']}`, update "
         f"{_n(c['update'])} ({_n(c['env_steps'])} env steps), {c['preset']} preset, {_n(c['params'])} parameters, "
         f"aux heads: {heads}. Trained on the simulator library encoder_version `{c['encoder_version']}`; packed "
         f"and evaluated on `{lib['version']}` (ABI {lib['abi_version']}){compat}. Simulator results (protocol "
         f"v{card['protocol_version']}, sim track), not real-engine results; the full card is `model-card.md`.", "",
         "| seed set | games | progress [95% CI] | win rate [Wilson 95%] | reached the Act-1 boss | reached Act 2 |",
         "| --- | --- | --- | --- | --- | --- |"]
    for s in secs:
        for r in s["runs"]:
            if r["kind"] != "nn":
                continue
            h = r["headline"]
            lo, hi = h["win_rate_ci95"]
            plo, phi = h["progress_ci95"]
            L.append(f"| {s['id']} | {h['games']} | {_f(h['progress_mean'])} [{_f(plo)}, {_f(phi)}] | "
                     f"{h['wins']}/{h['games']} [{_pct(lo)}, {_pct(hi)}] | {_wilson_cell(r['staged']['act1_boss'])} | "
                     f"{_wilson_cell(r['staged']['act2'])} |")
    L += ["", "## Files", "", "| path | content |", "| --- | --- |",
          f"| `checkpoint/{ck_file}` | the sts2train checkpoint as trained (weights, spec, config; with optimizer "
          f"and RNG state when resumable: `sts2train train --resume`) |",
          "| `checkpoint/spec.json` | the observation spec the model was built for (`features.ModelSpec`) |",
          "| `checkpoint/config.json` | the checkpoint's record: training config, arch, heads, counters, training-time "
          "evals |",
          "| `model-card.md`, `model-card.json` | `sts2train report-card`: identity, staged protocol results with "
          "Wilson intervals, baselines |",
          "| `library.json` | the simulator library it was packed against: encoder_version, ABI version, spec "
          "compatibility |",
          "| `manifest.json`, `SHA256SUMS` | size and sha256 of every file |", "",
          "## Verify", "", "```bash", f"tar xzf {name}.tar.gz && B=$PWD/{name}",
          '(cd "$B" && shasum -a 256 -c SHA256SUMS)', "```", "",
          "## Set up", "",
          "From the hack-sts2 repository root (every command below runs there, with `B` = the unpacked bundle):", "",
          "```bash", "dotnet build sim/Sts2Sim.sln -c Release      # the simulator library (Sts2Sim.Vec.dll)",
          "uv sync --project sim/tools/train", "```", "",
          "A library whose encoder_version differs from the one above runs the checkpoint only when the model's "
          "inputs are identical or widened (`features.check_spec_compat`); otherwise the commands exit 2 before "
          "any game.", "",
          "## Evaluate (protocol v0.2: greedy policy with the loop breaker)", "", "```bash",
          f"uv run --project sim/tools/train sts2train eval --seedset {first} --checkpoint {ck} \\",
          f"    --out {ev} --threads 4", "```", "",
          "Writes `report.md` / `report.json` and `games/<CHARACTER>-<SEED>.summary.json` (actions, result, act, "
          "floor, death); exits 1 when the replay audit fails. `--loop-breaker off` is the plain argmax. Seed sets: "
          + ", ".join(f"`{i}`" for i in ids) + ".", "",
          "## Re-make the model card", "", "```bash",
          f"uv run --project sim/tools/train sts2train report-card --checkpoint {ck} \\",
          f"    --out tmp/card/{name} --seedsets {','.join(refs)} --threads 4", "```", "",
          "Reuses the evaluations already in `--out` and the cached baselines (`tmp/eval/baselines`); "
          "`--lookahead-dir DIR ...` adds `python -m sts2train.lookahead eval` runs (next section).", "",
          "## Lookahead (a declared search row)", "", "```bash",
          f"uv run --project sim/tools/train python -m sts2train.lookahead eval --seedset {first} --checkpoint {ck} \\",
          f"    --out {la} --threads 4            # K 8, R 1, turn horizon, value+hp leaf",
          f"uv run --project sim/tools/train python -m sts2train.lookahead eval --seedset {first} --checkpoint {ck} \\",
          f"    --out {gr} --threads 4 --greedy   # its paired baseline: loop breaker off",
          f"uv run --project sim/tools/train sts2train report-card --checkpoint {ck} \\",
          f"    --out tmp/card/{name} --seedsets {','.join(refs)} --threads 4 --lookahead-dir {la} {gr}", "```", "",
          "The lookahead searches combat decisions on determinized copies of the game (the player's view; draw order "
          "and RNG resampled, never the true hidden state), declared in the run's `config.json` (`search`, "
          "`information`). The card shows it as a \"lookahead, determinized\" row, paired per seed against the "
          "`--greedy` run, which plays as the lookahead does outside its search (the plain argmax, no loop breaker). "
          "Guide: `docs/lookahead-2026-11-04.md`.", "",
          "## Probe", "", "```bash",
          f"uv run --project sim/tools/train sts2train probe --checkpoint {ck} \\",
          f"    --eval-dir {ev} --game {game} --top 3 --out {ev}/probe-{game}.jsonl", "```", "",
          "Replays that game from the evaluation's recorded actions and prints, per decision, the action taken, the "
          "model's top candidates with probabilities and the value head (and win_prob / combat_hp_pred with aux "
          "heads); `--out` keeps them as JSON lines. Without `--game` every game of the run is probed.", "",
          "## Real game (live twin runner)", "",
          f"`{LIVE_TWIN}` (guide `scripts/live/README-nn-twin.md`) plays the real game through a simulator twin: "
          "the network reads the twin's observation, the "
          "chosen candidate is mapped onto the game's actions through STS2_MCP. STS2_MCP is installed only while "
          "such a session runs (repository rule):", "", "```bash",
          "scripts/mcp_mod.sh on       # install STS2_MCP for this session",
          f"uv run --project sim/tools/train python {LIVE_TWIN} --checkpoint {ck} \\",
          f"    --character {character} --ascension {asc} --out output/nn-twin/{name}",
          "scripts/mcp_mod.sh off      # always, when the session ends", "```", "",
          "Start a run with that character and ascension in the game first (`--seed SEED` if the runner cannot "
          "read it from the game). `--dry-run-server URL --seed SEED` plays the simulator's play server instead "
          "of the game (no STS2_MCP).", ""]
    return "\n".join(L)


def _tar_add(tar: tarfile.TarFile, arcname: str, src) -> None:
    ti = tarfile.TarInfo(arcname)
    ti.mtime, ti.mode, ti.uid, ti.gid, ti.uname, ti.gname = int(time.time()), 0o644, 0, 0, "", ""
    if isinstance(src, Path):
        ti.size = src.stat().st_size
        with open(src, "rb") as f:
            tar.addfile(ti, f)
    else:
        ti.size = len(src)
        tar.addfile(ti, io.BytesIO(src))


def pack(checkpoint, out_file, *, card_dir=None, log: Callable[[str], None] = print, **card_kw) -> dict:
    """Write the weights bundle `out_file` (tar.gz, top directory ``bundle_name(out_file)``) and check it; -> where,
    size, sha256, file count, the card directory and the card. `card_dir` (default ``<FILE>.card`` next to the
    archive) is reused when its model-card.json is this checkpoint's (sha256), else ``report_card(..., **card_kw)``
    makes it there."""
    from sts2vec.evaluate import repo_sha

    ck_path, out_file = Path(checkpoint), Path(out_file)
    name = bundle_name(out_file)
    out_file.parent.mkdir(parents=True, exist_ok=True)
    sha = file_sha256(ck_path)
    card_dir = Path(card_dir) if card_dir else out_file.parent / f"{name}.card"
    card = _read_json(card_dir / "model-card.json")
    if card is None or (card.get("checkpoint") or {}).get("sha256") != sha or not (card_dir / "model-card.md").exists():
        log(f"model card: making it in {card_dir}")
        card = report_card(ck_path, card_dir, log=log, **card_kw)
    else:
        log(f"model card: reusing {card_dir} (the checkpoint's sha256 matches)")
    ck = torch.load(ck_path, map_location="cpu", weights_only=False)
    c = card["checkpoint"]
    library = {**card["library"], "checkpoint_encoder_version": c["encoder_version"],
               "spec_compat": card.get("spec_compat"), "card_generated_at": card.get("generated_at")}
    files: dict[str, bytes | Path] = {
        f"checkpoint/{ck_path.name}": ck_path,
        "checkpoint/spec.json": _jbytes(ck["spec"]),
        "checkpoint/config.json": _jbytes({k: ck[k] for k in CKPT_RECORD_KEYS if k in ck}),
        "model-card.md": (card_dir / "model-card.md").read_bytes(),
        "model-card.json": (card_dir / "model-card.json").read_bytes(),
        "library.json": _jbytes(library),
        "README-weights.md": readme_weights(card, name, ck_path.name).encode("utf-8"),
    }
    entries = []
    for rel, src in sorted(files.items()):
        if isinstance(src, Path):
            entries.append({"path": rel, "bytes": src.stat().st_size, "sha256": sha if src == ck_path
                            else file_sha256(src)})
        else:
            entries.append({"path": rel, "bytes": len(src), "sha256": hashlib.sha256(src).hexdigest()})
    files["manifest.json"] = _jbytes({
        "bundle": name, "created_at": _now(), "generator": "sts2train pack", "repo_sha": repo_sha(),
        "checkpoint": {"path": f"checkpoint/{ck_path.name}", "sha256": sha, "weights_hash": c["weights_hash"],
                       "update": c["update"], "env_steps": c["env_steps"]},
        "library": {k: library.get(k) for k in ("version", "encoder_version", "abi_version", "python_abi_version",
                                               "checkpoint_encoder_version")},
        "files": entries})
    sums = {e["path"]: e["sha256"] for e in entries}
    sums["manifest.json"] = hashlib.sha256(files["manifest.json"]).hexdigest()
    files["SHA256SUMS"] = "".join(f"{d}  {p}\n" for p, d in sorted(sums.items())).encode("utf-8")
    tmp = out_file.with_name(out_file.name + ".tmp")
    with tarfile.open(tmp, "w:gz", compresslevel=6) as tar:
        for rel in sorted(files):
            _tar_add(tar, f"{name}/{rel}", files[rel])
    tmp.replace(out_file)
    check = verify_bundle(out_file)
    log(f"wrote {out_file} ({out_file.stat().st_size / 2**20:.1f} MiB, {check['files']} files, sha256 verified)")
    return {"out": str(out_file), "bytes": out_file.stat().st_size, "sha256": file_sha256(out_file), "bundle": name,
            "files": check["files"], "card_dir": str(card_dir), "card": card}


def verify_bundle(path) -> dict:
    """Read a bundle once (streaming) and check every file against ``SHA256SUMS`` and ``manifest.json`` (sizes and
    hashes, nothing missing, nothing extra); -> {root, files, manifest}. Raises `BundleError`."""
    digests, sizes, keep = {}, {}, {}
    roots = set()
    with tarfile.open(path, "r|gz") as tar:
        for m in tar:
            if not m.isfile():
                continue
            root, _, rel = m.name.partition("/")
            roots.add(root)
            f = tar.extractfile(m)
            h = hashlib.sha256()
            buf = io.BytesIO() if rel in ("SHA256SUMS", "manifest.json") else None
            for b in iter(lambda: f.read(1 << 20), b""):
                h.update(b)
                if buf is not None:
                    buf.write(b)
            digests[rel], sizes[rel] = h.hexdigest(), m.size
            if buf is not None:
                keep[rel] = buf.getvalue()
    if len(roots) != 1:
        raise BundleError(f"{path}: expected one top directory, found {sorted(roots)}")
    if "SHA256SUMS" not in keep or "manifest.json" not in keep:
        raise BundleError(f"{path}: no SHA256SUMS / manifest.json")
    sums = {}
    for line in keep["SHA256SUMS"].decode("utf-8").splitlines():
        if line.strip():
            d, _, rel = line.partition("  ")
            sums[rel] = d
    listed = set(sums) | {"SHA256SUMS"}
    if set(digests) != listed:
        raise BundleError(f"{path}: files not in SHA256SUMS {sorted(set(digests) - listed)}, missing "
                          f"{sorted(listed - set(digests))}")
    bad = sorted(rel for rel, d in sums.items() if digests[rel] != d)
    if bad:
        raise BundleError(f"{path}: sha256 mismatch: {bad}")
    manifest = json.loads(keep["manifest.json"])
    for e in manifest.get("files") or []:
        if sums.get(e["path"]) != e["sha256"] or sizes.get(e["path"]) != e["bytes"]:
            raise BundleError(f"{path}: {e['path']} does not match manifest.json")
    return {"root": roots.pop(), "files": len(sums), "manifest": manifest}


# ------------------------------------------------------------------------------------------------ probe


def probe(checkpoint, run_dir, *, games=None, top: int = 3, out=None, threads: int = 4, device: str = "cpu",
          log: Callable[[str], None] = print) -> list[dict]:
    """Replay games of the protocol run directory `run_dir` (``history_actions`` of their summaries; `games` = keys
    or seeds, default all) on a fresh env with the run's limits and read `checkpoint` at every decision; -> one dict
    per decision (key, n, act, floor, phase, hp, cand_n, value, taken / greedy / top-`top` candidates with
    probabilities, win_prob / combat_hp_pred with aux heads), also written as JSON lines to `out`. Each game's state
    hash after its last recorded action is checked against the summary's ``final_state_hash``."""
    from sts2vec import VecEnv, load, schema_of

    from .features import ModelSpec, bind_spec, obs_to_torch
    from .ppo import load_checkpoint

    d = Path(run_dir)
    cfg = _read_json(d / "config.json")
    if not cfg or not cfg.get("seedset"):
        raise ValueError(f"{d}: not a protocol run directory (no config.json with a seed set)")
    ss = cfg["seedset"]
    want = set(games or ())
    sel = [g for g in ss["games"] if not want or PG.game_key(g) in want or g["seed"] in want]
    if not sel:
        raise ValueError(f"{d}: no game {sorted(want)} (games: {', '.join(PG.game_key(g) for g in ss['games'])})")
    hist, end_hash = [], []
    for g in sel:
        s = _read_json(d / "games" / f"{PG.game_key(g)}.summary.json")
        if s is None:
            raise ValueError(f"{d}: no summary for {PG.game_key(g)}")
        hist.append([int(x) for x in s.get("history_actions") or []])
        end_hash.append(s.get("final_state_hash"))
    schema = schema_of(load())
    model, spec, ck = load_checkpoint(checkpoint, device, library=ModelSpec.from_schema(schema))
    bind_spec(spec)
    if cfg.get("weights_hash") and cfg["weights_hash"] != ck.get("weights_hash"):
        log(f"note: {d} was played by weights {cfg['weights_hash']}; probing {ck.get('weights_hash')} on its states")
    aux = bool(getattr(model, "aux_heads", False))
    G, AX, CC = schema.cols("global_i"), schema.cols("aux"), schema.cols("cand")
    kinds, phases, cards = schema.vocab.enums["RunActionKind"], schema.vocab.enums["RunPhase"], schema.vocab.cards
    lim = cfg.get("limits") or LIMITS
    n = len(sel)
    rows: list[dict] = []
    mismatched = []
    env = VecEnv(n, threads=threads, step_cap=lim["step_cap"], floor_step_cap=lim["floor_step_cap"],
                 auto_reset=False, seed_text="EVAL", content=ss["content"], pool=None)
    try:
        for g in sel:
            env.pool.add_fresh(g["character"], seed=g["seed"], ascension=ss["ascension"], content=ss["content"])
        obs = env.reset(start_ids=np.arange(n, dtype=np.int32))
        ended = [False] * n
        for t in range(max(len(h) for h in hist)):
            live = [i for i in range(n) if not ended[i] and t < len(hist[i])]
            if not live:
                break
            with torch.no_grad():
                x = obs_to_torch(obs, device)
                logits, value, h = model(x, aux=True) if aux else (*model(x), None)
            probs = torch.softmax(logits.double(), -1).cpu().numpy()
            value = value.float().cpu().numpy()
            wp = torch.sigmoid(h["win_logit"]).cpu().numpy() if h is not None else None
            cp = torch.sigmoid(h["combat_logit"]).cpu().numpy() if h is not None else None
            gi, cand = obs["global_i"], obs["cand"]

            def label(i: int, j: int) -> str:
                c = cand[i, j]
                parts = [kinds.name(int(c[CC["kind"]]))]
                if int(c[CC["card_id"]]) > 0:
                    parts.append(cards.name(int(c[CC["card_id"]])))
                parts += [f"{f.split('_')[0]}{int(c[CC[f]])}" for f in ("hand_slot", "target_slot", "potion_slot",
                                                                        "option_index") if int(c[CC[f]]) >= 0]
                return " ".join(parts)

            a = np.full(n, -1, dtype=np.int32)
            for i in live:
                a[i] = act = hist[i][t]
                cn = int(obs["cand_n"][i])
                p = probs[i, :cn]
                order = np.argsort(-p, kind="stable")[:top]

                def cell(j: int) -> dict:
                    return {"index": int(j), "label": label(i, int(j)), "prob": round(float(p[j]), 4)}

                row = {"key": PG.game_key(sel[i]), "n": t, "act": int(obs["aux"][i, AX["act_index"]]) + 1,
                       "floor": int(gi[i, G["total_floor"]]), "phase": phases.name(int(gi[i, G["decision_kind"]])),
                       "hp": int(gi[i, G["hp"]]), "max_hp": int(gi[i, G["max_hp"]]), "cand_n": cn,
                       "value": round(float(value[i]), 4),
                       "taken": cell(act) if 0 <= act < cn else {"index": act, "label": "(illegal)", "prob": None},
                       "greedy": cell(int(order[0])), "top": [cell(int(j)) for j in order]}
                if wp is not None:
                    row["win_prob"] = round(float(wp[i]), 4)
                    row["combat_hp_pred"] = round(float(cp[i]), 4) if int(gi[i, G["is_combat"]]) else None
                rows.append(row)
            obs, _, term, trunc, _, _ = env.step(a)
            for i in live:
                if term[i] or trunc[i]:
                    ended[i] = True
                if ended[i] or t == len(hist[i]) - 1:
                    ended[i] = True
                    if end_hash[i] is not None and f"{int(env.state_hash([i])[0]):016x}" != end_hash[i]:
                        mismatched.append(PG.game_key(sel[i]))
    finally:
        env.close()
    if mismatched:
        log(f"WARNING: the replay did not reproduce the recorded end state of {mismatched} (another library or "
            "a non-deterministic run): the rows after the divergence are not that run's states")
    if out:
        Path(out).parent.mkdir(parents=True, exist_ok=True)
        with open(out, "w", encoding="utf-8") as f:
            for r in rows:
                f.write(json.dumps(r, ensure_ascii=False) + "\n")
    for r in rows:
        tops = ", ".join(f"{c['label']} {c['prob']:.2f}" for c in r["top"])
        extra = f" win_prob {r['win_prob']:.3f}" if "win_prob" in r else ""
        log(f"{r['key']} #{r['n']:<4} A{r['act']} F{r['floor']:<2} {r['phase']:<12} hp {r['hp']}/{r['max_hp']} "
            f"V {r['value']:+.3f}{extra} | took {r['taken']['label']} ({_f(r['taken']['prob'], 2)}) | top {tops}")
    log(f"probe: {len(rows)} decisions in {n} games" + (f" -> {out}" if out else ""))
    return rows
