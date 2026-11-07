#!/usr/bin/env python3
"""evalstats.py: windowed, per-seed paired readouts of protocol evals: window | pair | slope | index.

    python3 -I scripts/analysis/evalstats.py window SRC --seedset dev128 --at U [--k 5] [--mode greedy-off] [--after F]
    python3 -I scripts/analysis/evalstats.py pair A_SRC B_SRC --seedset dev128 --at U [--at-b U2] [--k 5] [--after F]
    python3 -I scripts/analysis/evalstats.py slope SRC [SRC ...] --seedset dev128 [--from U0] [--to U1] [--last-n N]
    python3 -I scripts/analysis/evalstats.py index SRC [--seedset dev128]
    (every subcommand: --json OUT.json, or --json - to print the JSON after the text line)

SRC = [RUN=]PATH[,PATH...]. A PATH is an eval directory (config.json + games/*.summary.json, or the flat
summary.json of eval_summary.py), a directory tree holding such directories (runs/<run>/evals/u<N>/<mode>/,
evals/endpoint/u<N>-<set>-<mode>/, a pulled copy, ...), a run mirror holding evals.jsonl (per-checkpoint means only),
or an evals.jsonl / summary.json file. With RUN=, eval directories whose checkpoint path names another run are
ignored (directories with no run in their path are kept); evals.jsonl rows are attributed to RUN (default: the name of
the directory holding the file). Several sources of one checkpoint merge: per-seed data wins, evals.jsonl means are
kept as a cross-check (``mean_mismatch`` warnings). Repeats (eval_mode.repeat) are plays of one checkpoint; a seed
enters with the mean of its plays.

--seedset: an id or name (silent-a10-dev-v1, silent-a10-ab-s512, ...) or an alias <role><n> (dev128, dev-128, ab512,
dev512): a set whose id or name contains -<role>- and that has n games. Blind sets are refused without --allow-blind.

window (EV1): the K latest checkpoints of the mode with after < update <= at; per seed the mean over them (per-seed
method) or, when a checkpoint has means only, the mean of the K means (means method). Leading indicators per seed:
act3 (reached Act 3 / won), act2_boss (reached the Act-2 boss), act2_boss_beaten.

pair (B - A): per-seed difference of the two windows: mean, sd, 95% t interval and 95% percentile bootstrap (10,000
resamples of the seeds, random.Random(0)) as sts2train/compare.py ``paired`` computes them (the bootstrap draws differ
from numpy's, so its bounds differ in the 3rd-4th decimal), better / worse / same, corr of the arms' per-seed means;
leading indicators as paired differences (act3 share: mean [t] [bootstrap] and the discordant counts b / c of a
McNemar table when both windows are single checkpoints; act2_boss_pass = beaten / reached per arm, its difference with
a joint seed bootstrap). With means only the interval is a variance model: half-width 1.96 * sqrt((J/K_a + J/K_b) / n),
J = per-game checkpoint-jitter variance (default 0.0156, EV1: adjacent checkpoints are as different as different lines,
so the seed component cancels); ``ci_method`` says which one was used.

slope: OLS of progress on update over the checkpoints in range (several SRCs concatenate a lineage; a later SRC wins an
update both hold), reported per 500 updates; per-seed method: the mean of per-seed slopes (equal to the slope of the
means) with a 10k seed bootstrap and a t interval; always also the slope of the means with its classical OLS
interval (t, n_points - 2 df). ``plateau`` = the interval's upper bound < +0.005 / 500 updates.

Standard library only; run as ``python3 -I`` on downloaded data.
"""

from __future__ import annotations

import argparse
import glob
import json
import math
import os
import random
import re
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python3 -I does not add the script's directory

import eval_summary as ES  # noqa: E402

BOOT = 10_000
DEFAULT_JITTER_VAR = 0.0156
PLATEAU_PER500 = 0.005
_ALIAS = re.compile(r"^(dev|ab|test|blind|train)-?(\d+)$")

# ------------------------------------------------------------------------------------------------ statistics


def _betacf(a, b, x):
    tiny, qab, qap, qam = 1e-300, a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    d = 1.0 / (d if abs(d) > tiny else tiny)
    h = d
    for m in range(1, 300):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < 1e-14:
            break
    return h


def _betainc(a, b, x):
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    lb = math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log1p(-x)
    if x < (a + 1.0) / (a + b + 2.0):
        return math.exp(lb) * _betacf(a, b, x) / a
    return 1.0 - math.exp(lb) * _betacf(b, a, 1.0 - x) / b


def t_cdf(t, df):
    tail = 0.5 * _betainc(df / 2.0, 0.5, df / (df + t * t))
    return 1.0 - tail if t >= 0 else tail


def t_ppf(p, df):
    """Student t quantile (bisection on the cdf; sts2train.lookahead.t_ppf)."""
    lo, hi = -1e3, 1e3
    for _ in range(200):
        mid = 0.5 * (lo + hi)
        if t_cdf(mid, df) < p:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def t_interval(xs):
    n = len(xs)
    if n == 0:
        return [None, None]
    m = statistics.fmean(xs)
    if n < 2:
        return [m, m]
    half = t_ppf(0.975, n - 1) * statistics.stdev(xs) / math.sqrt(n)
    return [m - half, m + half]


def percentile(sorted_xs, q):
    """numpy.percentile's default (linear) interpolation."""
    n = len(sorted_xs)
    if n == 1:
        return sorted_xs[0]
    pos = (n - 1) * q / 100.0
    lo = int(math.floor(pos))
    hi = min(lo + 1, n - 1)
    return sorted_xs[lo] + (sorted_xs[hi] - sorted_xs[lo]) * (pos - lo)


def bootstrap_mean_ci(xs, boot=BOOT, seed=0):
    n = len(xs)
    if n == 0:
        return [None, None]
    rng = random.Random(seed)
    means = sorted(math.fsum(rng.choices(xs, k=n)) / n for _ in range(boot))
    return [percentile(means, 2.5), percentile(means, 97.5)]


def pearson(xs, ys):
    if len(xs) < 2:
        return None
    mx, my = statistics.fmean(xs), statistics.fmean(ys)
    sxy = math.fsum((x - mx) * (y - my) for x, y in zip(xs, ys))
    sxx = math.fsum((x - mx) ** 2 for x in xs)
    syy = math.fsum((y - my) ** 2 for y in ys)
    return sxy / math.sqrt(sxx * syy) if sxx > 0 and syy > 0 else None


def diff_stats(d, boot=BOOT, seed=0) -> dict:
    """compare.py ``paired`` numbers of a per-seed difference vector."""
    n = len(d)
    return {"n": n, "delta_mean": statistics.fmean(d) if n else None,
            "delta_sd": statistics.stdev(d) if n > 1 else 0.0, "t_ci95": t_interval(d),
            "bootstrap_ci95": bootstrap_mean_ci(d, boot, seed), "bootstrap_resamples": boot,
            "better": sum(1 for x in d if x > 1e-9), "worse": sum(1 for x in d if x < -1e-9),
            "same": sum(1 for x in d if abs(x) <= 1e-9), "ci_method": "per-seed"}


def model_ci(delta, n, ka, kb, jitter_var=DEFAULT_JITTER_VAR):
    se = math.sqrt((jitter_var / ka + jitter_var / kb) / n)
    return {"delta_mean": delta, "se": se, "ci95": [delta - 1.96 * se, delta + 1.96 * se], "ci_method": "variance-model",
            "jitter_var": jitter_var, "n": n}


# ------------------------------------------------------------------------------------------------ indexing


def seedset_matches(spec: str, ss: dict) -> bool:
    spec = str(spec).strip()
    ident = {str(ss.get("id") or ""), str(ss.get("name") or "")}
    if spec in ident:
        return True
    m = _ALIAS.match(spec.lower())
    if not m:
        return False
    role, n = m.group(1), int(m.group(2))
    return ss.get("n") == n and any(f"-{role}-" in x for x in ident if x)


def is_blind(ss: dict) -> bool:
    return ss.get("role") == "blind" or any("-blind-" in str(ss.get(k) or "") for k in ("id", "name"))


def _entry_from_summary(s: dict, path: str) -> dict:
    return {"run": s.get("run"), "update": s.get("update"), "seedset": dict(s.get("seedset") or {}),
            "mode": (s.get("mode") or {}).get("name"), "repeat": (s.get("mode") or {}).get("repeat"),
            "weights_hash": s.get("weights_hash"), "source": "summary.json", "paths": [path],
            "plays": {k: [v] for k, v in (s.get("per_seed") or {}).items()},
            "stages": {k: [int(v)] for k, v in (s.get("per_seed_stages") or {}).items()},
            "mean": s.get("progress_mean"), "games": s.get("games"),
            "act3": (s.get("funnel") or {}).get("act3")}


def _entry_from_dir(d: str) -> dict:
    sj = ES.read_json(os.path.join(d, "summary.json"))
    if isinstance(sj, dict) and sj.get("schema") == ES.SCHEMA:
        return _entry_from_summary(sj, os.path.join(d, "summary.json"))
    return _entry_from_summary(ES.build_summary(d), d)


def _entries_from_jsonl(path: str, run: str | None) -> list[dict]:
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except ValueError:
                continue
            if r.get("update") is None or r.get("progress_mean") is None:
                continue
            mode = r.get("mode") or f"greedy-{r.get('loop_breaker') or 'on'}"
            ar = r.get("act_reached") or {}
            act3 = int(ar.get("3") or ar.get(3) or 0)            # final act 3 (a won game ends in act 3)
            out.append({"run": run, "update": int(r["update"]),
                        "seedset": {"id": r.get("seedset"), "name": None, "n": r.get("games")},
                        "mode": mode, "repeat": None, "weights_hash": r.get("weights_hash"), "source": "evals.jsonl",
                        "paths": [path], "plays": None, "stages": None, "mean": float(r["progress_mean"]),
                        "games": r.get("games"), "act3": act3})
    return out


def scan_path(path: str, run: str | None) -> list[dict]:
    """Raw entries (one per eval directory / evals.jsonl row) of a path."""
    if os.path.isfile(path):
        if path.endswith(".jsonl"):
            return _entries_from_jsonl(path, run or os.path.basename(os.path.dirname(os.path.abspath(path))))
        s = ES.read_json(path)
        if isinstance(s, dict) and s.get("schema") == ES.SCHEMA:
            return [_entry_from_summary(s, path)]
        raise ValueError(f"{path}: neither evals.jsonl nor a {ES.SCHEMA} summary")
    if not os.path.isdir(path):
        raise FileNotFoundError(path)
    out = []
    for d, dirs, files in os.walk(path):
        if ES.is_eval_dir(d) or (ES.read_json(os.path.join(d, "summary.json")) or {}).get("schema") == ES.SCHEMA:
            out.append(_entry_from_dir(d))
            dirs[:] = []
            continue
        if "evals.jsonl" in files:
            out += _entries_from_jsonl(os.path.join(d, "evals.jsonl"), run or os.path.basename(os.path.abspath(d)))
        dirs[:] = sorted(x for x in dirs if x not in ("games", "checkpoints", "__pycache__") and not x.startswith("."))
    return out


def parse_src(src: str) -> tuple[str | None, list[str]]:
    run, _, rest = str(src).partition("=")
    if not rest or "/" in run or os.path.exists(src):
        return None, [p for p in str(src).split(",") if p]
    return run, [p for p in rest.split(",") if p]


class Index:
    """The checkpoints of one run (or lineage) per (seedset, mode, update)."""

    def __init__(self, src: str | list[str], warnings: list | None = None):
        self.label, paths = (None, src) if isinstance(src, list) else parse_src(src)
        self.paths = paths
        self.warnings = warnings if warnings is not None else []
        self.entries: dict[tuple, dict] = {}
        raw = []
        for p in paths:
            raw += scan_path(p, self.label)
        runs = {e["run"] for e in raw if e["run"]}
        if self.label is None and len(runs) > 1:
            raise ValueError(f"{','.join(paths)}: eval dirs of several runs ({', '.join(sorted(runs))}); use RUN=PATH")
        for e in raw:
            if self.label and e["run"] and e["run"] != self.label:
                continue
            self._add(e)
        if self.label is None:
            self.label = next(iter(runs)) if len(runs) == 1 else ",".join(paths)

    def _add(self, e: dict):
        if e["update"] is None:
            self.warnings.append(f"{e['paths'][0]}: no update (skipped)")
            return
        sk = e["seedset"].get("id") or e["seedset"].get("name")
        key = (sk, e["mode"], int(e["update"]))
        cur = self.entries.get(key)
        if cur is None:
            self.entries[key] = dict(e)
            return
        if e["plays"] is None:                                  # a means row for a known checkpoint: cross-check
            cur.setdefault("jsonl_mean", e["mean"])
            return
        if cur["plays"] is None:                                # per-seed data replaces a means row
            e = dict(e)
            e["jsonl_mean"] = cur["mean"]
            self.entries[key] = e
            return
        if e.get("repeat") is not None and cur.get("repeat") is not None and e["repeat"] != cur["repeat"]:
            for k, v in e["plays"].items():                     # repeats: more plays of the same checkpoint
                cur["plays"].setdefault(k, []).extend(v)
            for k, v in (e["stages"] or {}).items():
                cur["stages"].setdefault(k, []).extend(v)
            cur["paths"] = cur["paths"] + e["paths"]
            cur["mean"] = statistics.fmean(x for v in cur["plays"].values() for x in v)
            cur["repeat"] = f"{cur['repeat']}+{e['repeat']}"
            return
        if cur["plays"] != e["plays"]:
            self.warnings.append(f"duplicate checkpoint {key}: {cur['paths'][0]} and {e['paths'][0]} differ; "
                                 f"kept the first")

    def points(self, seedset: str, mode: str, after=None, at=None, allow_blind=False) -> list[dict]:
        out = []
        for (sk, m, u), e in self.entries.items():
            if m != mode or not seedset_matches(seedset, e["seedset"]):
                continue
            if is_blind(e["seedset"]) and not allow_blind:
                raise PermissionError(f"{sk}: blind seed set (pass --allow-blind deliberately)")
            if (after is not None and u <= after) or (at is not None and u > at):
                continue
            out.append(e)
        out.sort(key=lambda e: e["update"])
        for e in out:
            jm = e.get("jsonl_mean")
            if jm is not None and e["plays"] is not None and abs(jm - e["mean"]) > 1e-6:
                msg = f"mean_mismatch {self.label} u{e['update']}: evals.jsonl {jm:.6f} vs per-seed {e['mean']:.6f}"
                if msg not in self.warnings:
                    self.warnings.append(msg)
        return out


def merge_lineage(indexes: list[Index]) -> Index:
    """One index over several (a later index wins an update both hold)."""
    m = Index.__new__(Index)
    m.label = "+".join(ix.label for ix in indexes)
    m.paths = [p for ix in indexes for p in ix.paths]
    m.warnings = indexes[0].warnings if indexes else []
    m.entries = {}
    for ix in indexes:
        m.entries.update(ix.entries)
    return m


# ------------------------------------------------------------------------------------------------ readouts


def _seed_mean(e, k):
    v = e["plays"][k]
    return statistics.fmean(v)


def _seed_flag(e, k, stage):
    bit = ES.STAGE_KEYS.index(stage)
    v = e["stages"][k]
    return statistics.fmean((int(x) >> bit) & 1 for x in v)


def window(ix: Index, seedset: str, at: int, k: int = 5, mode: str = "greedy-off", after=None,
           allow_blind=False) -> dict:
    pts = ix.points(seedset, mode, after=after, at=at, allow_blind=allow_blind)[-k:] if k > 0 else []
    out = {"run": ix.label, "seedset": seedset, "mode": mode, "at": at, "k": k, "after": after,
           "updates": [e["update"] for e in pts], "n_points": len(pts),
           "point_means": [e["mean"] for e in pts], "sources": [e["paths"][0] for e in pts]}
    if not pts:
        out.update(method=None, mean=None, per_seed=None, act3=None)
        return out
    ss = pts[-1]["seedset"]
    out["seedset_identity"] = {x: ss.get(x) for x in ("id", "name", "n", "sha256")}
    shas = {e["seedset"].get("sha256") for e in pts if e["seedset"].get("sha256")}
    if len(shas) > 1:
        raise ValueError(f"{ix.label}: window mixes seed sets {sorted(shas)}")
    keysets = {tuple(sorted(e["plays"])) for e in pts if e["plays"] is not None}
    if all(e["plays"] is not None for e in pts) and len(keysets) == 1:
        keys = list(next(iter(keysets)))
        per = {kk: statistics.fmean(_seed_mean(e, kk) for e in pts) for kk in keys}
        lead = {st: {kk: statistics.fmean(_seed_flag(e, kk, st) for e in pts) for kk in keys}
                for st in ("act3", "act2_boss", "act2_boss_beaten")} if all(e["stages"] for e in pts) else None
        out.update(method="per-seed", per_seed=per, lead_per_seed=lead, mean=statistics.fmean(per.values()),
                   games=len(keys), act3=statistics.fmean(lead["act3"].values()) if lead else None,
                   single_masks={kk: pts[0]["stages"][kk][0] for kk in keys}
                   if len(pts) == 1 and lead and all(len(pts[0]["stages"][kk]) == 1 for kk in keys) else None)
    else:
        if len(keysets) > 1:
            ix.warnings.append(f"{ix.label}: checkpoints of the window hold different games; means method")
        games = [e["games"] for e in pts if e.get("games")]
        a3 = [e["act3"] / e["games"] for e in pts if e.get("act3") is not None and e.get("games")]
        out.update(method="means", per_seed=None, lead_per_seed=None, mean=statistics.fmean(e["mean"] for e in pts),
                   games=min(games) if games else None, act3=statistics.fmean(a3) if len(a3) == len(pts) else None,
                   single_masks=None)
    return out


def _pass_rate(lead, keys):
    reach = math.fsum(lead["act2_boss"][k] for k in keys)
    beat = math.fsum(lead["act2_boss_beaten"][k] for k in keys)
    return beat / reach if reach > 0 else None


def leading_pair(wa: dict, wb: dict, boot=BOOT, seed=0) -> dict:
    """Paired leading indicators B - A: act3 share and act2_boss_pass."""
    if wa.get("lead_per_seed") and wb.get("lead_per_seed"):
        keys = sorted(wa["per_seed"])
        la, lb = wa["lead_per_seed"], wb["lead_per_seed"]
        d3 = [lb["act3"][k] - la["act3"][k] for k in keys]
        act3 = diff_stats(d3, boot, seed)
        act3.update(a=statistics.fmean(la["act3"][k] for k in keys), b=statistics.fmean(lb["act3"][k] for k in keys))
        if wa.get("single_masks") and wb.get("single_masks"):
            bit = 1 << ES.STAGE_KEYS.index("act3")
            act3["mcnemar"] = {"b_only": sum(1 for k in keys if wb["single_masks"][k] & bit and not
                                             wa["single_masks"][k] & bit),
                               "a_only": sum(1 for k in keys if wa["single_masks"][k] & bit and not
                                             wb["single_masks"][k] & bit)}
        pa, pb = _pass_rate(la, keys), _pass_rate(lb, keys)
        rng = random.Random(seed)
        n = len(keys)
        ra, ba = [la["act2_boss"][k] for k in keys], [la["act2_boss_beaten"][k] for k in keys]
        rb, bb = [lb["act2_boss"][k] for k in keys], [lb["act2_boss_beaten"][k] for k in keys]
        rows = list(zip(ra, ba, rb, bb))
        bs = []
        for _ in range(boot):
            s_ra = s_ba = s_rb = s_bb = 0.0
            for x1, x2, x3, x4 in rng.choices(rows, k=n):
                s_ra += x1
                s_ba += x2
                s_rb += x3
                s_bb += x4
            if s_ra > 0 and s_rb > 0:
                bs.append(s_bb / s_rb - s_ba / s_ra)
        bs.sort()
        pas = {"a": pa, "b": pb, "delta": (pb - pa) if pa is not None and pb is not None else None,
               "bootstrap_ci95": [percentile(bs, 2.5), percentile(bs, 97.5)] if bs else [None, None],
               "bootstrap_valid": len(bs), "ci_method": "per-seed joint bootstrap"}
        return {"act3": act3, "act2_boss_pass": pas}
    a3 = (wb["act3"] - wa["act3"]) if wa.get("act3") is not None and wb.get("act3") is not None else None
    return {"act3": {"a": wa.get("act3"), "b": wb.get("act3"), "delta_mean": a3, "ci_method": "means (no interval)"},
            "act2_boss_pass": {"delta": None, "ci_method": "unavailable without per-seed stages"}}


def paired(wa: dict, wb: dict, boot=BOOT, seed=0, jitter_var=DEFAULT_JITTER_VAR) -> dict:
    """Per-seed difference B - A of two windows (compare.py ``paired`` numbers), or the variance model."""
    if wa.get("mean") is None or wb.get("mean") is None:
        return {"a": wa["run"], "b": wb["run"], "delta_mean": None, "ci_method": None,
                "missing": [w["run"] for w in (wa, wb) if w.get("mean") is None]}
    ia, ib = wa.get("seedset_identity") or {}, wb.get("seedset_identity") or {}
    if ia.get("sha256") and ib.get("sha256") and ia["sha256"] != ib["sha256"]:
        raise ValueError(f"{wa['run']} and {wb['run']} are evaluations of different seed sets")
    out = {"a": wa["run"], "b": wb["run"], "a_updates": wa["updates"], "b_updates": wb["updates"],
           "a_mean": wa["mean"], "b_mean": wb["mean"], "aligned": wa["updates"] == wb["updates"]}
    if wa["method"] == "per-seed" and wb["method"] == "per-seed":
        keys = sorted(wa["per_seed"])
        if keys != sorted(wb["per_seed"]):
            raise ValueError(f"{wa['run']} and {wb['run']} hold different games")
        xa = [wa["per_seed"][k] for k in keys]
        xb = [wb["per_seed"][k] for k in keys]
        d = [y - x for x, y in zip(xa, xb)]
        out.update(diff_stats(d, boot, seed))
        out["ci95"] = out["bootstrap_ci95"]
        out["corr"] = pearson(xa, xb)
        out["per_seed_delta"] = {k: round(x, 6) for k, x in zip(keys, d)}
    else:
        n = min(x for x in (wa.get("games"), wb.get("games")) if x) if (wa.get("games") or wb.get("games")) else 128
        out.update(model_ci(wb["mean"] - wa["mean"], n, max(1, wa["n_points"]), max(1, wb["n_points"]), jitter_var))
    out["leading"] = leading_pair(wa, wb, boot, seed)
    return out


def ols(xs, ys) -> dict:
    n = len(xs)
    mx, my = statistics.fmean(xs), statistics.fmean(ys)
    sxx = math.fsum((x - mx) ** 2 for x in xs)
    b = math.fsum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sxx
    out = {"slope": b, "intercept": my - b * mx, "n_points": n}
    if n > 2:
        rss = math.fsum((y - (my + b * (x - mx))) ** 2 for x, y in zip(xs, ys))
        se = math.sqrt(rss / (n - 2) / sxx)
        half = t_ppf(0.975, n - 2) * se
        out.update(se=se, ci95=[b - half, b + half])
    else:
        out.update(se=None, ci95=[None, None])
    return out


def slope(ix: Index, seedset: str, mode: str = "greedy-off", lo=None, hi=None, last_n=None, per=500,
          boot=BOOT, seed=0, allow_blind=False) -> dict:
    pts = ix.points(seedset, mode, allow_blind=allow_blind)
    pts = [e for e in pts if (lo is None or e["update"] >= lo) and (hi is None or e["update"] <= hi)]
    if last_n:
        pts = pts[-int(last_n):]
    out = {"run": ix.label, "seedset": seedset, "mode": mode, "from": lo, "to": hi, "per_updates": per,
           "updates": [e["update"] for e in pts], "n_points": len(pts)}
    if len(pts) < 2 or len({e["update"] for e in pts}) < 2:
        out.update(method=None, slope=None, ci95=[None, None], plateau=None)
        return out
    xs = [float(e["update"]) for e in pts]
    om = ols(xs, [e["mean"] for e in pts])
    out["means_ols"] = {"slope": om["slope"] * per, "se": om["se"] * per if om["se"] is not None else None,
                        "ci95": [c * per if c is not None else None for c in om["ci95"]], "n_points": om["n_points"]}
    keysets = {tuple(sorted(e["plays"])) for e in pts if e["plays"] is not None}
    if all(e["plays"] is not None for e in pts) and len(keysets) == 1:
        keys = list(next(iter(keysets)))
        mx = statistics.fmean(xs)
        sxx = math.fsum((x - mx) ** 2 for x in xs)
        sl = []
        for k in keys:
            ys = [_seed_mean(e, k) for e in pts]
            my = statistics.fmean(ys)
            sl.append(math.fsum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sxx * per)
        bci = bootstrap_mean_ci(sl, boot, seed)
        out.update(method="per-seed", slope=statistics.fmean(sl), t_ci95=t_interval(sl), bootstrap_ci95=bci,
                   ci95=bci, games=len(keys))
    else:
        out.update(method="means", slope=out["means_ols"]["slope"], ci95=out["means_ols"]["ci95"])
    up = out["ci95"][1]
    out["plateau"] = (up is not None and up < PLATEAU_PER500 * per / 500.0)
    return out


# ------------------------------------------------------------------------------------------------ CLI


def _f(x, nd=4, sign=False):
    if x is None:
        return "n/a"
    return f"{x:+.{nd}f}" if sign else f"{x:.{nd}f}"


def _ci(c, nd=4):
    if not c or c[0] is None:
        return "[n/a]"
    return f"[{c[0]:+.{nd}f}, {c[1]:+.{nd}f}]"


def line_window(w):
    return (f"window {w['run']} {w['seedset']} {w['mode']} at u{w['at']} k={w['k']}: {_f(w['mean'])} "
            f"({w['method']}, {w['n_points']} pts {w['updates'][:1]}..{w['updates'][-1:]}"
            + (f", act3 {_f(w['act3'], 3)}" if w.get('act3') is not None else "") + ")")


def line_pair(p, label="pair"):
    if p.get("delta_mean") is None:
        return f"{label} {p['b']} - {p['a']}: n/a (missing {p.get('missing')})"
    lead = p.get("leading") or {}
    a3 = (lead.get("act3") or {}).get("delta_mean")
    ap = (lead.get("act2_boss_pass") or {}).get("delta")
    extra = f" | act3 {_f(a3, 4, True)}" + (f" a2pass {_f(ap, 3, True)}" if ap is not None else "")
    if p["ci_method"] == "per-seed":
        return (f"{label} {p['b']} - {p['a']}: {_f(p['delta_mean'], 4, True)} t{_ci(p['t_ci95'])} "
                f"boot{_ci(p['bootstrap_ci95'])} n={p['n']} {p['better']}/{p['worse']}/{p['same']} "
                f"corr {_f(p.get('corr'), 3)}" + extra)
    return (f"{label} {p['b']} - {p['a']}: {_f(p['delta_mean'], 4, True)} model{_ci(p['ci95'])} "
            f"(variance model J={p['jitter_var']}, k {len(p['a_updates'])}/{len(p['b_updates'])}, n={p['n']})" + extra)


def line_slope(s):
    if s.get("slope") is None:
        return f"slope {s['run']} {s['seedset']}: n/a ({s['n_points']} points)"
    return (f"slope {s['run']} {s['seedset']} {s['mode']} u{s['updates'][0]}-{s['updates'][-1]} "
            f"({s['n_points']} pts, {s['method']}): {_f(s['slope'], 4, True)}/{s['per_updates']} {_ci(s['ci95'])}"
            f" means-OLS {_f(s['means_ols']['slope'], 4, True)} +/- {_f(s['means_ols']['se'], 4)}"
            + (" PLATEAU" if s["plateau"] else ""))


def _strip(o):
    """JSON without the bulky per-seed maps (kept with --full)."""
    if isinstance(o, dict):
        return {k: _strip(v) for k, v in o.items()
                if k not in ("per_seed", "lead_per_seed", "single_masks", "per_seed_delta")}
    if isinstance(o, list):
        return [_strip(v) for v in o]
    return o


def emit(text: str, obj: dict, a) -> None:
    print(text)
    for w in obj.get("warnings") or []:
        print(f"warning: {w}", file=sys.stderr)
    if a.json:
        o = obj if a.full else _strip(obj)
        s = json.dumps(o, indent=1)
        if a.json == "-":
            print(s)
        else:
            with open(a.json, "w", encoding="utf-8") as f:
                f.write(s + "\n")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="evalstats.py", description=__doc__.split("\n\n")[0],
                                 epilog=__doc__.split("\n\n", 1)[1], formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p, at=True):
        p.add_argument("--seedset", default="dev128", help="seed set id / name / alias (default dev128)")
        p.add_argument("--mode", default="greedy-off", help="eval mode name (default greedy-off)")
        p.add_argument("--allow-blind", action="store_true", help="permit reading a blind seed set")
        p.add_argument("--json", default=None, help="write the JSON result here ('-' = stdout)")
        p.add_argument("--full", action="store_true", help="keep the per-seed maps in the JSON")
        if at:
            p.add_argument("--k", type=int, default=5, help="checkpoints per window (default 5)")
            p.add_argument("--after", type=int, default=None, help="only checkpoints with update > AFTER (fork)")

    p = sub.add_parser("window", help="K-checkpoint per-seed window of one run")
    p.add_argument("src")
    p.add_argument("--at", type=int, required=True)
    common(p)
    p = sub.add_parser("pair", help="paired window difference B - A")
    p.add_argument("a")
    p.add_argument("b")
    p.add_argument("--at", type=int, required=True, help="window end of A (and of B unless --at-b)")
    p.add_argument("--at-b", type=int, default=None, help="window end of B")
    p.add_argument("--jitter-var", type=float, default=DEFAULT_JITTER_VAR,
                   help=f"per-game checkpoint-jitter variance of the means-only model (default {DEFAULT_JITTER_VAR})")
    common(p)
    p = sub.add_parser("slope", help="OLS slope per 500 updates (several SRCs = one lineage)")
    p.add_argument("srcs", nargs="+")
    p.add_argument("--from", dest="lo", type=int, default=None)
    p.add_argument("--to", dest="hi", type=int, default=None)
    p.add_argument("--last-n", type=int, default=None)
    p.add_argument("--per", type=int, default=500, help="slope unit in updates (default 500)")
    common(p, at=False)
    p = sub.add_parser("index", help="list the checkpoints found")
    p.add_argument("src")
    p.add_argument("--seedset", default=None)
    p.add_argument("--json", default=None)
    p.add_argument("--full", action="store_true")
    a = ap.parse_args(argv)
    warnings: list = []
    try:
        if a.cmd == "window":
            ix = Index(a.src, warnings)
            w = window(ix, a.seedset, a.at, a.k, a.mode, a.after, a.allow_blind)
            w["warnings"] = warnings
            emit(line_window(w), w, a)
        elif a.cmd == "pair":
            ia, ib = Index(a.a, warnings), Index(a.b, warnings)
            wa = window(ia, a.seedset, a.at, a.k, a.mode, a.after, a.allow_blind)
            wb = window(ib, a.seedset, a.at_b if a.at_b is not None else a.at, a.k, a.mode, a.after, a.allow_blind)
            p = paired(wa, wb, jitter_var=a.jitter_var)
            if a.at_b is None and p.get("aligned") is False:
                warnings.append(f"windows not aligned: {wa['run']} {wa['updates']} vs {wb['run']} {wb['updates']}")
            r ={"pair": p, "a": wa, "b": wb, "warnings": warnings}
            emit("\n".join([line_window(wa), line_window(wb), line_pair(p)]), r, a)
        elif a.cmd == "slope":
            ix = merge_lineage([Index(s, warnings) for s in a.srcs])
            s = slope(ix, a.seedset, a.mode, a.lo, a.hi, a.last_n, a.per, allow_blind=a.allow_blind)
            s["warnings"] = warnings
            emit(line_slope(s), s, a)
        else:
            ix = Index(a.src, warnings)
            rows = []
            for (sk, m, u), e in sorted(ix.entries.items(), key=lambda kv: (str(kv[0][0]), str(kv[0][1]), kv[0][2])):
                if a.seedset and not seedset_matches(a.seedset, e["seedset"]):
                    continue
                rows.append({"seedset": sk, "mode": m, "update": u, "source": e["source"], "mean": e["mean"],
                             "per_seed": e["plays"] is not None, "path": e["paths"][0]})
            text = "\n".join(f"{r['seedset']} {r['mode']} u{r['update']} {r['mean']:.4f} "
                             f"{'per-seed' if r['per_seed'] else 'means'} {r['path']}" for r in rows)
            emit(text or "(no checkpoints)", {"run": ix.label, "points": rows, "warnings": warnings}, a)
    except (ValueError, FileNotFoundError, PermissionError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
