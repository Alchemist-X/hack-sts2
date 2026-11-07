#!/usr/bin/env python3
"""dash_data.py (v2) — build the dashboard's dataset files from the hand-kept state, the event log, the experiment
ledger and the run mirror.

Usage:
  python3 -I dash_data.py STATE_JSON EVENTS_JSONL OUT_DIR [BALANCE]
         [--lines LINES_JSON] [--line-events LINE_EVENTS_JSONL] [--system SYSTEM_JSON]
         [--mirror MIRROR_DIR] [--endpoints DIR[,DIR...]] [--results RESULTS_JSON]
         [--balance-log BALANCE_JSONL] [--units-rate UNITS_PER_H] [--max-metrics-bytes N]

Writes OUT_DIR/{status,plan,timeline,metrics,evals,lines,line_events,throughput,system}.json: each a JSON array of flat
row objects (floats rounded to 4 decimals). The field contract is the "dashboard v2" dataset contract
(scripts/dashboard/README.md, "Refresh (v2)").

Inputs (every one optional except the three positionals; missing files and malformed lines are skipped):
  STATE_JSON      hand-kept: stage, stage_label, takeaway, plan rows, units_cap, balance_at_batch_start, optional
                  topups ([{time, units}] or a number), runs = [{name (line id), dir (mirror dir)}]. A relative run
                  "dir" is resolved against $STS2_OPS_DIR, default the directory of STATE_JSON.
  EVENTS_JSONL    timeline events {"time": "YYYY-MM-DD HH:MM" (EST), "kind": ok|info|bad, "event", optional "lines"}.
  --lines         the experiment ledger (array of line rows, or {"lines": [...]}). Text fields pass through; the
                  numeric fields the mirror can answer are recomputed every run (updates_done, gpu_hours, units_est,
                  endpoints, best_*; started/ended when empty).
  --line-events   per-line journey rows {line, time, kind, event, update, value} (jsonl or a JSON array).
  --system        architecture facts: an array of {key, value, note} rows, or an object {key: value | {value, note}}.
  --mirror        root holding <run dir>/{metrics.jsonl, evals.jsonl, status.json, config.json, train.log}; default the
                  parent of the first state run dir. Ledger lines whose run dir is not in the state are found here.
  --endpoints     roots of pulled segment-end eval reports, layout <root>/<run>/<name>/{report.json|repeats.json} or
                  <root>/<run>/endpoint/<name>/...; <mirror>/<run>/evals/endpoint/<name>/ is always searched too.
  --results       docs/results/evals-silent-a10-*.json: lowest-priority fallback for segment-end readouts.
  --balance-log   jsonl {time (ISO), balance, rate}: balance history; with the "余额 X" readings in EVENTS_JSONL it is
                  used to detect top-ups (positive jumps) for units_used and, without --units-rate, the burn rate.
  --units-rate    current burn rate (compute units per hour, e.g. the "Usage rate" line of `colab usage`).
"""
import datetime as dt
import glob
import json
import math
import os
import re
import sys

UNITS_PER_GPU_HOUR = 6.73          # one Colab A100 runtime
GAP_S = 20 * 60                    # a gap between metrics rows longer than this is downtime, not GPU time
EST = dt.timezone(dt.timedelta(hours=-5))
SEEDSET_SHORT = (("-dev-", "dev-128"), ("-ab-", "ab-512"), ("-blind-", "blind-256"), ("-test-", "test-100"))
MODES = ("greedy_off", "greedy_on", "sample_off", "sample_on")


# ----------------------------------------------------------------------------------------------------------- helpers
def parse_args(argv):
    pos, opt = [], {}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a.startswith("--"):
            k, eq, v = a[2:].partition("=")
            if not eq:
                i += 1
                v = argv[i] if i < len(argv) else ""
            opt[k.replace("-", "_")] = v
        else:
            pos.append(a)
        i += 1
    if len(pos) < 3:
        sys.exit(__doc__)
    return pos, opt


def read_json(path, default=None, quiet=False):
    if not path or not os.path.isfile(path):
        return default
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return json.load(f)
    except Exception as e:
        if not quiet:
            print(f"warning: {path}: {e}", file=sys.stderr)
        return default


def read_jsonl(path):
    rows = []
    if path and os.path.isfile(path):
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    r = json.loads(line)
                except Exception:
                    continue
                if isinstance(r, dict):
                    rows.append(r)
    return rows


def read_rows(path):
    """A JSON array file, an object with one list value, or jsonl."""
    if not path or not os.path.isfile(path):
        return []
    d = None if path.endswith(".jsonl") else read_json(path, quiet=True)
    if isinstance(d, list):
        return [r for r in d if isinstance(r, dict)]
    if isinstance(d, dict):
        for v in d.values():
            if isinstance(v, list):
                return [r for r in v if isinstance(r, dict)]
        return []
    return read_jsonl(path)


def num(v, nd=4):
    """A finite number (bools and non-numbers -> None); floats rounded, ints kept."""
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, int):
        return v
    if isinstance(v, float):
        return round(v, nd) if math.isfinite(v) else None
    if isinstance(v, str):
        try:
            f = float(v)
        except ValueError:
            return None
        return round(f, nd) if math.isfinite(f) else None
    return None


def intv(v):
    n = num(v)
    return int(n) if n is not None else None


def g(r, *keys, default=None):
    for k in keys:
        if isinstance(r, dict) and k in r and r[k] is not None:
            return r[k]
    return default


def parse_time(s):
    """ISO (with offset or Z) -> aware UTC; 'YYYY-MM-DD HH:MM' and 'MM-DD HH:MM' are read as EST."""
    if not s or not isinstance(s, str):
        return None
    s = s.strip().replace(" EST", "")
    try:
        if "T" in s:
            t = dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
            return (t if t.tzinfo else t.replace(tzinfo=dt.timezone.utc)).astimezone(dt.timezone.utc)
        if re.match(r"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}", s):
            return dt.datetime.strptime(s[:16], "%Y-%m-%d %H:%M").replace(tzinfo=EST).astimezone(dt.timezone.utc)
        if re.match(r"^\d{2}-\d{2} \d{2}:\d{2}$", s):
            return dt.datetime.strptime(f"2026-{s}", "%Y-%m-%d %H:%M").replace(tzinfo=EST).astimezone(dt.timezone.utc)
    except ValueError:
        return None
    return None


def est(t, fmt="%Y-%m-%d %H:%M"):
    return t.astimezone(EST).strftime(fmt) if t else ""


def act_count(ar, act=3):
    """Games/episodes that reached `act` or later: act_reached as {"1": n, "2": n, "3": n} or a list of counts
    (index 0 = act 1)."""
    if isinstance(ar, dict):
        tot, seen = 0, False
        for k, v in ar.items():
            try:
                a = int(k)
            except (TypeError, ValueError):
                continue
            n = num(v)
            if n is None:
                continue
            seen = True
            if a >= act:
                tot += n
        return int(tot) if seen else None
    if isinstance(ar, list) and ar and all(num(x) is not None for x in ar):
        return int(sum(num(x) for x in ar[act - 1:]))
    return None


def seedset_short(sid):
    s = (sid or "").lower()
    for key, short in SEEDSET_SHORT:
        if key in s:
            return short
    for key, short in (("dev", "dev-128"), ("ab", "ab-512"), ("blind", "blind-256"), ("test", "test-100")):
        if re.search(rf"(^|[^a-z]){key}([^a-z]|$)", s):
            return short
    return "other"


def mode_of(e):
    m = g(e, "mode")
    if isinstance(m, dict):
        m = m.get("name")
    if isinstance(m, str) and m:
        m = m.replace("-", "_").lower()
        if m in MODES:
            return m
    pol = (g(e, "policy") or "greedy")
    pol = "sample" if "sample" in str(pol) else "greedy"
    br = g(e, "breaker", "loop_breaker")
    if isinstance(br, dict):
        br = br.get("mode")
    br = "off" if str(br).lower() in ("off", "false", "0") else "on"
    return f"{pol}_{br}"


def as_list(v):
    if v is None or v == "":
        return []
    if isinstance(v, list):
        return v
    if isinstance(v, str):
        try:
            d = json.loads(v)
            return d if isinstance(d, list) else []
        except Exception:
            return []
    return []


def csv_ids(v):
    if isinstance(v, list):
        return ",".join(str(x) for x in v if x not in (None, ""))
    return str(v or "")


def write(out, name, rows):
    path = os.path.join(out, name + ".json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, separators=(",", ":"))
    return path


# ---------------------------------------------------------------------------------------------------------- inputs
pos, opt = parse_args(sys.argv[1:])
STATE_PATH, EVENTS_PATH, OUT = pos[0], pos[1], pos[2]
BALANCE = num(pos[3]) if len(pos) > 3 and pos[3] not in ("", "-") else None
os.makedirs(OUT, exist_ok=True)
NOW = dt.datetime.now(dt.timezone.utc)

state = read_json(STATE_PATH, {}) or {}
events = read_rows(EVENTS_PATH)
ledger = read_rows(opt.get("lines"))
line_events_in = read_rows(opt.get("line_events"))
system_in = read_json(opt.get("system"))
MAX_METRICS_BYTES = int(opt.get("max_metrics_bytes") or 3_000_000)

BASE = os.environ.get("STS2_OPS_DIR") or os.path.dirname(os.path.abspath(STATE_PATH))
# Defaults when the flags are absent (so the bare positional call matches dash_refresh.sh): the ops dir's
# endpoints/ cache and balance_log.jsonl, and the repo's newest docs/results/evals-silent-a10-*.json.
_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
if "endpoints" not in opt and os.path.isdir(os.path.join(BASE, "endpoints")):
    opt["endpoints"] = os.path.join(BASE, "endpoints")
if "balance_log" not in opt and os.path.isfile(os.path.join(BASE, "balance_log.jsonl")):
    opt["balance_log"] = os.path.join(BASE, "balance_log.jsonl")
if "results" not in opt:
    _res = sorted(glob.glob(os.path.join(_REPO, "docs", "results", "evals-silent-a10-*.json")))
    opt["results"] = _res[-1] if _res else ""
results_doc = read_json(opt.get("results"), {}) or {}
balance_log = read_jsonl(opt.get("balance_log"))
state_runs = state.get("runs") or (
    [{"name": state.get("run") or "run", "dir": state["mirror_dir"]}] if state.get("mirror_dir") else [])
state_runs = [dict(r, dir=os.path.join(BASE, r["dir"])) for r in state_runs if isinstance(r, dict) and r.get("dir")]
MIRROR = opt.get("mirror") or (os.path.dirname(state_runs[0]["dir"].rstrip("/")) if state_runs else
                               os.path.join(BASE, "mirror"))
ENDPOINT_ROOTS = [p for p in (opt.get("endpoints") or "").split(",") if p]

# --------------------------------------------------------------------------------------- line -> mirror dir sources
# A line's rows come from its dirs: the state runs with that name plus <mirror>/<ledger run>. When several lines share
# a dir (C and C2 in w4-c), each takes the updates after its own updates_from up to the next line's updates_from.
led = {}
order_ids = []
for r in ledger:
    lid = str(r.get("id") or "").strip()
    if lid and lid not in led:
        led[lid] = dict(r)
        order_ids.append(lid)
line_dirs = {}                      # line id -> [abs dir]
for r in state_runs:
    line_dirs.setdefault(r["name"], [])
    if r["dir"] not in line_dirs[r["name"]]:
        line_dirs[r["name"]].append(r["dir"])
for lid, r in led.items():
    run = str(r.get("run") or "").strip()
    if run:
        d = run if os.path.isabs(run) else os.path.join(MIRROR, run)
        line_dirs.setdefault(lid, [])
        if os.path.isdir(d) and d not in line_dirs[lid]:
            line_dirs[lid].append(d)
    else:
        line_dirs.setdefault(lid, [])
all_ids = list(dict.fromkeys(order_ids + list(line_dirs)))

dir_lines = {}                      # dir -> [line ids]
for lid in all_ids:
    for d in line_dirs.get(lid, []):
        dir_lines.setdefault(d, []).append(lid)


def ufrom(lid):
    return intv(led.get(lid, {}).get("updates_from"))


def owner(d, update):
    """Which line owns `update` in dir d."""
    ids = dir_lines.get(d, [])
    if len(ids) <= 1:
        return ids[0] if ids else None
    starts = sorted(((ufrom(i) if ufrom(i) is not None else -1), i) for i in ids)
    best = starts[0][1]
    for s, i in starts:
        if update is not None and update > s:
            best = i
    return best


def run_name(d):
    return os.path.basename(d.rstrip("/"))


# ------------------------------------------------------------------------------------------------------- metrics
raw = {}                            # line -> {update: metrics row}
for d, ids in dir_lines.items():
    for r in read_jsonl(os.path.join(d, "metrics.jsonl")):
        u = intv(r.get("update"))
        if u is None:
            continue
        lid = owner(d, u)
        if lid:
            raw.setdefault(lid, {})[u] = r          # later dirs / later rows win on a repeated update


def metrics_row(lid, r):
    eps = num(g(r, "episodes"))
    ewc = num(g(r, "episodes_with_cycle"))
    a3 = act_count(r.get("act_reached"))
    hl = r.get("harvest_live")
    if isinstance(hl, dict):
        hl = sum(num(v) or 0 for v in hl.values())
    return {
        "run": lid, "update": intv(r.get("update")), "env_steps": intv(r.get("env_steps")), "time": g(r, "time"),
        "progress_fresh": num(g(r, "fresh_progress_mean", "progress_mean")), "progress_all": num(g(r, "progress_mean")),
        "entropy": num(g(r, "entropy")), "approx_kl": num(g(r, "approx_kl")), "sps": num(g(r, "env_steps_per_s"), 1),
        "episodes": intv(eps), "t_env_s": num(g(r, "t_env_s")), "t_infer_s": num(g(r, "t_infer_s")),
        "t_update_s": num(g(r, "t_update_s")), "t_total_s": num(g(r, "t_total_s")),
        "gpu_mem_peak_mb": num(g(r, "gpu_mem_peak_mb"), 1), "harvest_live": num(hl),
        "harvest_start_share": num(g(r, "harvest_start_share")), "save_episodes": intv(g(r, "save_episodes")),
        "save_wins": intv(g(r, "save_wins")), "save_progress_gain_mean": num(g(r, "save_progress_gain_mean")),
        "boss2_kills": intv(g(r, "boss2_kills")), "boss_bonus_sum": num(g(r, "boss_bonus_sum")),
        "explained_variance": num(g(r, "explained_variance")), "clipfrac": num(g(r, "clipfrac")),
        "grad_norm": num(g(r, "grad_norm")), "cycles_total": intv(g(r, "cycles_total")),
        "cycle_games_frac": (num(ewc / eps) if (ewc is not None and eps) else None),
        "act3_frac": (num(a3 / eps) if (a3 is not None and eps) else None),
        "fresh_win_rate": num(g(r, "fresh_win_rate")),
    }


metrics_full = {lid: [metrics_row(lid, rows[u]) for u in sorted(rows)] for lid, rows in raw.items()}


def active_hours(rows):
    """GPU-busy hours from row timestamps: consecutive gaps up to GAP_S count; a longer gap (lost runtime, restart)
    counts only that row's own t_total_s."""
    ts = [(parse_time(m["time"]), m.get("t_total_s")) for m in rows]
    ts = [(t, tt) for t, tt in ts if t]
    if not ts:
        return None, 0, 0.0
    sec = ts[0][1] or 0.0
    gaps, gap_h = 0, 0.0
    for (a, _), (b, tt) in zip(ts, ts[1:]):
        d = (b - a).total_seconds()
        if d < 0:
            continue
        if d <= GAP_S:
            sec += d
        else:
            gaps += 1
            gap_h += d / 3600
            sec += tt or 0.0
    return sec / 3600, gaps, gap_h


def thin(rows, tail, stride):
    n = len(rows)
    return [m for i, m in enumerate(rows) if i >= n - tail or i % stride == 0 or i == n - 1]


# --------------------------------------------------------------------------------------------------------- evals
evals_by_key = {}                   # (line, update, seedset, mode) -> row (priority: higher source wins)
PRIO = {"mirror": 3, "report": 2, "results": 1, "ledger": 0}


def put_eval(row, src):
    if row["run"] is None or row["update"] is None:
        return
    k = (row["run"], row["update"], row["seedset"], row["mode"])
    old = evals_by_key.get(k)
    if old is None or PRIO[src] >= PRIO[old[0]]:
        if old is not None:                         # keep the fields the weaker source had and this one lacks
            for f, v in old[1].items():
                if row.get(f) is None and v is not None:
                    row[f] = v
            row["endpoint"] = bool(row.get("endpoint") or old[1].get("endpoint"))
        evals_by_key[k] = (src, row)
    else:
        for f, v in row.items():
            if old[1].get(f) is None and v is not None:
                old[1][f] = v
        old[1]["endpoint"] = bool(old[1].get("endpoint") or row.get("endpoint"))


def eval_row(lid, e, endpoint=False):
    ci = e.get("progress_ci95") or [None, None]
    if not isinstance(ci, list) or len(ci) < 2:
        ci = [None, None]
    res = e.get("results") if isinstance(e.get("results"), dict) else {}
    games = intv(g(e, "games"))
    wins = intv(g(e, "wins", default=res.get("win")))
    sid = g(e, "seedset_id", "seedset") or ""
    wh = g(e, "weights_hash", "prompt_hash") or ""
    return {
        "run": lid, "update": intv(e.get("update")), "seedset": seedset_short(sid), "seedset_id": sid,
        "mode": mode_of(e), "progress_mean": num(e.get("progress_mean")), "ci_lo": num(ci[0]), "ci_hi": num(ci[1]),
        "games": games, "wins": wins,
        "clear_rate": num(g(e, "win_rate", default=(wins / games if (wins is not None and games) else None))),
        "act3": act_count(e.get("act_reached")),
        "stuck": intv(g(e, "stuck", "stuck_games", default=res.get("stuck"))),
        "interventions": intv(g(e, "loop_breaks", "interventions")), "cycles": intv(g(e, "cycles")),
        "floor_mean": num(g(e, "floor_mean")), "weights_hash": str(wh).replace("weights:", ""),
        "endpoint": bool(endpoint or str(e.get("dir") or "").startswith("evals/endpoint")), "time": g(e, "time"),
    }


for d in dir_lines:
    for e in read_jsonl(os.path.join(d, "evals.jsonl")):
        u = intv(e.get("update"))
        lid = owner(d, u)
        if lid and u is not None:
            put_eval(eval_row(lid, e), "mirror")


def update_from_name(name):
    m = re.match(r"^u0*(\d+)", name)
    return int(m.group(1)) if m else None


def endpoint_dirs(run):
    pats = [os.path.join(MIRROR, run, "evals", "endpoint", "*")]
    for root in ENDPOINT_ROOTS:
        pats += [os.path.join(root, run, "*"), os.path.join(root, run, "endpoint", "*")]
    seen = {}
    for p in pats:
        for d in glob.glob(p):
            if os.path.isdir(d) and update_from_name(os.path.basename(d)) is not None:
                seen.setdefault(os.path.basename(d), d)
    return seen


def report_row(lid, name, d):
    u = update_from_name(name)
    rep = read_json(os.path.join(d, "report.json"))
    if isinstance(rep, dict):
        h = rep.get("headline") if isinstance(rep.get("headline"), dict) else {}
        em = rep.get("eval_mode") if isinstance(rep.get("eval_mode"), dict) else {}
        lb = rep.get("loop_breaker") if isinstance(rep.get("loop_breaker"), dict) else {}
        cy = rep.get("cycles") if isinstance(rep.get("cycles"), dict) else {}
        e = dict(h, update=u, seedset_id=rep.get("seedset_id") or rep.get("seedset"),
                 mode=em.get("name") or "-".join(name.split("-")[-2:]), policy=em.get("policy"),
                 breaker=em.get("breaker"), weights_hash=rep.get("prompt_hash"), stuck=h.get("stuck_games"),
                 loop_breaks=lb.get("loop_breaks"), cycles=cy.get("cycles"),
                 time=rep.get("finished_at") or rep.get("time"))
        return eval_row(lid, e, endpoint=True)
    rp = read_json(os.path.join(d, "repeats.json"))
    if isinstance(rp, dict):
        runs = [x for x in rp.get("runs") or [] if isinstance(x, dict)]
        games = sum(intv(x.get("games")) or 0 for x in runs) or intv(rp.get("plays"))
        wins = sum(intv((x.get("results") or {}).get("win")) or 0 for x in runs)
        per = [num(x.get("progress_mean")) for x in runs if num(x.get("progress_mean")) is not None]
        e = {"update": u, "seedset_id": rp.get("seedset") or "", "policy": rp.get("policy") or "sample",
             "breaker": rp.get("breaker"), "progress_mean": rp.get("progress_mean_pooled",
                                                                  (sum(per) / len(per)) if per else None),
             "progress_ci95": [min(per), max(per)] if per else None, "games": games, "wins": wins,
             "weights_hash": rp.get("weights_hash"), "loop_breaks": sum(intv(x.get("loop_breaks")) or 0 for x in runs),
             "cycles": sum(intv(x.get("cycles")) or 0 for x in runs)}
        row = eval_row(lid, e, endpoint=True)
        if row["seedset"] == "other" and "dev" in name:
            row["seedset"] = "dev-128"
        return row
    return None


for d in dir_lines:
    for name, ed in sorted(endpoint_dirs(run_name(d)).items()):
        u = update_from_name(name)
        lid = owner(d, u)
        row = report_row(lid, name, ed) if lid else None
        if row:
            put_eval(row, "report")

# results-JSON fallback (segment ends recorded when the bucket copy was pulled)
for rr in (results_doc.get("runs") or []) if isinstance(results_doc, dict) else []:
    d = os.path.join(MIRROR, str(rr.get("name") or ""))
    cands = [x for x in dir_lines if run_name(x) == rr.get("name")]
    if not cands:
        continue
    for e in rr.get("endpoint") or []:
        if not isinstance(e, dict):
            continue
        lid = owner(cands[0], intv(e.get("update")))
        e = dict(e, seedset_id={"ab512": "silent-a10-ab-v1"}.get(e.get("seedset"), e.get("seedset")))
        put_eval(eval_row(lid, e, endpoint=True), "results")
    for e in rr.get("sample_x3") or []:
        if not isinstance(e, dict):
            continue
        lid = owner(cands[0], intv(e.get("update")))
        per = [num(x) for x in e.get("per_repeat") or [] if num(x) is not None]
        e2 = {"update": e.get("update"), "seedset_id": e.get("seedset") or "silent-a10-dev-s128", "policy": "sample",
              "breaker": e.get("breaker") or "off", "progress_mean": g(e, "mean", "progress_mean_pooled",
                                                                       default=(sum(per) / len(per) if per else None)),
              "progress_ci95": [min(per), max(per)] if per else None, "games": g(e, "plays", "games"),
              "weights_hash": e.get("weights_hash")}
        row = eval_row(lid, e2, endpoint=True)
        if row["seedset"] == "other":
            row["seedset"] = "dev-128"
        put_eval(row, "results")

# segment ends per line: the ledger's endpoint updates, its updates_to, and every update that has a segment-end report
seg_ends = {lid: set() for lid in all_ids}
for lid, r in led.items():
    for ep in as_list(r.get("endpoints")):
        if isinstance(ep, dict) and intv(ep.get("update")) is not None:
            seg_ends[lid].add(intv(ep.get("update")))
    for k in ("updates_to",):
        if intv(r.get(k)) is not None:
            seg_ends[lid].add(intv(r.get(k)))
    for u in r.get("segment_ends") or []:
        if intv(u) is not None:
            seg_ends[lid].add(intv(u))
for (lid, u, ss, mode), (src, row) in evals_by_key.items():
    if row.get("endpoint") and src != "mirror":
        seg_ends.setdefault(lid, set()).add(u)

# ledger endpoints that no file backs yet become ab-512 / dev-128 rows (source "ledger", weakest)
for lid, r in led.items():
    for ep in as_list(r.get("endpoints")):
        if not isinstance(ep, dict) or intv(ep.get("update")) is None:
            continue
        u = intv(ep.get("update"))
        if num(ep.get("ab512")) is not None and (lid, u, "ab-512", "greedy_off") not in evals_by_key:
            put_eval({"run": lid, "update": u, "seedset": "ab-512", "seedset_id": "silent-a10-ab-v1",
                      "mode": "greedy_off", "progress_mean": num(ep.get("ab512")), "ci_lo": num(ep.get("ab512_lo")),
                      "ci_hi": num(ep.get("ab512_hi")), "games": 512, "wins": None, "clear_rate": None,
                      "act3": intv(ep.get("act3_ab512")), "stuck": None, "interventions": None, "cycles": None,
                      "floor_mean": None, "weights_hash": "", "endpoint": True, "time": None}, "ledger")
        if num(ep.get("dev128")) is not None and (lid, u, "dev-128", "greedy_off") not in evals_by_key:
            put_eval({"run": lid, "update": u, "seedset": "dev-128", "seedset_id": "silent-a10-dev-v1",
                      "mode": "greedy_off", "progress_mean": num(ep.get("dev128")), "ci_lo": None, "ci_hi": None,
                      "games": 128, "wins": None, "clear_rate": None, "act3": None, "stuck": None,
                      "interventions": None, "cycles": None, "floor_mean": None, "weights_hash": "",
                      "endpoint": True, "time": None}, "ledger")

ledger_only = {k for k, (src, _) in evals_by_key.items() if src == "ledger"}
evals = []
for (lid, u, ss, mode), (src, row) in evals_by_key.items():
    if u in seg_ends.get(lid, set()) and ss in ("dev-128", "ab-512"):
        row["endpoint"] = True
    evals.append(row)
evals.sort(key=lambda e: (e["run"], e["update"], e["seedset"], e["mode"]))
# an eval row without a time borrows the time of a same-line same-update row
t_at = {}
for e in evals:
    if e.get("time"):
        t_at.setdefault((e["run"], e["update"]), e["time"])
for e in evals:
    if not e.get("time"):
        e["time"] = t_at.get((e["run"], e["update"]))


def evals_for(lid, seedset, mode="greedy_off"):
    return [e for e in evals if e["run"] == lid and e["seedset"] == seedset and e["mode"] == mode
            and e["progress_mean"] is not None]


def best(rows):
    return max(rows, key=lambda e: (e["progress_mean"], -e["update"])) if rows else None


# --------------------------------------------------------------------------------------------- config / throughput
def load_config(d):
    c = read_json(os.path.join(d, "config.json"), {})
    return c if isinstance(c, dict) else {}


configs = {d: load_config(d) for d in dir_lines}
logs_compile = {}


def log_compile(d):
    if d not in logs_compile:
        mode = None
        p = os.path.join(d, "train.log")
        if os.path.isfile(p):
            comp = eager = False
            with open(p, encoding="utf-8", errors="replace") as f:
                for line in f:
                    if "device:" in line and "torch.compile" in line:
                        comp = True
                    elif "device:" in line and ("eager" in line or "compile off" in line):
                        eager = True
            mode = "mixed" if comp and eager else "compiled" if comp else "eager" if eager else None
        logs_compile[d] = mode
    return logs_compile[d]


def line_sessions(lid):
    out = []
    lo = ufrom(lid)
    for d in line_dirs.get(lid, []):
        sess = configs.get(d, {}).get("sessions") or []
        last = {}
        for s in sess:
            if isinstance(s, dict):
                last[(s.get("session"), s.get("update_start"))] = s     # a retry inside a session: last entry wins
        for s in last.values():
            us = intv(s.get("update_start"))
            if len(dir_lines[d]) > 1 and owner(d, (us or 0) + 1) != lid:
                continue
            if lo is not None and us is not None and us < lo and len(dir_lines[d]) > 1:
                continue
            out.append((d, s))
    return out


def compile_mode(lid):
    ss = line_sessions(lid)
    vals = {("eager" if str(s.get("compile")).lower() in ("off", "none", "false", "eager") else "compiled")
            for _, s in ss if s.get("compile") is not None}
    if vals:
        return vals.pop() if len(vals) == 1 else "mixed"
    for d in line_dirs.get(lid, []):
        c = configs.get(d, {}).get("config") or {}
        if c.get("compile") is not None:
            return "eager" if str(c["compile"]).lower() in ("off", "none", "false") else "compiled"
        m = log_compile(d)
        if m:
            return m
    return "unknown"


def cfg_val(lid, key):
    for d in line_dirs.get(lid, []):
        c = configs.get(d, {})
        v = (c.get("config") or {}).get(key)
        if v is None:
            for s in c.get("sessions") or []:
                v = ((s or {}).get("given") or {}).get(key, v)
        if v is not None:
            return intv(v)
    return None


def hardware(lid):
    devs, cpus = [], []
    for _, s in line_sessions(lid):
        dv = str(s.get("cuda_device") or s.get("device") or "").replace("NVIDIA ", "")
        if dv and dv not in devs:
            devs.append(dv)
        if s.get("cpu_count") and s["cpu_count"] not in cpus:
            cpus.append(s["cpu_count"])
    if not devs:
        return ""
    return " / ".join(devs) + (f" + {'/'.join(str(c) for c in cpus)} vCPU" if cpus else "")


def mean(vals):
    v = [x for x in vals if x is not None]
    return (sum(v) / len(v)) if v else None


throughput = []
line_hours = {}
for lid in all_ids:
    rows = metrics_full.get(lid) or []
    if not rows:
        continue
    h, gaps, gap_h = active_hours(rows)
    line_hours[lid] = h
    n = rows[-1]["update"] - rows[0]["update"] + 1     # updates trained (the mirror may hold fewer rows than that)
    t0, t1 = parse_time(rows[0]["time"]), parse_time(rows[-1]["time"])
    span_h = ((t1 - t0).total_seconds() / 3600) if (t0 and t1) else None
    note = f"活跃 {h:.2f} 小时" if h is not None else ""
    if gaps:
        note += f"；剔除 {gaps} 段超过 20 分钟的中断（共 {gap_h:.2f} 小时，墙钟跨度 {span_h:.2f} 小时）"
    if len(rows) < n:
        note += f"；镜像只有 {len(rows)} 行指标（均值按这些行）"
    throughput.append({
        "run": lid, "gpu_session": str(led.get(lid, {}).get("gpu_session") or ""), "hardware": hardware(lid),
        "updates": n, "hours": num(h), "upd_per_hour": num(n / h if h else None, 1),
        "sps_mean": num(mean(m["sps"] for m in rows), 1), "t_env_mean": num(mean(m["t_env_s"] for m in rows)),
        "t_infer_mean": num(mean(m["t_infer_s"] for m in rows)),
        "t_update_mean": num(mean(m["t_update_s"] for m in rows)),
        "t_total_mean": num(mean(m["t_total_s"] for m in rows)),
        "gpu_peak_mb": num(max((m["gpu_mem_peak_mb"] for m in rows if m["gpu_mem_peak_mb"] is not None),
                               default=None), 1),
        "units_per_1000_updates": num(h / n * 1000 * UNITS_PER_GPU_HOUR if h else None, 2),
        "compile": compile_mode(lid), "envs": cfg_val(lid, "envs"), "rollout_steps": cfg_val(lid, "rollout"),
        "note": note,
    })
throughput.sort(key=lambda t: all_ids.index(t["run"]))

# ----------------------------------------------------------------------------------------------------------- lines
LINE_FIELDS = ("id", "run", "title", "hypothesis", "fork_line", "fork_update", "flags", "control", "gpu_session",
               "started", "ended", "status", "updates_from", "updates_to", "updates_done", "gpu_hours", "units_est",
               "endpoints", "best_ab512", "best_ab512_update", "best_dev128", "best_dev128_update", "paired",
               "verdict", "stop_reason", "lessons", "docs", "order")


LINE_STATUS = ("running", "done", "stopped", "planned", "lost")
VERDICTS = ("adopted", "neutral", "rejected", "stopped", "pending")


def status_json(lid):
    for d in line_dirs.get(lid, []):
        s = read_json(os.path.join(d, "status.json"), {})
        if isinstance(s, dict) and s:
            return s
    return {}


lines = []
for lid in all_ids:
    r = dict(led.get(lid) or {"id": lid})
    rows = metrics_full.get(lid) or []
    dirs = line_dirs.get(lid, [])
    if not r.get("run") and dirs:
        r["run"] = run_name(dirs[-1])
    if r.get("status") not in LINE_STATUS:
        st = status_json(lid).get("state")
        r["status"] = {"running": "running", "finished": "done"}.get(st, "stopped" if st else "planned")
        t_last = parse_time(rows[-1]["time"]) if rows else None
        if r["status"] == "running" and t_last and (NOW - t_last).total_seconds() > 3600:
            r["status"] = "stopped"                 # status.json never got its final write (lost / killed runtime)
    if r.get("verdict") not in VERDICTS:
        r["verdict"] = "stopped" if r["status"] == "stopped" else "pending"
    uf = intv(r.get("updates_from"))
    if rows:
        if uf is None:
            uf = rows[0]["update"] - 1
            r["updates_from"] = uf
        r["updates_done"] = rows[-1]["update"] - uf
        h = line_hours.get(lid)
        r["gpu_hours"] = num(h, 2)
        r["units_est"] = num(h * UNITS_PER_GPU_HOUR, 1) if h is not None else None
        if not r.get("started"):
            r["started"] = est(parse_time(rows[0]["time"]), "%m-%d %H:%M")
        if not r.get("ended") and r.get("status") not in ("running", "planned"):
            r["ended"] = est(parse_time(rows[-1]["time"]), "%m-%d %H:%M")
    else:
        r.setdefault("updates_done", 0 if r.get("status") == "planned" else r.get("updates_done"))
    # endpoints: hand-kept values merged with the readouts the files give (files win where they have a number)
    eps = {}
    for ep in as_list(r.get("endpoints")):
        if isinstance(ep, dict) and intv(ep.get("update")) is not None:
            eps[intv(ep["update"])] = {k: ep.get(k) for k in ("update", "ab512", "ab512_lo", "ab512_hi", "dev128",
                                                              "act3_ab512")}
    ab = {e["update"]: e for e in evals_for(lid, "ab-512") if e.get("endpoint")}
    dv = {e["update"]: e for e in evals_for(lid, "dev-128")}
    for u in sorted(set(eps) | set(ab) | (seg_ends.get(lid, set()) & set(dv))):
        ep = eps.setdefault(u, {"update": u, "ab512": None, "ab512_lo": None, "ab512_hi": None, "dev128": None,
                                "act3_ab512": None})
        if u in ab:
            ep.update(ab512=ab[u]["progress_mean"], ab512_lo=ab[u]["ci_lo"], ab512_hi=ab[u]["ci_hi"],
                      act3_ab512=ab[u]["act3"] if ab[u]["act3"] is not None else ep.get("act3_ab512"))
        if u in dv:
            ep["dev128"] = dv[u]["progress_mean"]
        for k in ("ab512", "ab512_lo", "ab512_hi", "dev128"):
            ep[k] = num(ep.get(k))
        ep["act3_ab512"] = intv(ep.get("act3_ab512"))
    eps_list = [eps[u] for u in sorted(eps) if any(eps[u].get(k) is not None for k in ("ab512", "dev128"))]
    r["endpoints"] = json.dumps(eps_list, ensure_ascii=False, separators=(",", ":"))
    b = best(evals_for(lid, "ab-512"))
    if b:
        r["best_ab512"], r["best_ab512_update"] = b["progress_mean"], b["update"]
    b = best(evals_for(lid, "dev-128"))
    if b:
        r["best_dev128"], r["best_dev128_update"] = b["progress_mean"], b["update"]
    p = r.get("paired")
    r["paired"] = p if isinstance(p, str) else json.dumps(p or [], ensure_ascii=False, separators=(",", ":"))
    r["docs"] = csv_ids(r.get("docs"))
    r["control"] = str(r.get("control") or "")
    for k in ("fork_update", "updates_from", "updates_to", "best_ab512_update", "best_dev128_update", "order",
              "updates_done"):
        r[k] = intv(r.get(k))
    for k in ("best_ab512", "best_dev128", "gpu_hours", "units_est"):
        r[k] = num(r.get(k))
    lines.append({k: (r.get(k) if r.get(k) is not None else (None if k in (
        "fork_update", "updates_from", "updates_to", "updates_done", "gpu_hours", "units_est", "best_ab512",
        "best_ab512_update", "best_dev128", "best_dev128_update", "order") else "")) for k in LINE_FIELDS})
# order: hand-kept rank, else chronological by start
for i, ln in enumerate(sorted(lines, key=lambda x: (x["order"] if x["order"] is not None else 10**6,
                                                     parse_time(x["started"]) or NOW))):
    if ln["order"] is None:
        ln["order"] = 1000 + i
lines.sort(key=lambda x: x["order"])

# ----------------------------------------------------------------------------------------------------- line_events
KINDS = ("start", "segment", "eval", "pair", "decision", "incident", "stop", "note")
line_events = []
for e in line_events_in:
    lid = str(e.get("line") or "").strip()
    if not lid:
        continue
    t = parse_time(e.get("time"))
    kind = e.get("kind") if e.get("kind") in KINDS else "note"
    line_events.append({"line": lid, "time": est(t) if t else str(e.get("time") or ""), "kind": kind,
                        "event": str(e.get("event") or ""), "update": intv(e.get("update")),
                        "value": num(e.get("value"))})
have = {(e["line"], e["update"]) for e in line_events if e["update"] is not None}
for lid in all_ids:                                  # segment-end readouts the hand-kept journey does not cover yet
    for e in evals_for(lid, "ab-512"):
        if not e.get("endpoint") or (lid, e["update"]) in have or \
                (lid, e["update"], e["seedset"], e["mode"]) in ledger_only:
            continue
        t = parse_time(e.get("time"))
        dvr = [x for x in evals_for(lid, "dev-128") if x["update"] == e["update"]]
        txt = f"段终 u{e['update']}：ab-512 {e['progress_mean']:.3f}"
        if e["ci_lo"] is not None and e["ci_hi"] is not None:
            txt += f" [{e['ci_lo']:.3f}, {e['ci_hi']:.3f}]"
        if dvr:
            txt += f"，dev-128 {dvr[0]['progress_mean']:.3f}"
        if e.get("act3") is not None:
            txt += f"，到 act 3 {e['act3']}/{e['games'] or 512}"
        line_events.append({"line": lid, "time": est(t) if t else "", "kind": "segment", "event": txt + "（自动）",
                            "update": e["update"], "value": e["progress_mean"]})
line_events.sort(key=lambda e: (e["line"], e["time"], e["update"] or 0))

# ---------------------------------------------------------------------------------------------------------- system
system = []
if isinstance(system_in, list):
    for r in system_in:
        if isinstance(r, dict) and r.get("key"):
            system.append({"key": str(r["key"]), "value": r.get("value"), "note": str(r.get("note") or "")})
elif isinstance(system_in, dict):
    for k, v in system_in.items():
        if isinstance(v, dict):
            system.append({"key": k, "value": v.get("value"), "note": str(v.get("note") or "")})
        else:
            system.append({"key": k, "value": v, "note": ""})
have_keys = {r["key"] for r in system}
running = [ln["id"] for ln in lines if ln["status"] == "running"]


def add_observed(key, value, note):
    if key not in have_keys and value not in (None, ""):
        system.append({"key": key, "value": value, "note": note})


obs_params, obs_env, obs_bundles = None, set(), {}
for lid in running or all_ids[-2:]:
    for d, s in line_sessions(lid):
        if s.get("bundle"):
            obs_bundles.setdefault(s["bundle"], set()).add(lid)
    for d in line_dirs.get(lid, []):
        c = configs.get(d, {})
        obs_params = obs_params or intv(c.get("params"))
        v = (c.get("library") or {}).get("version")
        if v:
            obs_env.add(v)
add_observed("observed_params", obs_params, "运行中各线 config.json 的参数量（核对用）")
add_observed("observed_env_hash", ", ".join(sorted(obs_env)), "运行中各线 config.json 的模拟器库版本（核对用）")
add_observed("observed_bundles", "; ".join(f"{b}: {','.join(sorted(ls))}" for b, ls in sorted(obs_bundles.items())),
             "运行中各线最近会话的包（核对用）")
add_observed("units_per_gpu_hour", UNITS_PER_GPU_HOUR, "一台 A100 运行时每小时约耗计算单位")

# ------------------------------------------------------------------------------------------------- balance / rate
readings = []                                        # (utc time, balance)
for e in events:
    m = re.search(r"余额\s*([0-9]+(?:\.[0-9]+)?)", str(e.get("event") or ""))
    t = parse_time(e.get("time"))
    if m and t:
        readings.append((t, float(m.group(1))))
for b in balance_log:
    t, v = parse_time(b.get("time")), num(b.get("balance"))
    if t and v is not None:
        readings.append((t, float(v)))
readings.sort()
if BALANCE is not None and readings:
    # A BALANCE argument lower than the newest logged reading by more than every GPU could have burned since then
    # (3 runtimes at UNITS_PER_GPU_HOUR, +2 units slack) is stale (typed from an older note, e.g. before a top-up):
    # warn and use the newest reading instead.
    t_last, b_last = readings[-1]
    max_burn = max(0.0, (NOW - t_last).total_seconds() / 3600) * 3 * UNITS_PER_GPU_HOUR + 2.0
    if BALANCE < b_last - max_burn:
        print(f"warning: balance argument {BALANCE} is below the {b_last} logged at {t_last:%Y-%m-%d %H:%M}Z by more "
              f"than {max_burn:.1f} units could burn since; using {b_last}", file=sys.stderr)
        BALANCE = b_last
if BALANCE is not None:
    readings.append((NOW, float(BALANCE)))
batch_t0 = parse_time(state.get("batch_started_at"))
readings = [x for x in readings if not batch_t0 or x[0] >= batch_t0]
topups = state.get("topups")
if isinstance(topups, list):
    topup_total = sum(num(x.get("units") if isinstance(x, dict) else x) or 0 for x in topups)
elif num(topups) is not None:
    topup_total = num(topups)
else:                                                # not hand-kept: positive balance jumps are top-ups
    topup_total = sum(b - a for (_, a), (_, b) in zip(readings, readings[1:]) if b - a > 1.0)
units_used = None
if BALANCE is not None and num(state.get("balance_at_batch_start")) is not None:
    units_used = num(state["balance_at_batch_start"] + topup_total - BALANCE, 2)
rate = num(opt.get("units_rate"), 2) if opt.get("units_rate") not in (None, "", "-") else None
if rate is None and num(state.get("units_rate_per_h")) is not None:
    rate = num(state["units_rate_per_h"], 2)
if rate is None:                                     # the newest `colab usage` rate in the balance log, if < 1 h old
    for b in sorted(balance_log, key=lambda b: str(b.get("time") or ""), reverse=True)[:1]:
        t = parse_time(b.get("time"))
        if t and (NOW - t).total_seconds() <= 3600 and num(b.get("rate")) is not None:
            rate = num(b["rate"], 2)
if rate is None:                                     # burn over the last 3 h of readings, after the last top-up
    win = [x for x in readings if (NOW - x[0]).total_seconds() <= 3 * 3600]
    for i in range(len(win) - 1, 0, -1):
        if win[i][1] - win[i - 1][1] > 1.0:
            win = win[i:]
            break
    if len(win) >= 2 and (win[-1][0] - win[0][0]).total_seconds() >= 15 * 60:
        rate = num((win[0][1] - win[-1][1]) / ((win[-1][0] - win[0][0]).total_seconds() / 3600), 2)
if rate is None and running:
    gpus = {str(ln["gpu_session"]) for ln in lines if ln["status"] == "running"}
    rate = num(len(gpus) * UNITS_PER_GPU_HOUR, 2)

# ---------------------------------------------------------------------------------------------------------- status
latest_lid, latest_t = None, None
for lid, rows in metrics_full.items():
    t = parse_time(rows[-1]["time"]) if rows else None
    if t and (latest_t is None or t > latest_t):
        latest_lid, latest_t = lid, t
state_run = state.get("run")
cur_lid = state_run if state_run in metrics_full and state_run in running else (
    latest_lid if latest_lid else state_run)
cur = metrics_full.get(cur_lid) or []
last = cur[-1] if cur else {}


def recent(key, n=20):
    return num(mean(m.get(key) for m in cur[-n:]))


def best_over(seedset):
    rows = [e for e in evals if e["seedset"] == seedset and e["mode"] == "greedy_off"
            and e["progress_mean"] is not None]
    return best(rows)


bab, bdv = best_over("ab-512"), best_over("dev-128")
act3s = [e["act3"] for e in evals if e["seedset"] == "ab-512" and e.get("endpoint") and e["act3"] is not None]
raw_rows = sum(len(v) for v in metrics_full.values())
status = [{
    "updated_at": NOW.astimezone(EST).strftime("%Y-%m-%d %H:%M EST"), "stage": state.get("stage") or "",
    "stage_label": state.get("stage_label") or "", "takeaway": state.get("takeaway") or "",
    "run": cur_lid or "", "gpu_session": state.get("gpu_session") or "", "update": last.get("update"),
    "env_steps_m": (round(last["env_steps"] / 1e6, 1) if last.get("env_steps") else None),
    "progress_fresh_recent": recent("progress_fresh"), "entropy": last.get("entropy"),
    "approx_kl": last.get("approx_kl"), "sps": last.get("sps"),
    "cycles_per_update_recent": recent("cycles_total"), "cycle_games_frac_recent": recent("cycle_games_frac"),
    "units_used": units_used, "units_cap": state.get("units_cap"), "balance": BALANCE,
    "next_report": state.get("next_report_local") or "", "rows_metrics": raw_rows, "rows_evals": len(evals),
    "lines_running": ",".join(running),
    "best_ab512": bab["progress_mean"] if bab else None, "best_ab512_line": bab["run"] if bab else "",
    "best_ab512_update": bab["update"] if bab else None,
    "best_dev128": bdv["progress_mean"] if bdv else None, "best_dev128_line": bdv["run"] if bdv else "",
    "best_dev128_update": bdv["update"] if bdv else None,
    "act3_best": max(act3s) if act3s else None, "units_rate_per_h": rate,
}]

# ------------------------------------------------------------------------------------------------- plan / timeline
plan = []
for p in state.get("plan") or []:
    if not isinstance(p, dict):
        continue
    eta = p.get("eta") or ""
    if not eta and p.get("status") == "done" and p.get("finished"):
        eta = f"完成 {p['finished']}"
    plan.append({"stage": p.get("stage") or "", "label": p.get("label") or p.get("title") or "",
                 "status": p.get("status") if p.get("status") in ("running", "pending", "done", "failed") else
                 "pending", "target_updates": intv(p.get("target_updates")), "started": p.get("started") or "",
                 "eta": eta, "note": p.get("note") or ""})

ID_RE = None
ids_for_match = sorted({i for i in all_ids if i}, key=len, reverse=True)
if ids_for_match:
    ID_RE = re.compile(r"(?<![A-Za-z0-9_])(?<!GPU )(" + "|".join(re.escape(i) for i in ids_for_match) +
                       r")(′?)(?![A-Za-z0-9_])")
PRIME = {"W0": "W0r", "C0": "C0r", "C1": "C1r"}


def event_lines(e):
    if e.get("lines") not in (None, ""):
        return csv_ids(e["lines"])
    if not ID_RE:
        return ""
    found = []
    for m in ID_RE.finditer(str(e.get("event") or "")):
        lid = m.group(1)
        if m.group(2) and lid in PRIME and PRIME[lid] in all_ids:
            lid = PRIME[lid]
        if lid not in found:
            found.append(lid)
    return ",".join(found)


timeline = []
for e in events:
    t = parse_time(e.get("time"))
    timeline.append({"time": est(t) if t else str(e.get("time") or ""),
                     "kind": e.get("kind") if e.get("kind") in ("ok", "info", "bad") else "info",
                     "event": str(e.get("event") or ""), "lines": event_lines(e)})
timeline.sort(key=lambda e: e["time"], reverse=True)

# --------------------------------------------------------------------------------------------- metrics (thinned)
# spec: per line every row of the last 300 updates, every 5th before. Over the byte budget, thin further in steps:
# stride 10, then finished lines keep only their last 100 in full, then stride 20 / 40 / 80.
running_set = set(running)
steps = [(300, 300, 5), (300, 300, 10), (300, 100, 10), (300, 100, 20), (300, 100, 40), (300, 100, 80)]
for tail_run, tail_done, stride in steps:
    metrics = []
    for lid in all_ids:
        metrics += thin(metrics_full.get(lid) or [], tail_run if lid in running_set else tail_done, stride)
    size = len(json.dumps(metrics, ensure_ascii=False, separators=(",", ":")).encode())
    if size <= MAX_METRICS_BYTES:
        break
metrics.sort(key=lambda m: (m["run"], m["update"]))

# ----------------------------------------------------------------------------------------------------------- write
for name, rows in (("status", status), ("plan", plan), ("timeline", timeline), ("metrics", metrics),
                   ("evals", evals), ("lines", lines), ("line_events", line_events), ("throughput", throughput),
                   ("system", system)):
    p = write(OUT, name, rows)
    print(f"{name}.json: {len(rows)} rows, {os.path.getsize(p)} B")
print(f"metrics thinning: every row of the last {tail_run} updates (running lines) / {tail_done} (finished lines), "
      f"every {stride}th before; {raw_rows} raw rows -> {len(metrics)}", file=sys.stderr)
