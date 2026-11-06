#!/usr/bin/env python3
"""dash_data.py — build the dashboard's dataset files from the hand-kept state, the event log and the run mirror.
Usage: python3 -I dash_data.py STATE_JSON EVENTS_JSONL OUT_DIR [BALANCE]
Writes OUT_DIR/{status,plan,timeline,metrics,evals}.json (arrays of row objects).
A relative run "dir" in STATE_JSON (e.g. mirror/w3m-a) is resolved against $STS2_OPS_DIR, default the directory of
STATE_JSON; absolute dirs are used as they are."""
import json, os, sys, time, datetime as dt

state = json.load(open(sys.argv[1]))
events = [json.loads(l) for l in open(sys.argv[2]) if l.strip()]
out = sys.argv[3]; os.makedirs(out, exist_ok=True)
balance = float(sys.argv[4]) if len(sys.argv) > 4 and sys.argv[4] not in ("", "-") else None
now = dt.datetime.now(dt.timezone.utc)
EST = dt.timezone(dt.timedelta(hours=-5))
now_local = now.astimezone(EST).strftime("%Y-%m-%d %H:%M EST")

def jload(path):
    rows = []
    if path and os.path.isfile(path):
        for l in open(path, encoding="utf-8", errors="replace"):
            l = l.strip()
            if not l: continue
            try: rows.append(json.loads(l))
            except Exception: pass
    return rows

def g(r, *keys, default=None):
    for k in keys:
        if k in r and r[k] is not None: return r[k]
    return default

# --- metrics from the mirror(s): state["runs"] = [{name, dir}] or single mirror_dir
runs = state.get("runs") or ([{"name": state.get("run") or "run", "dir": state["mirror_dir"]}] if state.get("mirror_dir") else [])
BASE = os.environ.get("STS2_OPS_DIR") or os.path.dirname(os.path.abspath(sys.argv[1]))
runs = [dict(r, dir=os.path.join(BASE, r["dir"])) for r in runs]   # join keeps an absolute dir unchanged
metrics, evals = [], []
last = None
for run in runs:
    rows = jload(os.path.join(run["dir"], "metrics.jsonl"))
    for r in rows:
        eps = g(r, "episodes", default=0) or 0
        ewc = g(r, "episodes_with_cycle", default=None)
        metrics.append({
            "run": run["name"], "update": g(r, "update"), "env_steps": g(r, "env_steps"),
            "progress_fresh": g(r, "fresh_progress_mean", "progress_mean"), "progress_all": g(r, "progress_mean"),
            "entropy": g(r, "entropy"), "approx_kl": g(r, "approx_kl"), "sps": g(r, "env_steps_per_s"),
            "episodes": eps, "cycles_total": g(r, "cycles_total", default=None),
            "cycles_rest": g(r, "cycles_rest", default=None), "cycles_shop": g(r, "cycles_shop", default=None),
            "cycle_games_frac": (ewc / eps if (ewc is not None and eps) else None),
            "cycle_cost_total": g(r, "cycle_cost_total", default=None), "floor_stall": g(r, "floor_stall", default=None),
            "t_total_s": g(r, "t_total_s"), "time": g(r, "time"),
        })
        last = (run["name"], r)
    for e in jload(os.path.join(run["dir"], "evals.jsonl")):
        ci = g(e, "progress_ci95", default=[None, None]) or [None, None]
        evals.append({
            "run": run["name"], "update": g(e, "update"), "seedset": g(e, "seedset"), "mode": g(e, "mode", default="greedy_on"),
            "progress_mean": g(e, "progress_mean"), "ci_lo": ci[0], "ci_hi": ci[1], "games": g(e, "games"),
            "wins": g(e, "wins"), "stuck": g(e, "stuck", default=None), "interventions": g(e, "loop_breaks", "interventions", default=None),
            "cycles": g(e, "cycles", default=None), "floor_mean": g(e, "floor_mean"), "time": g(e, "time"),
        })

# recent-20 means
def recent_mean(key, n=20):
    v = [m[key] for m in metrics[-n:] if m.get(key) is not None]
    return (sum(v) / len(v)) if v else None

units_used = (state["balance_at_batch_start"] - balance) if balance is not None else None
status = [{
    "updated_at": now_local, "stage": state["stage"], "stage_label": state["stage_label"], "takeaway": state["takeaway"],
    "run": state.get("run") or "", "gpu_session": state.get("gpu_session") or "",
    "update": (last[1].get("update") if last else None), "env_steps_m": (round(last[1].get("env_steps", 0) / 1e6, 1) if last else None),
    "progress_fresh_recent": recent_mean("progress_fresh"), "entropy": (last[1].get("entropy") if last else None),
    "approx_kl": (last[1].get("approx_kl") if last else None), "sps": (last[1].get("env_steps_per_s") if last else None),
    "cycles_per_update_recent": recent_mean("cycles_total"), "cycle_games_frac_recent": recent_mean("cycle_games_frac"),
    "units_used": (round(units_used, 2) if units_used is not None else None), "units_cap": state["units_cap"],
    "balance": balance, "last_sync": state.get("last_sync") or "", "last_ping": state.get("last_ping") or "",
    "next_report": state.get("next_report_local") or "", "rows_metrics": len(metrics), "rows_evals": len(evals),
}]
# thin the curve rows so the page stays light: per run keep every row of the last 400 updates, every 3rd before
thinned = []
by_run = {}
for m in metrics:
    by_run.setdefault(m["run"], []).append(m)
for run, rows_ in by_run.items():
    n = len(rows_)
    for i, m in enumerate(rows_):
        if i >= n - 400 or i % 3 == 0:
            thinned.append(m)
metrics = sorted(thinned, key=lambda m: (m["run"], m["update"] or 0))
timeline = sorted(events, key=lambda e: e["time"], reverse=True)
plan = state["plan"]
for name, rows in (("status", status), ("plan", plan), ("timeline", timeline), ("metrics", metrics), ("evals", evals)):
    with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False)
    print(f"{name}.json: {len(rows)} rows, {os.path.getsize(os.path.join(out, name + '.json'))} B")
