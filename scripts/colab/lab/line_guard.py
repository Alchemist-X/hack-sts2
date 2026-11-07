#!/usr/bin/env python3
"""line_guard.py RUN_DIR [--rules RULES.json] [--mode alert|kill] [--once] [--dry-run] ...

Sidecar kill-rule guard for one training line (backlog GUARD = ops F3 + process P2/P3 + eval EV5 KILL part).
Stdlib only; runs with ``python3 -I``. It tails RUN_DIR/metrics.jsonl and RUN_DIR/evals.jsonl, evaluates the
rules of a rules file (default: guard-rules-default.json next to this script) and, when a rule fires:

  --mode alert (default)  appends a reason line to <state>/ALERT, merges {reason, alert_*} into
                          <state>/line_status.json (the state field is left alone) and prints the reason line.
  --mode kill             touches RUN_DIR/STOP (the v2 driver checks it before every session), SIGTERMs the trainer
                          pid from status.json (graceful: it checkpoints, then stops after this update), SIGTERMs the
                          driver's process group (w3m_line*.sh restarts a trainer that exits rc 0 below the segment
                          end, so the driver must go too), waits up to --kill-wait s for the trainer to exit and writes
                          line_status.json state "killed" with the rule, key, update and reason.
  --dry-run               prints every action (files, pids, process groups) instead of performing it.

The trainer pid is only signalled when status.json's host is this host and the pid's command line mentions RUN_DIR
(a mirrored status.json from another machine never maps to a local process). The driver is --driver-pid, or the
process whose command line contains --driver-match (default "w3m_line") and RUN_DIR as a separate word.

Rules file: {"warmup_updates": N, "fork": {"run": DIR, "update": N, "eval": X}, "control": DIR, "rules": [RULE...]}.
CLI --fork-run/--fork-update/--fork-eval/--control override the file. The fork update defaults to the checkpoint
number of config.json sessions[0].resumed_from, else the first metrics row's update - 1. RULE fields:

  name              label (default metric)
  source            metrics (default) | evals
  metric            metrics key; fnmatch globs expand to every matching key (each judged separately); "sps" is
                    derived per update as env_steps_update / t_total_s
  seedset, mode     evals: the eval rows judged (mode compared with the row's "mode"; omit for rows without one)
  agg, window       median | mean | last | max | min over the last `window` points (metrics: a full window is
                    needed; evals: at least `min_points`, default = window)
  op                > | < | >= | <= | nonfinite (metrics: nan/inf, or |x| > `limit` when given)
  value             absolute threshold, or
  ref               fork | control | first5, with `factor` (threshold = ref * factor) or `delta` (ref + delta);
                    first5 = mean of the first `ref_n` (default 5) judged points, which are then not judged;
                    `min_ref` skips the rule when |ref| is below it (a mass that was ~0 cannot collapse)
  ci                evals with a delta: also require the 95% interval of (window - ref) to exclude 0
                    (half-widths from progress_ci95, combined as independent; conservative for paired seeds)
  for_updates       metrics: the condition must hold on this many consecutive judged updates (default 1)
  consecutive_evals evals: ... on this many consecutive eval points (default 1)
  warmup_updates    updates after the fork that are not judged (default: the file's warmup_updates, else 0)
  active_until      judge only updates <= fork + active_until (a pilot rule set); omit = always
  action            kill (default) | alert: an alert rule never kills, even in --mode kill
  tags              labels for --tags (preflight.sh's smoke uses "smoke")

Exit status: 0 nothing fired; 10 an alert fired (or a kill rule in --mode alert); 11 a kill was performed (or would
be, with --dry-run); 2 usage / unreadable rules. --json PATH writes the full per-rule result (preflight / ledger).
"""
from __future__ import annotations

import argparse
import fnmatch
import json
import math
import os
import re
import signal
import socket
import statistics
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_RULES = HERE / "guard-rules-default.json"
EXIT_OK, EXIT_USAGE, EXIT_ALERT, EXIT_KILL = 0, 2, 10, 11
OPS = {">": lambda a, b: a > b, "<": lambda a, b: a < b, ">=": lambda a, b: a >= b, "<=": lambda a, b: a <= b}
TERMINAL_LINE_STATES = {"done", "failed", "killed"}


def now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# ------------------------------------------------------------------------------------------------------- data
def read_jsonl(p: Path) -> list[dict]:
    """Rows of a JSONL file; a torn last line (a writer mid-append) and bad lines are skipped."""
    out: list[dict] = []
    try:
        with open(p, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    r = json.loads(line)
                except ValueError:
                    continue
                if isinstance(r, dict):
                    out.append(r)
    except OSError:
        pass
    return out


def read_json(p: Path):
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def metric_rows(run: Path, until: int | None = None) -> list[dict]:
    """metrics.jsonl rows with an integer update, deduplicated by update (last wins), in update order."""
    by: dict[int, dict] = {}
    for r in read_jsonl(run / "metrics.jsonl"):
        u = r.get("update")
        if isinstance(u, int) and (until is None or u <= until):
            by[u] = r
    return [by[u] for u in sorted(by)]


def eval_rows(run: Path, until: int | None = None) -> list[dict]:
    return [r for r in read_jsonl(run / "evals.jsonl")
            if isinstance(r.get("update"), int) and (until is None or r["update"] <= until)]


def num(x):
    if isinstance(x, bool) or not isinstance(x, (int, float)):
        return None
    return float(x)


def value_of(row: dict, key: str):
    if key == "sps":
        eu, t = num(row.get("env_steps_update")), num(row.get("t_total_s"))
        return eu / t if eu is not None and t is not None and t > 0 else None
    return num(row.get(key))


def finite(x) -> bool:
    return x is not None and math.isfinite(x)


def aggregate(vals: list[float], agg: str):
    vals = [v for v in vals if finite(v)]
    if not vals:
        return None
    if agg == "median":
        return float(statistics.median(vals))
    if agg == "mean":
        return float(sum(vals) / len(vals))
    if agg == "last":
        return vals[-1]
    if agg == "max":
        return max(vals)
    if agg == "min":
        return min(vals)
    raise ValueError(f"unknown agg {agg!r}")


def fork_update_of(run: Path, rows: list[dict]):
    cfg = read_json(run / "config.json") or {}
    sess = cfg.get("sessions")
    if isinstance(sess, list) and sess:
        m = re.search(r"ckpt-(\d+)\.pt$", str((sess[0] or {}).get("resumed_from") or ""))
        if m:
            return int(m.group(1))
    return rows[0]["update"] - 1 if rows else None


def eval_points(rows: list[dict], seedset, mode) -> list[tuple[int, float, float | None]]:
    """(update, progress_mean, ci half-width) of the matching eval rows, one per update (last wins)."""
    by: dict[int, tuple] = {}
    for r in rows:
        if seedset is not None and r.get("seedset") != seedset:
            continue
        if r.get("mode") != mode:
            continue
        pm = num(r.get("progress_mean"))
        if pm is None:
            continue
        ci = r.get("progress_ci95")
        hw = (float(ci[1]) - float(ci[0])) / 2 if isinstance(ci, list) and len(ci) == 2 and None not in ci else None
        by[r["update"]] = (r["update"], pm, hw)
    return [by[u] for u in sorted(by)]


# ------------------------------------------------------------------------------------------------- context
class Context:
    def __init__(self, run: Path, spec: dict, args):
        self.run = run
        self.until = args.until_update
        self.rows = metric_rows(run, self.until)
        self.evals = eval_rows(run, self.until)
        fork = dict(spec.get("fork") or {})
        if args.fork_run:
            fork["run"] = args.fork_run
        if args.fork_update is not None:
            fork["update"] = args.fork_update
        if args.fork_eval is not None:
            fork["eval"] = args.fork_eval
        self.fork_run = Path(fork["run"]) if fork.get("run") else None
        self.fork_u = fork.get("update")
        if self.fork_u is None:
            self.fork_u = fork_update_of(run, self.rows)
        self.fork_eval = fork.get("eval")
        ctl = args.control or spec.get("control")
        self.control = Path(ctl) if ctl else None
        self.warmup = int(spec.get("warmup_updates") or 0)
        self._cache: dict = {}

    def control_rows(self):
        if "crows" not in self._cache:
            self._cache["crows"] = metric_rows(self.control) if self.control else []
        return self._cache["crows"]

    def control_evals(self):
        if "cevals" not in self._cache:
            self._cache["cevals"] = eval_rows(self.control) if self.control else []
        return self._cache["cevals"]

    def fork_rows(self):
        if "frows" not in self._cache:
            self._cache["frows"] = metric_rows(self.fork_run, self.fork_u) if self.fork_run else []
        return self._cache["frows"]

    def fork_eval_point(self, seedset, mode):
        """(progress_mean, half-width) of the fork-point eval: --fork-eval, else the fork run's (or this run's own)
        matching eval at the fork update (the latest at or before it)."""
        if self.fork_eval is not None:
            return float(self.fork_eval), None
        for src in ([self.fork_run] if self.fork_run else []) + [self.run]:
            pts = [p for p in eval_points(eval_rows(src), seedset, mode)
                   if self.fork_u is not None and p[0] <= self.fork_u]
            if pts:
                return pts[-1][1], pts[-1][2]
        return None, None


# --------------------------------------------------------------------------------------------------- rules
def _result(rule, key, status, **kw):
    return dict({"rule": rule.get("name") or rule.get("metric"), "key": key, "status": status,
                 "action": rule.get("action", "kill")}, **kw)


def _threshold(rule, refv):
    if rule.get("factor") is not None:
        return refv * float(rule["factor"])
    if rule.get("delta") is not None:
        return refv + float(rule["delta"])
    return refv


def judge_metric(rule: dict, ctx: Context) -> list[dict]:
    pat = rule["metric"]
    keys = sorted({k for r in ctx.rows for k in r if fnmatch.fnmatchcase(k, pat)}) if any(
        c in pat for c in "*?[") else [pat]
    if not keys:
        return [_result(rule, pat, "skipped", reason="no matching metrics key")]
    if ctx.fork_u is None:
        return [_result(rule, pat, "skipped", reason="no fork update (no metrics yet)")]
    warm = int(rule.get("warmup_updates", ctx.warmup))
    until = rule.get("active_until")
    window = int(rule.get("window", 1))
    agg = rule.get("agg", "last")
    need = int(rule.get("for_updates", 1))
    op = rule["op"]
    out = []
    for key in keys:
        pts = [(r["update"], value_of(r, key)) for r in ctx.rows
               if r["update"] - ctx.fork_u > warm and (until is None or r["update"] - ctx.fork_u <= int(until))]
        pts = [(u, v) for u, v in pts if v is not None]
        if op == "nonfinite":
            lim = rule.get("limit")
            hit = next(((u, v) for u, v in pts if not math.isfinite(v) or (lim is not None and abs(v) > float(lim))),
                       None)
            out.append(_result(rule, key, "triggered", update=hit[0], since_fork=hit[0] - ctx.fork_u,
                               value=hit[1] if math.isfinite(hit[1]) else str(hit[1]), threshold=lim,
                               reason=f"{key}={hit[1]:.4g} non-finite or |x|>{lim}") if hit else
                       _result(rule, key, "ok", judged=len(pts)))
            continue
        refv, ref = None, rule.get("ref")
        if ref is None:
            thr = float(rule["value"])
        else:
            if ref == "first5":
                n = int(rule.get("ref_n", 5))
                if len(pts) < n:
                    out.append(_result(rule, key, "pending", reason=f"{len(pts)}/{n} reference points"))
                    continue
                refv = aggregate([v for _, v in pts[:n]], "mean")
                pts = pts[n:]
            elif ref == "control":
                if ctx.control is None:
                    out.append(_result(rule, key, "skipped", reason="no control run"))
                    continue
                crow = ctx.control_rows()
                cv = [value_of(r, key) for r in crow if r["update"] - ctx.fork_u > warm] or \
                     [value_of(r, key) for r in crow]
                refv = aggregate([v for v in cv if v is not None], rule.get("ref_agg", "median"))
            elif ref == "fork":
                if ctx.fork_run is None:
                    out.append(_result(rule, key, "skipped", reason="no fork run"))
                    continue
                fv = [value_of(r, key) for r in ctx.fork_rows()][-max(window, 10):]
                refv = aggregate([v for v in fv if v is not None], rule.get("ref_agg", "median"))
            else:
                raise ValueError(f"rule {rule.get('name')}: unknown ref {ref!r}")
            if refv is None:
                out.append(_result(rule, key, "skipped", reason=f"no {ref} reference value"))
                continue
            if rule.get("min_ref") is not None and abs(refv) < float(rule["min_ref"]):
                out.append(_result(rule, key, "skipped", ref_value=refv,
                                   reason=f"{ref} reference {refv:.3g} below min_ref {rule['min_ref']}"))
                continue
            thr = _threshold(rule, refv)
        streak, hit, last = 0, None, None
        for i in range(len(pts)):
            if i + 1 < window:
                continue
            a = aggregate([v for _, v in pts[i - window + 1:i + 1]], agg)
            if a is None:
                streak = 0
                continue
            last = a
            streak = streak + 1 if OPS[op](a, thr) else 0
            if streak >= need:
                hit = (pts[i][0], a)
                break
        if hit:
            out.append(_result(rule, key, "triggered", update=hit[0], since_fork=hit[0] - ctx.fork_u, value=hit[1],
                               threshold=thr, ref_value=refv,
                               reason=f"{agg}({key}, {window}) = {hit[1]:.4g} {op} {thr:.4g}"
                                      + (f" ({ref} {refv:.4g})" if ref else "")
                                      + (f" for {need} updates" if need > 1 else "")))
        else:
            out.append(_result(rule, key, "ok" if len(pts) >= window else "pending", judged=max(0, len(pts) - window + 1),
                               last=last, threshold=thr, ref_value=refv))
    return out


def judge_eval(rule: dict, ctx: Context) -> list[dict]:
    seedset, mode = rule.get("seedset"), rule.get("mode")
    key = f"eval:{seedset}:{mode}"
    if ctx.fork_u is None:
        return [_result(rule, key, "skipped", reason="no fork update")]
    warm = int(rule.get("warmup_updates", ctx.warmup))
    until = rule.get("active_until")
    pts = [p for p in eval_points(ctx.evals, seedset, mode)
           if p[0] - ctx.fork_u > warm and (until is None or p[0] - ctx.fork_u <= int(until))]
    window = int(rule.get("window", 1))
    minp = int(rule.get("min_points", window))
    agg = rule.get("agg", "last")
    need = int(rule.get("consecutive_evals", 1))
    op, ref = rule["op"], rule.get("ref")
    if op not in OPS:
        raise ValueError(f"rule {rule.get('name')}: op {op!r} is not valid for evals")
    refv, refhw, cpts = None, None, None
    if ref == "fork":
        refv, refhw = ctx.fork_eval_point(seedset, mode)
        if refv is None:
            return [_result(rule, key, "skipped", reason="no fork-point eval (--fork-run/--fork-eval)")]
    elif ref == "control":
        if ctx.control is None:
            return [_result(rule, key, "skipped", reason="no control run")]
        cpts = eval_points(ctx.control_evals(), seedset, mode)
    elif ref == "first5":
        n = int(rule.get("ref_n", 5))
        if len(pts) < n:
            return [_result(rule, key, "pending", reason=f"{len(pts)}/{n} reference evals")]
        refv = aggregate([p[1] for p in pts[:n]], "mean")
        pts = pts[n:]
    elif ref is not None:
        raise ValueError(f"rule {rule.get('name')}: unknown ref {ref!r}")
    streak, hit, last = 0, None, None
    for i in range(len(pts)):
        win = pts[max(0, i - window + 1):i + 1]
        if len(win) < minp:
            continue
        a = aggregate([p[1] for p in win], agg)
        rv, rhw = refv, refhw
        if cpts is not None:   # control at the same update (the latest at or before it)
            c = [p for p in cpts if p[0] <= pts[i][0]]
            if not c:
                continue
            rv, rhw = c[-1][1], c[-1][2]
        thr = float(rule["value"]) if ref is None else _threshold(rule, rv)
        cond = OPS[op](a, thr)
        hw = None
        if cond and rule.get("ci") and ref is not None:
            hws = [p[2] for p in win]
            if None not in hws and rhw is not None:
                hw = math.sqrt(sum(h * h for h in hws) / len(hws) ** 2 + rhw * rhw)
            elif None not in hws:
                hw = math.sqrt(sum(h * h for h in hws) / len(hws) ** 2)
            if hw is not None:
                d = a - rv
                cond = (d + hw < 0) if op in ("<", "<=") else (d - hw > 0)
        last = {"update": pts[i][0], "value": a, "threshold": thr}
        streak = streak + 1 if cond else 0
        if streak >= need:
            hit = (pts[i][0], a, thr, rv, hw)
            break
    if hit:
        u, a, thr, rv, hw = hit
        reason = (f"{agg}(dev {seedset}/{mode}, last {window}) = {a:.4f} {op} {thr:.4f}"
                  + (f" ({ref} {rv:.4f}{f', ci half-width {hw:.4f}' if hw is not None else ''})" if ref else "")
                  + (f" on {need} consecutive evals" if need > 1 else ""))
        return [_result(rule, key, "triggered", update=u, since_fork=u - ctx.fork_u, value=a, threshold=thr,
                        ref_value=rv, ci_halfwidth=hw, reason=reason)]
    return [_result(rule, key, "ok" if pts else "pending", evals=len(pts), last=last, ref_value=refv)]


def judge(spec: dict, ctx: Context, tags: set[str] | None = None) -> list[dict]:
    res = []
    for rule in spec.get("rules") or []:
        if tags and not tags & set(rule.get("tags") or []):
            continue
        if rule.get("disabled"):
            continue
        res += (judge_eval if rule.get("source") == "evals" else judge_metric)(rule, ctx)
    return res


# ------------------------------------------------------------------------------------------------- actions
def ps_cmdline(pid: int):
    try:
        out = subprocess.run(["ps", "-p", str(int(pid)), "-o", "command="], capture_output=True, text=True,
                             timeout=10)
    except (OSError, subprocess.SubprocessError, ValueError):
        return None
    s = out.stdout.strip()
    return s or None


def mentions(cmd: str, run: Path) -> bool:
    toks = cmd.split()
    names = {str(run), str(run.resolve()), str(run).rstrip("/")}
    return any(t.rstrip("/") in names for t in toks)


def trainer_target(run: Path, args):
    st = read_json(run / "status.json") or {}
    pid = st.get("pid")
    if not isinstance(pid, int):
        return None, "no pid in status.json"
    host = st.get("host")
    if host and host != socket.gethostname() and not args.ignore_host:
        return None, f"status.json host {host!r} is not this host ({socket.gethostname()!r})"
    cmd = ps_cmdline(pid)
    if not cmd:
        return None, f"trainer pid {pid} is not alive"
    if not mentions(cmd, run):
        return None, f"pid {pid} does not mention {run} ({cmd[:120]!r})"
    return pid, cmd


def driver_targets(run: Path, args):
    """[(pid, pgid, cmd)] of the line driver(s) of RUN_DIR."""
    out = []
    me = os.getpid()
    if args.driver_pid:
        cmd = ps_cmdline(args.driver_pid)
        if cmd:
            try:
                out.append((args.driver_pid, os.getpgid(args.driver_pid), cmd))
            except OSError:
                pass
        return out
    try:
        ps = subprocess.run(["ps", "-axo", "pid=,pgid=,command="], capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return out
    for line in ps.stdout.splitlines():
        parts = line.split(None, 2)
        if len(parts) < 3 or not parts[0].isdigit() or not parts[1].isdigit():
            continue
        pid, pgid, cmd = int(parts[0]), int(parts[1]), parts[2]
        if pid == me or args.driver_match not in cmd or "line_guard" in cmd:
            continue
        if mentions(cmd, run):
            out.append((pid, pgid, cmd))
    return out


def write_json_atomic(p: Path, obj) -> None:
    tmp = p.with_name(p.name + f".tmp{os.getpid()}")
    tmp.write_text(json.dumps(obj, indent=1) + "\n", encoding="utf-8")
    os.replace(tmp, p)


def merge_line_status(state_dir: Path, fields: dict, dry: bool, say) -> None:
    p = state_dir / "line_status.json"
    cur = read_json(p)
    cur = cur if isinstance(cur, dict) else {}
    cur.update(fields)
    if dry:
        say(f"DRY-RUN would merge into {p}: {json.dumps(fields)}")
    else:
        write_json_atomic(p, cur)


def reason_line(run: Path, r: dict) -> str:
    return (f"{now()} GUARD {run.name} u{r['update']} (+{r['since_fork']}) rule={r['rule']} key={r['key']}: "
            f"{r['reason']}")


def do_alert(run: Path, state_dir: Path, r: dict, dry: bool, say) -> None:
    line = reason_line(run, r)
    if dry:
        say(f"DRY-RUN would append to {state_dir / 'ALERT'}")
    else:
        with open(state_dir / "ALERT", "a", encoding="utf-8") as f:
            f.write(line + "\n")
    merge_line_status(state_dir, {"reason": line, "alert_rule": r["rule"], "alert_key": r["key"],
                                  "alert_update": r["update"], "alert_at": now()}, dry, say)


def do_kill(run: Path, state_dir: Path, r: dict, args, say) -> dict:
    dry = args.dry_run
    line = reason_line(run, r)
    acts: dict = {"stop_file": str(run / "STOP")}
    if dry:
        say(f"DRY-RUN would write {run / 'STOP'}")
    else:
        (run / "STOP").write_text(line + "\n", encoding="utf-8")
    pid, info = trainer_target(run, args)
    acts["trainer"] = {"pid": pid, "info": info}
    if pid:
        if dry:
            say(f"DRY-RUN would SIGTERM trainer pid {pid} ({info[:100]})")
        else:
            try:
                os.kill(pid, signal.SIGTERM)
                say(f"SIGTERM trainer pid {pid}")
            except OSError as e:
                acts["trainer"]["error"] = str(e)
    else:
        say(f"trainer: {info}")
    drv = driver_targets(run, args)
    acts["drivers"] = []
    mypg = os.getpgrp()
    for dpid, pgid, cmd in drv:
        tgt = ("pgid", pgid) if pgid not in (0, 1, mypg) else ("pid", dpid)
        acts["drivers"].append({"pid": dpid, "pgid": pgid, "signal": tgt[0], "cmd": cmd[:200]})
        if dry:
            say(f"DRY-RUN would SIGTERM driver {tgt[0]} {tgt[1]} ({cmd[:100]})")
            continue
        try:
            (os.killpg if tgt[0] == "pgid" else os.kill)(tgt[1], signal.SIGTERM)
            say(f"SIGTERM driver {tgt[0]} {tgt[1]}")
        except OSError as e:
            acts["drivers"][-1]["error"] = str(e)
    if not drv:
        say(f"driver: no process matching {args.driver_match!r} and {run}")
    if pid and not dry:
        t0 = time.time()
        while time.time() - t0 < args.kill_wait and ps_cmdline(pid):
            time.sleep(2)
        acts["trainer"]["exited"] = ps_cmdline(pid) is None
        say(f"trainer pid {pid} {'exited' if acts['trainer']['exited'] else 'still running (checkpointing?)'}")
    merge_line_status(state_dir, {"state": "killed", "reason": line, "rule": r["rule"], "key": r["key"],
                                  "update": r["update"], "killed_at": now(), "killed_by": "line_guard"}, dry, say)
    return acts


# ---------------------------------------------------------------------------------------------------- main
def load_spec(p: Path) -> dict:
    spec = read_json(p)
    if not isinstance(spec, dict) or not isinstance(spec.get("rules"), list):
        raise ValueError(f"{p}: not a rules file (needs a 'rules' list)")
    for r in spec["rules"]:
        if r.get("source") != "evals" and "metric" not in r:
            raise ValueError(f"{p}: rule without metric: {r}")
        if r.get("op") not in set(OPS) | {"nonfinite"}:
            raise ValueError(f"{p}: rule {r.get('name')}: bad op {r.get('op')!r}")
        if r.get("op") != "nonfinite" and r.get("value") is None and r.get("ref") is None:
            raise ValueError(f"{p}: rule {r.get('name')}: needs value or ref")
    return spec


def parse(argv=None):
    ap = argparse.ArgumentParser(prog="line_guard.py", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run", metavar="RUN_DIR")
    ap.add_argument("--rules", default=str(DEFAULT_RULES), help="rules JSON (default %(default)s)")
    ap.add_argument("--mode", choices=["alert", "kill"], default="alert", help="(default alert)")
    ap.add_argument("--once", action="store_true", help="judge the files as they are now and exit (replay)")
    ap.add_argument("--dry-run", action="store_true", help="print the actions instead of performing them")
    ap.add_argument("--tags", default=None, help="only rules carrying one of these comma-separated tags")
    ap.add_argument("--fork-run", default=None, help="the fork's source run dir (fork-point eval / metrics)")
    ap.add_argument("--fork-update", type=int, default=None, help="fork update (default: config.json / metrics)")
    ap.add_argument("--fork-eval", type=float, default=None, help="fork-point dev progress (overrides --fork-run)")
    ap.add_argument("--control", default=None, help="control run dir (ref: control)")
    ap.add_argument("--until-update", type=int, default=None, help="ignore rows after this update (replay)")
    ap.add_argument("--state-dir", default=None,
                    help="where ALERT / line_status.json / guard.state.json go (default RUN_DIR); STOP is always "
                         "RUN_DIR/STOP")
    ap.add_argument("--json", default=None, help="write the per-rule result here")
    ap.add_argument("--interval", type=float, default=60.0, help="follow mode poll seconds (default 60)")
    ap.add_argument("--max-polls", type=int, default=0, help="follow mode: stop after N polls (0 = no limit)")
    ap.add_argument("--driver-pid", type=int, default=None, help="the driver pid (default: found by --driver-match)")
    ap.add_argument("--driver-match", default="w3m_line", help="driver command substring (default %(default)s)")
    ap.add_argument("--kill-wait", type=float, default=120.0, help="seconds to wait for the trainer (default 120)")
    ap.add_argument("--ignore-host", action="store_true", help="signal the status.json pid even if host differs")
    ap.add_argument("--quiet", action="store_true", help="only reason lines and the summary")
    return ap.parse_args(argv)


def summary_line(run: Path, res: list[dict], mode: str, killed) -> str:
    trig = sorted([r for r in res if r["status"] == "triggered"], key=lambda r: r["update"])
    if not trig:
        n = sum(r["status"] == "ok" for r in res)
        return f"GUARD {run.name}: nothing fired ({n} rule keys ok, {len(res) - n} pending/skipped)"
    f = trig[0]
    return (f"GUARD {run.name}: {len(trig)} fired; first u{f['update']} (+{f['since_fork']}) {f['rule']} "
            f"[{f['key']}]; mode {mode}{'; KILLED' if killed else ''}")


def main(argv=None) -> int:
    args = parse(argv)
    run = Path(args.run)
    say = (lambda s: None) if args.quiet else (lambda s: print(s, flush=True))
    if not run.is_dir():
        print(f"line_guard: {run} is not a directory", file=sys.stderr)
        return EXIT_USAGE
    try:
        spec = load_spec(Path(args.rules))
    except ValueError as e:
        print(f"line_guard: {e}", file=sys.stderr)
        return EXIT_USAGE
    tags = set(args.tags.split(",")) if args.tags else None
    state_dir = Path(args.state_dir) if args.state_dir else run
    if not args.dry_run:
        state_dir.mkdir(parents=True, exist_ok=True)
    sp = state_dir / "guard.state.json"
    gst = (read_json(sp) if not args.dry_run else None) or {"fired": [], "killed": None}
    rc, polls, sig = EXIT_OK, 0, None
    while True:
        polls += 1
        cur = (os.stat(run / "metrics.jsonl").st_mtime if (run / "metrics.jsonl").exists() else 0,
               os.stat(run / "evals.jsonl").st_mtime if (run / "evals.jsonl").exists() else 0)
        if cur != sig or args.once:
            sig = cur
            ctx = Context(run, spec, args)
            try:
                res = judge(spec, ctx, tags)
            except (ValueError, KeyError) as e:
                print(f"line_guard: bad rule: {e}", file=sys.stderr)
                return EXIT_USAGE
            trig = sorted([r for r in res if r["status"] == "triggered"], key=lambda r: (r["update"], r["rule"]))
            for r in trig:
                rid = f"{r['rule']}|{r['key']}"
                if rid in gst["fired"]:
                    continue
                gst["fired"].append(rid)
                kill = args.mode == "kill" and r["action"] == "kill" and not gst.get("killed")
                print(("KILL " if kill else "ALERT ") + reason_line(run, r), flush=True)
                if kill:
                    gst["killed"] = {"rule": r["rule"], "key": r["key"], "update": r["update"],
                                     "actions": do_kill(run, state_dir, r, args, say)}
                    rc = EXIT_KILL
                else:
                    do_alert(run, state_dir, r, args.dry_run, say)
                    rc = max(rc, EXIT_ALERT)
            if not args.dry_run:
                write_json_atomic(sp, gst)
            out = {"run": str(run), "mode": args.mode, "dry_run": args.dry_run, "fork_update": ctx.fork_u,
                   "control": str(ctx.control) if ctx.control else None,
                   "fork_run": str(ctx.fork_run) if ctx.fork_run else None,
                   "last_update": ctx.rows[-1]["update"] if ctx.rows else None,
                   "fired": bool(trig), "first": trig[0] if trig else None, "killed": gst.get("killed"),
                   "results": res, "at": now()}
            if args.json:
                Path(args.json).write_text(json.dumps(out, indent=1) + "\n", encoding="utf-8")
            if args.once or gst.get("killed"):
                print(summary_line(run, res, args.mode, gst.get("killed")), flush=True)
                break
        ls = read_json(state_dir / "line_status.json") or {}
        if isinstance(ls, dict) and ls.get("state") in TERMINAL_LINE_STATES:
            say(f"line_status {ls.get('state')}: guard exits")
            break
        if args.max_polls and polls >= args.max_polls:
            break
        time.sleep(args.interval)
    return rc


if __name__ == "__main__":
    sys.exit(main())
