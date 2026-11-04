"""``sts2train compare``: per-seed paired comparison of two or more protocol evaluations of one seed set.

    sts2train compare GREEDY_OFF0,GREEDY_OFF1 SAMPLE_OFF0,SAMPLE_OFF1 GREEDY_ON0,GREEDY_ON1 \
        --labels greedy-off,sample-off,greedy-on [--json OUT.json]

An *arm* is one comma-separated list of run directories: protocol run directories (``sts2train eval`` /
``sts2vec eval``: config.json + games/) and / or repeat parents (``sts2train eval --repeats R``: ``r<k>/`` children),
the slices of one seed set (``--chunk``; config.json ``parent_seedset``) merged. Every arm must cover the same games of
the same seed set (identity: the full set's sha256). A game with several plays (repeats) enters with the mean of its
plays, so every seed weighs the same.

Per arm: games, plays, progress mean [95% t interval over the per-seed means], clear rate, results, the stage funnel
(``reportcard.stages``: reached / beat the Act-1 boss, reached Act 2, reached / beat the Act-2 boss, Act 3, cleared;
share of plays), floor mean, decisions per play, UI revisit cycles (``sts2train.cycles``: total, per play, plays with
>= 1, rest / shop), loop-breaker interventions (total, plays with >= 1), stalls (``stuck`` plays and where: phase,
floor), wall seconds (sum of the runs' ``run.json``). Per pair (B - A, every pair in the given order): the per-seed
difference of the progress means: mean, sd, 95% t interval, 95% percentile-bootstrap interval (10,000 resamples of
the seeds, seed 0), better / worse / same, and, when both arms play each game once, how many games have identical
action sequences.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from collections import Counter
from pathlib import Path

import numpy as np
from sts2vec import protocol as PG

BOOT = 10_000


def _read(p: Path):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def run_dirs(spec: str) -> list[Path]:
    """The protocol run directories of an arm spec (comma-separated; a repeat parent expands to its r<k>/)."""
    out = []
    for x in [x for x in str(spec).split(",") if x.strip()]:
        d = Path(x.strip())
        if (d / "config.json").exists():
            out.append(d)
            continue
        kids = sorted((p.parent for p in d.glob("r*/config.json")), key=lambda p: (len(p.name), p.name))
        if not kids:
            raise FileNotFoundError(f"{d}: neither a protocol run directory nor a repeat parent (r<k>/)")
        out += kids
    return out


def load_arm(spec: str, label: str | None = None) -> dict:
    """{label, dirs, parent (seed-set identity), ascension, plays: {key: [summary, ...]}, wall_s, modes}."""
    dirs = run_dirs(spec)
    plays: dict[str, list[dict]] = {}
    parents, modes, wall = {}, Counter(), 0.0
    asc = None
    for d in dirs:
        cfg = _read(d / "config.json")
        ss = cfg["seedset"]
        par = cfg.get("parent_seedset") or {"name": ss.get("name"), "id": ss.get("id"),
                                            "sha256": PG.checksum(ss), "games": len(ss["games"])}
        parents[par["sha256"]] = par
        asc = int(ss["ascension"])
        m = cfg.get("eval_mode") or {}
        modes[m.get("name") or (f"greedy-{(cfg.get('loop_breaker') or {}).get('mode') or 'off'}"
                                if cfg.get("greedy") else str(cfg.get("policy")))] += 1
        rj = d / "run.json"
        wall += float((_read(rj) if rj.exists() else {}).get("wall_seconds") or 0.0)
        for g in ss["games"]:
            k = PG.game_key(g)
            plays.setdefault(k, []).append(_read(d / "games" / f"{k}.summary.json"))
    if len(parents) != 1:
        raise ValueError(f"arm {label or spec}: run directories of {len(parents)} different seed sets")
    par = next(iter(parents.values()))
    if len(plays) != int(par["games"]):
        raise ValueError(f"arm {label or spec}: {len(plays)} of the seed set's {par['games']} games (a slice missing?)")
    return {"label": label or Path(dirs[0]).name, "dirs": [str(d) for d in dirs], "parent": par, "ascension": asc,
            "plays": plays, "wall_s": round(wall, 2), "modes": dict(modes)}


def t_interval(x: np.ndarray) -> list[float]:
    from .lookahead import t_ppf

    n = len(x)
    if n < 2:
        return [float(x.mean()), float(x.mean())] if n else [None, None]
    half = t_ppf(0.975, n - 1) * float(x.std(ddof=1)) / math.sqrt(n)
    return [float(x.mean() - half), float(x.mean() + half)]


def seed_means(arm: dict, keys: list[str]) -> np.ndarray:
    return np.array([float(np.mean([float(s["progress"] or 0.0) for s in arm["plays"][k]])) for k in keys])


def arm_stats(arm: dict) -> dict:
    from .reportcard import STAGES, stages

    keys = sorted(arm["plays"])
    allp = [s for k in keys for s in arm["plays"][k]]
    n = len(allp)
    pm = seed_means(arm, keys)
    res = Counter(s.get("result") for s in allp)
    flags = [stages(s, arm["ascension"]) for s in allp]
    cyc = [int(s.get("cycles") or 0) for s in allp]
    lb = [int(s.get("loop_breaks") or 0) for s in allp]
    stuck_at = Counter(f"{(s.get('stuck') or {}).get('phase')}@F{(s.get('stuck') or {}).get('floor')}"
                       for s in allp if s.get("result") == "stuck")
    return {"label": arm["label"], "modes": arm["modes"], "games": len(keys), "plays": n,
            "plays_per_game": n / len(keys) if keys else None,
            "progress_mean": float(pm.mean()), "progress_ci95_t": t_interval(pm),
            "clear_rate": res.get("win", 0) / n if n else None, "results": dict(sorted(res.items(), key=str)),
            "funnel": {key: sum(f[key] for f in flags) / n for key, _ in STAGES},
            "floor_mean": float(np.mean([int(s.get("floor") or 0) for s in allp])),
            "decisions_mean": float(np.mean([int(s.get("steps") or 0) for s in allp])),
            "cycles_total": int(sum(cyc)), "cycles_per_play": sum(cyc) / n if n else None,
            "plays_with_cycle": sum(1 for c in cyc if c > 0),
            "plays_with_cycle_share": sum(1 for c in cyc if c > 0) / n,
            "cycles_rest": int(sum(int(s.get("cycles_rest") or 0) for s in allp)),
            "cycles_shop": int(sum(int(s.get("cycles_shop") or 0) for s in allp)),
            "cycles_counted": all("cycles" in s for s in allp),
            "loop_breaks_total": int(sum(lb)), "plays_with_loop_break": sum(1 for x in lb if x > 0),
            "plays_with_loop_break_share": sum(1 for x in lb if x > 0) / n,
            "stuck": res.get("stuck", 0), "stuck_share": res.get("stuck", 0) / n,
            "stuck_at": dict(stuck_at.most_common()),
            "replayable": sum(1 for s in allp if s.get("replayable")), "wall_s": arm["wall_s"], "dirs": arm["dirs"]}


def paired(a: dict, b: dict, boot: int = BOOT, seed: int = 0) -> dict:
    """Per-seed difference B - A of the progress means (the arms' games must match)."""
    if a["parent"]["sha256"] != b["parent"]["sha256"]:
        raise ValueError(f"{a['label']} and {b['label']} are evaluations of different seed sets")
    keys = sorted(a["plays"])
    if keys != sorted(b["plays"]):
        raise ValueError(f"{a['label']} and {b['label']} hold different games")
    d = seed_means(b, keys) - seed_means(a, keys)
    n = len(d)
    bm = d[np.random.default_rng(seed).integers(0, n, size=(boot, n))].mean(1)
    lo, hi = np.percentile(bm, [2.5, 97.5])
    out = {"a": a["label"], "b": b["label"], "games": n, "delta_mean": float(d.mean()),
           "delta_sd": float(d.std(ddof=1)) if n > 1 else 0.0, "t_ci95": t_interval(d),
           "bootstrap_ci95": [float(lo), float(hi)], "bootstrap_resamples": boot,
           "better": int((d > 1e-9).sum()), "worse": int((d < -1e-9).sum()), "same": int((np.abs(d) <= 1e-9).sum()),
           "per_game": {k: round(float(x), 6) for k, x in zip(keys, d)}}
    if all(len(a["plays"][k]) == 1 and len(b["plays"][k]) == 1 for k in keys):
        out["identical_actions"] = sum(1 for k in keys if a["plays"][k][0].get("history_actions")
                                       == b["plays"][k][0].get("history_actions"))
    return out


def compare(specs: list[str], labels: list[str] | None = None) -> dict:
    labels = list(labels or [])
    arms = [load_arm(s, labels[i] if i < len(labels) else None) for i, s in enumerate(specs)]
    shas = {a["parent"]["sha256"] for a in arms}
    if len(shas) != 1:
        raise ValueError("the arms evaluate different seed sets: " + ", ".join(
            f"{a['label']} {a['parent'].get('name')} {a['parent']['sha256'][:12]}" for a in arms))
    stats = [arm_stats(a) for a in arms]
    pairs = [paired(arms[i], arms[j]) for i in range(len(arms)) for j in range(i + 1, len(arms))]
    p = arms[0]["parent"]
    return {"seedset": {k: p.get(k) for k in ("name", "id", "sha256", "games")}, "arms": stats, "pairs": pairs}


def _f(x, nd=3):
    return "n/a" if x is None else f"{x:.{nd}f}"


def render(r: dict) -> str:
    from .reportcard import STAGES

    ss = r["seedset"]
    L = [f"seed set {ss.get('id') or ss.get('name')} ({ss['games']} games, sha256 {ss['sha256'][:12]})", ""]
    head = ["arm", "plays", "progress [95% t]", "clear", "stuck", "plays w/ cycle", "cycles/play", "rest/shop",
            "breaker plays", "decisions", "wall s"]
    L.append(" | ".join(head))
    for a in r["arms"]:
        lo, hi = a["progress_ci95_t"]
        L.append(" | ".join([a["label"], str(a["plays"]), f"{_f(a['progress_mean'])} [{_f(lo)}, {_f(hi)}]",
                             f"{a['clear_rate']:.3f}", f"{a['stuck']} ({a['stuck_share']:.1%})",
                             f"{a['plays_with_cycle']} ({a['plays_with_cycle_share']:.1%})",
                             _f(a["cycles_per_play"], 2), f"{a['cycles_rest']}/{a['cycles_shop']}",
                             f"{a['plays_with_loop_break']} ({a['plays_with_loop_break_share']:.1%})",
                             _f(a["decisions_mean"], 1), _f(a["wall_s"], 1)]))
    L += ["", "funnel (share of plays): " + ", ".join(lab for _, lab in STAGES)]
    for a in r["arms"]:
        L.append(f"  {a['label']}: " + " / ".join(f"{a['funnel'][k]:.3f}" for k, _ in STAGES))
    L.append("")
    for a in r["arms"]:
        if a["stuck_at"]:
            L.append(f"stalls {a['label']}: " + ", ".join(f"{k} x{v}" for k, v in list(a["stuck_at"].items())[:8]))
    L += ["", "paired per seed (B - A): mean [95% t] [95% bootstrap] better/worse/same"]
    for p in r["pairs"]:
        L.append(f"  {p['b']} - {p['a']}: {p['delta_mean']:+.4f} [{p['t_ci95'][0]:+.4f}, {p['t_ci95'][1]:+.4f}] "
                 f"[{p['bootstrap_ci95'][0]:+.4f}, {p['bootstrap_ci95'][1]:+.4f}] {p['better']}/{p['worse']}/"
                 f"{p['same']}" + (f", identical actions {p['identical_actions']}" if "identical_actions" in p else ""))
    return "\n".join(L)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="sts2train compare", description=__doc__.split("\n\n")[0])
    ap.add_argument("arms", nargs="+", help="one arm per argument: comma-separated run directories (slices / repeats)")
    ap.add_argument("--labels", default=None, help="comma-separated arm labels (default: the first directory's name)")
    ap.add_argument("--json", default=None, help="also write the full result (per-game differences included) here")
    a = ap.parse_args(argv)
    if len(a.arms) < 2:
        ap.error("compare needs at least two arms")
    r = compare(a.arms, [x for x in (a.labels or "").split(",") if x] or None)
    print(render(r))
    if a.json:
        Path(a.json).write_text(json.dumps(r, indent=1) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
