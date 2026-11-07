#!/usr/bin/env python3
"""decide.py: a pre-registration JSON and the eval data in, ADOPT / REJECT / CONTINUE / REPLICATE / KILL out.

    python3 -I scripts/analysis/decide.py PREREG.json --at U [--data RUN=PATH[,PATH...]] [--root DIR ...]
            [--out DIR] [--no-write] [--ab-log FILE] [--json -]

PREREG (``scripts/analysis/prereg/*.json``, committed before the run starts; schema ``sts2.prereg/1``)::

    {"schema": "sts2.prereg/1", "name": "s2-vs-c4",
     "arms": {"treat": ["w10-s2"], "control": ["w4-c4"]},      # paired by index = by training seed / stream
     "labels": {"treat": "S2", "control": "C4"},
     "fork": 12164, "at": [12664, 13164], "max_at": 13164,       # pre-registered reads; max_at = last one
     "primary": {"seedset": ["dev512", "dev128"], "window": 5, "mode": "greedy-off"},   # first set both arms have
     "confirm": {"seedset": "ab512", "mode": "greedy-off", "endpoint": true},
     "mde": 0.015, "margin": 0.01,
     "kill": {"after_updates": 300, "delta": -0.03},             # vs the paired control window
     "futility": {"from_read": 2},                               # REJECT on upper < mde/2 from the 2nd read on
     "leading": ["act3", "act2_boss_pass"]}

Data: --data RUN=PATH[,PATH...] (eval dirs, trees, run mirrors with evals.jsonl, summary.json files; see
evalstats.py) and / or --root DIR (DIR/<run> and DIR/runs/<run> are used for every run of the prereg when they
exist). Per pair i (treat_i, control_i): the primary window (evalstats.window: the K latest checkpoints with
fork < update <= U, per seed) and the paired per-seed difference treat - control; pooled over pairs as the per-seed
mean of the pair differences (10,000-resample seed bootstrap; the variance model when a checkpoint has means only).

Verdicts, first match wins (U = --at):
  KILL       U - fork >= kill.after_updates and pooled delta < kill.delta and CI upper < 0 (any read, every 100 ok)
  CONTINUE   U is not a pre-registered read (monitoring read: only KILL can fire; the confirm set is not read)
  CONTINUE   pairing_wait: some arm's primary data does not reach U yet
  ADOPT      >= 2 pairs, pooled CI lower > 0, every pair's delta > 0, confirm delta > 0 and confirm CI lower > -margin
  REPLICATE  1 pair and pooled CI lower > 0 (never ADOPT on one training seed)
  REJECT     pooled CI upper < 0 (harm), or upper < mde / 2 from the futility.from_read-th read on (futility), or
             U >= max_at without ADOPT / REPLICATE
  CONTINUE   otherwise
Deviation from the audit plan (EV5): futility (upper < mde/2) is not applied at the first pre-registered read by
default: early windows sit mostly before the effect has accumulated (S - C3 @u11164: window -0.008 [-0.022, +0.006]
would have read REJECT, the same contrast read +0.000 at u11664).

The confirm read (k = 1 at exactly U, pre-registered reads only) is appended to the ab-access log (default
<out>/../ab-access.jsonl) with the prior read count. Leading indicators: paired act3 share difference (with McNemar
discordant counts for single checkpoints) and act2_boss_pass difference (joint seed bootstrap), per pair.
Writes <out>/pair.json (default: <treat run dir>/evals/pairs/u<U>-vs-<control>/ when the treat run's first path is a
run directory with evals/, else ./pairs/<name>/u<U>-vs-<control>/) and prints one line (--json - also prints the JSON).
Standard library only; run as ``python3 -I``.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import statistics
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python3 -I does not add the script's directory

import evalstats as EV  # noqa: E402

VERDICTS = ("ADOPT", "REJECT", "CONTINUE", "REPLICATE", "KILL")
SCHEMA = "sts2.prereg/1"


class PreregError(ValueError):
    pass


def load_prereg(path: str) -> tuple[dict, str]:
    with open(path, "rb") as f:
        raw = f.read()
    p = json.loads(raw.decode("utf-8"))
    if p.get("schema") != SCHEMA:
        raise PreregError(f"{path}: schema must be {SCHEMA}")
    arms = p.get("arms") or {}
    t, c = arms.get("treat") or [], arms.get("control") or []
    if not t or len(t) != len(c):
        raise PreregError(f"{path}: arms.treat and arms.control must be non-empty lists of equal length")
    if not isinstance(p.get("fork"), int):
        raise PreregError(f"{path}: fork must be an update number")
    at = p.get("at")
    at = [at] if isinstance(at, int) else list(at or [])
    if not at or any(not isinstance(x, int) or x <= p["fork"] for x in at):
        raise PreregError(f"{path}: at must list update numbers after the fork")
    p["at"] = sorted(at)
    p.setdefault("max_at", p["at"][-1])
    pr = p.setdefault("primary", {})
    if isinstance(pr.get("seedset", "dev128"), str):
        pr["seedset"] = [pr.get("seedset", "dev128")]
    pr.setdefault("window", 5)
    pr.setdefault("mode", "greedy-off")
    if p.get("confirm"):
        p["confirm"].setdefault("mode", pr["mode"])
    for k in ("mde", "margin"):
        if not isinstance(p.get(k), (int, float)):
            raise PreregError(f"{path}: {k} must be a number")
    p.setdefault("futility", {}).setdefault("from_read", 2)
    p.setdefault("leading", ["act3", "act2_boss_pass"])
    return p, hashlib.sha256(raw).hexdigest()


def run_paths(run: str, data: dict, roots: list[str]) -> list[str]:
    out = list(data.get(run, []))
    for r in roots:
        for cand in (os.path.join(r, run), os.path.join(r, "runs", run)):
            if os.path.exists(cand) and cand not in out:
                out.append(cand)
    return out


def _ci(res: dict):
    return res.get("ci95") or res.get("bootstrap_ci95") or [None, None]


def pool(pairs: list[dict], boot: int = EV.BOOT) -> dict:
    """Pooled treat - control over the pairs: per-seed mean of the pair differences, else normal combination."""
    ok = [p for p in pairs if p.get("delta_mean") is not None]
    if not ok:
        return {"delta_mean": None, "ci95": [None, None], "ci_method": None, "n_pairs": 0}
    if len(ok) == 1:
        p = ok[0]
        return {"delta_mean": p["delta_mean"], "ci95": _ci(p), "ci_method": p["ci_method"], "n_pairs": 1,
                **({"t_ci95": p["t_ci95"]} if "t_ci95" in p else {})}
    if all(p["ci_method"] == "per-seed" for p in ok):
        keys = sorted(ok[0]["per_seed_delta"])
        if all(sorted(p["per_seed_delta"]) == keys for p in ok):
            d = [statistics.fmean(p["per_seed_delta"][k] for p in ok) for k in keys]
            s = EV.diff_stats(d, boot)
            s.update(ci95=s["bootstrap_ci95"], n_pairs=len(ok))
            return s
    m = len(ok)
    ses = []
    for p in ok:
        if p.get("se") is not None:
            ses.append(p["se"])
        else:
            ses.append((p.get("delta_sd") or 0.0) / math.sqrt(max(1, p.get("n") or 1)))
    delta = statistics.fmean(p["delta_mean"] for p in ok)
    se = math.sqrt(math.fsum(x * x for x in ses)) / m
    return {"delta_mean": delta, "se": se, "ci95": [delta - 1.96 * se, delta + 1.96 * se],
            "ci_method": "pooled-normal", "n_pairs": m}


def _count_reads(log: str, name: str) -> int:
    n = 0
    try:
        with open(log, encoding="utf-8") as f:
            for line in f:
                try:
                    if json.loads(line).get("prereg") == name:
                        n += 1
                except ValueError:
                    pass
    except OSError:
        pass
    return n


def decide(prereg: dict, at: int, indexes: dict, *, read_confirm: bool = True, boot: int = EV.BOOT) -> dict:
    """The verdict (no I/O besides what the indexes hold); ``indexes`` = {run: evalstats.Index}."""
    treat, control = prereg["arms"]["treat"], prereg["arms"]["control"]
    pr, fork = prereg["primary"], prereg["fork"]
    registered = at in prereg["at"]
    read_no = prereg["at"].index(at) + 1 if registered else None
    warnings = []
    # primary seed set: the first one every arm has data for in (fork, at]
    chosen = None
    for ss in pr["seedset"]:
        if all(indexes[r].points(ss, pr["mode"], after=fork, at=at) for r in treat + control):
            chosen = ss
            break
    out = {"prereg": prereg["name"], "at": at, "fork": fork, "registered_read": registered, "read_no": read_no,
           "pairs": [], "primary": {"seedset": chosen, "candidates": pr["seedset"], "window": pr["window"],
                                    "mode": pr["mode"]}, "confirm": None, "warnings": warnings}
    if chosen is None:
        out.update(verdict="CONTINUE", rule="pairing_wait: no primary data after the fork for every arm",
                   state="pairing_wait")
        return out
    wins = {}
    for r in treat + control:
        wins[r] = EV.window(indexes[r], chosen, at, pr["window"], pr["mode"], after=fork)
    for t, c in zip(treat, control):
        p = EV.paired(wins[c], wins[t], boot=boot)
        p["treat"], p["control"] = t, c
        p["treat_window"] = {k: wins[t][k] for k in ("updates", "mean", "method", "act3")}
        p["control_window"] = {k: wins[c][k] for k in ("updates", "mean", "method", "act3")}
        if not p.get("aligned", True):
            warnings.append(f"pair {t}-{c}: windows not aligned {wins[t]['updates']} vs {wins[c]['updates']}")
        out["pairs"].append(p)
    pooled = pool(out["pairs"], boot)
    out["primary"]["pooled"] = pooled
    a3 = [((p.get("leading") or {}).get("act3") or {}).get("delta_mean") for p in out["pairs"]]
    out["leading"] = {"act3_delta_mean": statistics.fmean(a3) if a3 and None not in a3 else None,
                      "per_pair": [{"treat": p["treat"], "control": p["control"], **(p.get("leading") or {})}
                                   for p in out["pairs"]]}
    lo, hi = pooled["ci95"]
    delta = pooled["delta_mean"]
    reach = all(wins[r]["updates"] and wins[r]["updates"][-1] == at for r in treat + control)
    # ---- KILL
    kill = prereg.get("kill")
    if kill and at - fork >= int(kill.get("after_updates", 0)) and delta is not None and hi is not None \
            and delta < float(kill["delta"]) and hi < 0:
        out.update(verdict="KILL", rule=f"window delta {delta:+.4f} < {kill['delta']} and CI upper {hi:+.4f} < 0 "
                                        f"after {at - fork} >= {kill['after_updates']} updates")
        return out
    if not registered:
        out.update(verdict="CONTINUE", rule=f"monitoring read (u{at} not in {prereg['at']}): only KILL applies",
                   state="monitoring")
        return out
    if not reach:
        last = {r: (wins[r]["updates"][-1] if wins[r]["updates"] else None) for r in treat + control}
        out.update(verdict="CONTINUE", rule=f"pairing_wait: primary data not yet at u{at} ({last})",
                   state="pairing_wait")
        return out
    # ---- confirm (pre-registered reads only)
    conf = prereg.get("confirm")
    cres = None
    if conf and read_confirm:
        cp = []
        for t, c in zip(treat, control):
            wt = EV.window(indexes[t], conf["seedset"], at, 1, conf["mode"], after=fork)
            wc = EV.window(indexes[c], conf["seedset"], at, 1, conf["mode"], after=fork)
            if wt["updates"] != [at] or wc["updates"] != [at]:
                cp.append({"treat": t, "control": c, "delta_mean": None,
                           "missing": [r for r, w in ((t, wt), (c, wc)) if w["updates"] != [at]]})
                continue
            p = EV.paired(wc, wt, boot=boot)
            p["treat"], p["control"] = t, c
            p["sources"] = wt["sources"] + wc["sources"]
            cp.append(p)
        cres = {"seedset": conf["seedset"], "mode": conf["mode"], "pairs": cp, "pooled": pool(cp, boot),
                "complete": all(p.get("delta_mean") is not None for p in cp)}
        out["confirm"] = cres
    n_pairs = pooled["n_pairs"]
    pair_pos = all(p.get("delta_mean") is not None and p["delta_mean"] > 0 for p in out["pairs"])
    # ---- ADOPT
    if n_pairs >= 2 and lo is not None and lo > 0 and pair_pos:
        if cres and cres["complete"]:
            cd, (clo, _chi) = cres["pooled"]["delta_mean"], cres["pooled"]["ci95"]
            if cd > 0 and clo > -float(prereg["margin"]):
                out.update(verdict="ADOPT", rule=f"{n_pairs} pairs, pooled lower {lo:+.4f} > 0, every pair > 0, "
                                                 f"confirm {cd:+.4f} > 0 with lower {clo:+.4f} > -{prereg['margin']}")
                return out
            why = f"confirm {cd:+.4f} lower {clo:+.4f} fails (> 0, > -{prereg['margin']})"
        elif conf:
            why = "confirm data missing"
        else:
            why = None
        if why is None:
            out.update(verdict="ADOPT", rule=f"{n_pairs} pairs, pooled lower {lo:+.4f} > 0, every pair > 0 "
                                             f"(no confirm set registered)")
            return out
        warnings.append(f"primary would ADOPT but {why}")
    # ---- REPLICATE
    if n_pairs == 1 and lo is not None and lo > 0:
        out.update(verdict="REPLICATE", rule=f"one training-seed pair, primary lower {lo:+.4f} > 0 "
                                             f"(delta {delta:+.4f}); needs a second pair before ADOPT")
        return out
    # ---- REJECT
    half = float(prereg["mde"]) / 2.0
    if hi is not None and hi < 0:
        out.update(verdict="REJECT", rule=f"harm: pooled CI upper {hi:+.4f} < 0")
        return out
    if hi is not None and hi < half and read_no >= int(prereg["futility"]["from_read"]):
        out.update(verdict="REJECT", rule=f"futility: pooled CI upper {hi:+.4f} < mde/2 {half:.4f} "
                                          f"(read {read_no} >= {prereg['futility']['from_read']})")
        return out
    if at >= prereg["max_at"]:
        out.update(verdict="REJECT", rule=f"u{at} = max_at {prereg['max_at']} without ADOPT / REPLICATE "
                                          f"(pooled {delta:+.4f} [{lo:+.4f}, {hi:+.4f}], {n_pairs} pair(s))")
        return out
    out.update(verdict="CONTINUE", rule=f"pooled {delta:+.4f} [{lo:+.4f}, {hi:+.4f}] inconclusive at read "
                                        f"{read_no}/{len(prereg['at'])}")
    return out


def line(r: dict) -> str:
    pr = r["primary"]
    pooled = pr.get("pooled") or {}
    s = f"{r['verdict']} {r['prereg']} @u{r['at']}: {r['rule']}"
    if pooled.get("delta_mean") is not None:
        lo, hi = pooled["ci95"]
        s += (f" | primary {pr['seedset']} w{pr['window']} {pooled['delta_mean']:+.4f} [{lo:+.4f}, {hi:+.4f}] "
              f"({pooled['n_pairs']} pair(s), {pooled['ci_method']})")
    c = r.get("confirm")
    if c:
        cp = c["pooled"]
        if cp.get("delta_mean") is not None:
            s += f" | confirm {c['seedset']} {cp['delta_mean']:+.4f} [{cp['ci95'][0]:+.4f}, {cp['ci95'][1]:+.4f}]"
        else:
            s += f" | confirm {c['seedset']} missing"
    a3 = (r.get("leading") or {}).get("act3_delta_mean")
    if a3 is not None:
        s += f" | act3 {a3:+.4f}"
    return s


def default_out(prereg: dict, at: int, paths: list[str]) -> str:
    ctl = prereg["arms"]["control"][0]
    tail = f"u{at:07d}-vs-{ctl}"
    first = paths[0] if paths else None
    if first and os.path.isdir(os.path.join(first, "evals")):
        return os.path.join(first, "evals", "pairs", tail)
    return os.path.join("pairs", prereg["name"], tail)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="decide.py", description=__doc__.split("\n\n")[0],
                                 epilog=__doc__.split("\n\n", 1)[1], formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("prereg", help="pre-registration JSON (sts2.prereg/1)")
    ap.add_argument("--at", type=int, required=True, help="the read point (update)")
    ap.add_argument("--data", action="append", default=[], metavar="RUN=PATH[,PATH]", help="data of one run")
    ap.add_argument("--root", action="append", default=[], help="DIR with <run>/ or runs/<run>/ subdirectories")
    ap.add_argument("--out", default=None, help="output directory of pair.json")
    ap.add_argument("--no-write", action="store_true", help="print only (no pair.json, no ab-access log line)")
    ap.add_argument("--ab-log", default=None, help="confirm-set access log (default <out>/../ab-access.jsonl)")
    ap.add_argument("--json", default=None, help="'-' prints the JSON result after the line")
    a = ap.parse_args(argv)
    try:
        prereg, sha = load_prereg(a.prereg)
        data = {}
        for d in a.data:
            run, _, rest = d.partition("=")
            if not rest:
                ap.error(f"--data {d}: expected RUN=PATH[,PATH]")
            data.setdefault(run, []).extend(p for p in rest.split(",") if p)
        runs = prereg["arms"]["treat"] + prereg["arms"]["control"]
        indexes, paths = {}, {}
        all_warn: list = []
        for r in runs:
            paths[r] = run_paths(r, data, a.root)
            if not paths[r]:
                all_warn.append(f"{r}: no data paths (give --data {r}=PATH or --root)")
            ix = EV.Index([], all_warn)
            if paths[r]:
                ix = EV.Index(f"{r}=" + ",".join(paths[r]), all_warn)
            ix.label = r
            indexes[r] = ix
        res = decide(prereg, a.at, indexes)
    except (ValueError, FileNotFoundError, PermissionError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    res["warnings"] = all_warn + res["warnings"]
    res["prereg_file"] = os.path.abspath(a.prereg)
    res["prereg_sha256"] = sha
    res["prereg_body"] = prereg
    res["data_paths"] = paths
    res["generated_at"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    out_dir = a.out or default_out(prereg, a.at, paths[prereg["arms"]["treat"][0]])
    if res.get("confirm") and not a.no_write:
        log = a.ab_log or os.path.join(os.path.dirname(os.path.abspath(out_dir)), "ab-access.jsonl")
        before = _count_reads(log, prereg["name"])
        res["confirm"]["reads_before"] = before
        if before:
            res["warnings"].append(f"confirm set already read {before} time(s) for {prereg['name']} ({log})")
        os.makedirs(os.path.dirname(os.path.abspath(log)), exist_ok=True)
        with open(log, "a", encoding="utf-8") as f:
            f.write(json.dumps({"time": res["generated_at"], "prereg": prereg["name"], "prereg_sha256": sha,
                                "at": a.at, "seedset": res["confirm"]["seedset"], "mode": res["confirm"]["mode"],
                                "complete": res["confirm"]["complete"], "verdict": res["verdict"],
                                "purpose": "decide.py confirm read"}) + "\n")
        res["confirm"]["log"] = log
    text = line(res)
    print(text)
    for w in res["warnings"]:
        print(f"warning: {w}", file=sys.stderr)
    if not a.no_write:
        os.makedirs(out_dir, exist_ok=True)
        with open(os.path.join(out_dir, "pair.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, indent=1)
            f.write("\n")
        print(f"wrote {os.path.join(out_dir, 'pair.json')}", file=sys.stderr)
    if a.json == "-":
        print(json.dumps(res, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
