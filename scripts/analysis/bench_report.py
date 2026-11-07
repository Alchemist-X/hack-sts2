#!/usr/bin/env python3
"""bench_report.py: the throughput bench results of one runtime in, a table and a recommendation out.

    python3 -I scripts/analysis/bench_report.py runs/bench-w21 [more dirs / files ...] [--json OUT|-]
            [--envs 2048] [--rollout 64] [--epochs 1] [--live-mode default] [--min-gain 0.03]
            [--live-env 5.3] [--live-infer 2.4] [--live-update 3.2] [--live-total 12.9]

Inputs (directories are searched recursively; the launcher puts everything under runs/bench-w21/evals/ because
gcs_sync.py mirrors only evals/** plus a fixed list of run-root files):
  * ``sts2train bench-update --json`` files: ``{"torch", "device", "results": [{"mode", "update_ms",
    "rollout_step_ms", "infer_forward_ms", "gpu_peak_mb", "minibatch", "check": {"ok"}, "check_forward": {"ok"},
    "profile": {"kernels_per_minibatch"}}, ...]}``. One file per mode or one file with all modes: rows merge by
    mode (a later file fills only the keys an earlier one lacks).
  * ``sts2train bench-rollout --json`` files (wave 21; any file whose name contains "rollout" and that has a
    "results" list): per (mode, envs) ``step_ms`` (per step of the T + 1 window), ``window_s`` (T steps + the
    bootstrap = the rollout inference of one update), ``forward_ms``, ``phase_ms`` (t_h2d / t_forward / t_store /
    t_sample_d2h / t_track, ms per step), ``profile.launch_calls_per_step``, ``gpu_idle_frac``. Only the rows at
    --envs are read; they take precedence over bench-update's rollout_step_ms.
  * ``bench/overlap_bench.py`` output (``bench.json`` with ``measured.infer_ratio``) and the preflight's
    ``decision.txt`` (``OVERLAP=double|off``).

Per mode: ms per minibatch -> update seconds per training update (x ceil(envs * rollout / minibatch) * epochs), ms
per rollout step -> rollout inference seconds per update (bench-rollout's window_s, else x rollout), their sum = GPU
seconds per update, speed-up vs off, peak GPU MB, kernel launches (runtime launch calls; a CUDA-graph replay is one) per minibatch / per rollout step,
GPU idle share (update / rollout step, profiler) and the compiled-vs-eager checks. Recommendation:
the fastest mode whose checks pass, separately for the update and the rollout forward (a split recommendation maps to
--compile-update / --compile-infer), adopted only when it beats --live-mode (what the lines run today) by >=
--min-gain of the projected live update time; overlap is worth it iff r_inf <= 1.4 and the bench's measured
total_speedup > 1 (README "Rollout overlap"; and the preflight's verdict, when present, says double).
Exit 0 with a report, 2 when no bench data was found.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys

MODE_ORDER = ("off", "default", "reduce-overhead", "max-autotune", "max-autotune-no-cudagraphs")
R_INF_MAX = 1.4                     # README "Rollout overlap" / w3m plan §3.3 pass rule
KNOWN_MS = {"update_ms", "rollout_step_ms", "infer_forward_ms", "step_ms", "forward_ms", "phase_sum_ms"}


def _load(path: str):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def _files(paths: list[str]) -> list[str]:
    out = []
    for p in paths:
        if os.path.isdir(p):
            for root, dirs, files in os.walk(p):
                dirs.sort()
                out.extend(os.path.join(root, f) for f in sorted(files)
                           if f.endswith(".json") or f == "decision.txt")
        elif os.path.isfile(p):
            out.append(p)
    return out


def _is_rollout_file(path: str) -> bool:
    return "rollout" in os.path.basename(path).lower()


def collect(paths: list[str], envs: int | None = None) -> dict:
    """{"update": {mode: row}, "rollout": {mode: row}, "overlap": bench.json | None, "decision": str | None,
    "meta": {...}, "files": [...]}"""
    data = {"update": {}, "rollout": {}, "overlap": None, "overlap_file": None, "decision": None, "meta": {},
            "files": []}
    for f in _files(paths):
        if os.path.basename(f) == "decision.txt":
            try:
                text = open(f, encoding="utf-8").read()
            except OSError:
                continue
            m = re.search(r"OVERLAP=(\w+)", text)
            if m:
                data["decision"] = m.group(1)
                data["files"].append(f)
            continue
        d = _load(f)
        if not isinstance(d, dict):
            continue
        if isinstance(d.get("results"), list):
            kind = "rollout" if _is_rollout_file(f) else "update"
            used = False
            for r in d["results"]:
                if not isinstance(r, dict) or "mode" not in r:
                    continue
                if kind == "rollout" and envs is not None and _num(r.get("envs")) not in (None, envs):
                    continue                        # bench-rollout sweeps batch sizes: keep the live one
                row = data[kind].setdefault(str(r["mode"]), {})
                for k, v in r.items():
                    row.setdefault(k, v)
                used = True
            if used:
                data["files"].append(f)
                for k in ("torch", "device", "gpu"):
                    if d.get(k) is not None:
                        data["meta"].setdefault(k, d[k])
        elif isinstance(d.get("measured"), dict) or ("off" in d and "double" in d):
            data["overlap"], data["overlap_file"] = d, f
            data["files"].append(f)
    return data


def _ordered(modes) -> list[str]:
    known = [m for m in MODE_ORDER if m in modes]
    return known + sorted(m for m in modes if m not in MODE_ORDER)


def _num(x):
    return x if isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x) else None


def _checks_ok(*rows) -> bool | None:
    """True / False from the rows' check.ok / check_forward.ok; None when no check ran (off, or --no-check)."""
    seen = []
    for r in rows:
        for k in ("check", "check_forward"):
            c = (r or {}).get(k)
            if isinstance(c, dict) and "ok" in c:
                seen.append(bool(c["ok"]))
    return None if not seen else all(seen)


def table(data: dict, envs: int, rollout: int, epochs: int) -> list[dict]:
    """One dict per mode: update / rollout figures per training update, speed-ups vs off."""
    rows = []
    for mode in _ordered(set(data["update"]) | set(data["rollout"])):
        u, r = data["update"].get(mode, {}), data["rollout"].get(mode, {})
        mb = _num(u.get("minibatch")) or _num(r.get("minibatch"))
        n_mb = math.ceil(envs * rollout / mb) * epochs if mb else None
        upd_ms = _num(u.get("update_ms"))
        step_ms = _num(r.get("step_ms")) or _num(r.get("rollout_step_ms")) or _num(u.get("rollout_step_ms"))
        upd_s = upd_ms * n_mb / 1000.0 if upd_ms is not None and n_mb else None
        inf_s = (_num(r.get("window_s")) if _num(r.get("rollout_steps")) in (None, rollout) else None) or (
            step_ms * rollout / 1000.0 if step_ms is not None else None)
        gpu_s = upd_s + inf_s if upd_s is not None and inf_s is not None else None
        prof = u.get("profile") if isinstance(u.get("profile"), dict) else {}
        rprof = r.get("profile") if isinstance(r.get("profile"), dict) else {}
        launches = (_num(prof.get("launch_calls_per_minibatch")) or _num(prof.get("kernels_per_minibatch"))
                    or _num(u.get("launches")))
        rl = (_num(rprof.get("launch_calls_per_step")) or _num(r.get("launches"))
              or _num(rprof.get("device_ops_per_step")))
        peaks = [p for p in (_num(u.get("gpu_peak_mb")), _num(r.get("gpu_peak_mb"))) if p is not None]
        rows.append({"mode": mode, "minibatch": mb, "minibatches_per_update": n_mb, "update_ms": upd_ms,
                     "update_s": upd_s, "step_ms": step_ms, "forward_ms": _num(r.get("forward_ms"))
                     or _num(r.get("infer_forward_ms")) or _num(u.get("infer_forward_ms")),
                     "idle_update": _num(u.get("gpu_idle_frac")), "idle_step": _num(r.get("gpu_idle_frac")), "infer_s": inf_s, "gpu_s": gpu_s,
                     "peak_mb": max(peaks) if peaks else None, "launches": launches, "rollout_launches": rl,
                     "checks_ok": _checks_ok(u, r), "compile_s": _num(u.get("compile_s"))})
    base = next((x for x in rows if x["mode"] == "off"), rows[0] if rows else None)
    for x in rows:
        for key, sp in (("update_ms", "x_update"), ("step_ms", "x_step"), ("gpu_s", "x_gpu")):
            b, v = (base or {}).get(key), x.get(key)
            x[sp] = b / v if b is not None and v else None
    return rows


def phase_rows(data: dict) -> list[dict]:
    """bench-rollout phase splits per mode: a `phases` dict, else every *_ms key other than the headline ones."""
    out = []
    for mode in _ordered(data["rollout"]):
        r = data["rollout"][mode]
        ph = next((r[k] for k in ("phase_ms", "phases") if isinstance(r.get(k), dict)), None)
        if ph is None:
            ph = {k: v for k, v in r.items() if k.endswith("_ms") and k not in KNOWN_MS}
        ph = {k: v for k, v in ph.items() if _num(v) is not None}
        if ph:
            out.append({"mode": mode, "phases": ph})
    return out


def recommend(rows: list[dict], data: dict, a) -> dict:
    """{"compile": text, "compile_update", "compile_infer", "adopt": bool, "projected_s", "live_s", "overlap": text,
    "overlap_worth": bool | None}"""
    live_other = max(0.0, a.live_total - a.live_env - a.live_infer - a.live_update)
    by = {x["mode"]: x for x in rows}
    rec = {"live_s": a.live_total, "live_mode": a.live_mode}
    ref = by.get(a.live_mode) or by.get("off")
    ok = [x for x in rows if x["checks_ok"] is not False]
    bad = [x["mode"] for x in rows if x["checks_ok"] is False]
    best_u = min((x for x in ok if x["update_ms"] is not None), key=lambda x: x["update_ms"], default=None)
    best_i = min((x for x in ok if x["step_ms"] is not None), key=lambda x: x["step_ms"], default=None)
    if not ref or not best_u or not best_i or ref["update_ms"] is None or ref["step_ms"] is None:
        rec.update(compile="compile: not enough bench data (need update and rollout-step timings of the live mode)",
                   adopt=False)
    else:
        if ref["mode"] != a.live_mode:
            rec["note"] = f"live mode {a.live_mode!r} not benched; ratios are against {ref['mode']!r}"
        ru, ri = best_u["update_ms"] / ref["update_ms"], best_i["step_ms"] / ref["step_ms"]
        proj = a.live_env + a.live_infer * ri + a.live_update * ru + live_other
        gain = (a.live_total - proj) / a.live_total
        rec.update(compile_update=best_u["mode"], compile_infer=best_i["mode"], projected_s=round(proj, 2),
                   update_ratio=round(ru, 4), infer_ratio=round(ri, 4),
                   gain=round(gain, 4), adopt=gain >= a.min_gain and (best_u["mode"], best_i["mode"]) !=
                   (ref["mode"], ref["mode"]))
        how = (f"--compile {best_u['mode']}" if best_u["mode"] == best_i["mode"] else
               f"--compile-update {best_u['mode']} --compile-infer {best_i['mode']}")
        proj_txt = (f"projected live update {a.live_total:.1f} s -> {proj:.1f} s (x{a.live_total / proj:.2f}; update "
                    f"x{1 / ru:.2f}, rollout inference x{1 / ri:.2f} vs {ref['mode']})")
        if rec["adopt"]:
            rec["compile"] = f"compile: ADOPT {how}: {proj_txt}"
        else:
            rec["compile"] = (f"compile: KEEP --compile {a.live_mode} (best: {how}; {proj_txt}; gain "
                              f"{gain:+.1%} < {a.min_gain:.0%})")
        if bad:
            rec["compile"] += f"; excluded (compiled-vs-eager MISMATCH): {', '.join(bad)}"
    ov = data.get("overlap")
    if not ov:
        rec.update(overlap="overlap: no preflight data", overlap_worth=None)
        return rec
    m = ov.get("measured") or {}
    r_inf, r_env, tot = _num(m.get("infer_ratio")), _num(m.get("env_ratio")), _num(m.get("total_speedup"))
    worth = r_inf is not None and r_inf <= R_INF_MAX and tot is not None and tot > 1.0
    dec = data.get("decision")
    txt = f"r_inf {r_inf} (<= {R_INF_MAX}), bench total_speedup {tot} (> 1)"
    if dec is not None:
        txt += f", preflight verdict OVERLAP={dec}"
        if worth and dec != "double":
            txt += " (numbers pass, but the 3-update compile check did not: see compile.out / train.log)"
        worth = worth and dec == "double"
    if worth and r_env is not None:
        # live projection (README): double ~ max(env x r_env, infer x r_inf) + update + other, at today's live phases
        # (after the compile recommendation when it is adopted: its rollout / update ratios apply first)
        ri, ru = (rec["infer_ratio"], rec["update_ratio"]) if rec.get("adopt") else (1.0, 1.0)
        inf_live, upd_live = a.live_infer * ri, a.live_update * ru
        dbl = max(a.live_env * r_env, inf_live * r_inf) + upd_live + live_other
        txt += f"; projected live update {a.live_env + inf_live + upd_live + live_other:.1f} s -> {dbl:.1f} s"
        rec["overlap_projected_s"] = round(dbl, 2)
    rec["overlap_worth"] = worth
    rec["overlap"] = ("overlap: WORTH IT (" if worth else "overlap: NOT worth it (") + txt + ")" + (
        "; note --overlap double refuses --harvest and --start-saves (the C lineage runs harvest)" if worth else "")
    return rec


def _phase_name(k: str) -> str:
    """t_h2d_s / t_h2d_ms -> h2d (bench-rollout's phase_ms keys are the metrics' t_*_s names, values in ms)."""
    return re.sub(r"_(s|ms)$", "", k[2:] if k.startswith("t_") else k)


def _f(v, fmt, dash="-"):
    return dash if v is None else format(v, fmt)


def render(data: dict, rows: list[dict], phases: list[dict], rec: dict, a) -> str:
    meta = data["meta"]
    lines = [f"bench report: {len(data['files'])} files; torch {meta.get('torch', '?')}, device {meta.get('device', '?')}"
             f"; per update = {a.envs} envs x {a.rollout} steps, epochs {a.epochs}"]
    if rows:
        hdr = (f"{'mode':<16} {'ms/mb':>8} {'upd s/u':>8} {'step ms':>8} {'inf s/u':>8} {'gpu s/u':>8} "
               f"{'x vs off':>8} {'x upd':>6} {'x step':>6} {'peak MB':>8} {'launches':>9} {'idle u/r':>9} {'check':>8}")
        lines += ["", hdr, "-" * len(hdr)]
        for x in rows:
            ck = {True: "ok", False: "MISMATCH", None: "-"}[x["checks_ok"]]
            la = _f(x["launches"], ".0f") + (f"/{x['rollout_launches']:.0f}" if x["rollout_launches"] else "")
            idle = _f(x["idle_update"], ".0%") + "/" + _f(x["idle_step"], ".0%")
            lines.append(f"{x['mode']:<16} {_f(x['update_ms'], '8.1f'):>8} {_f(x['update_s'], '8.2f'):>8} "
                         f"{_f(x['step_ms'], '8.1f'):>8} {_f(x['infer_s'], '8.2f'):>8} {_f(x['gpu_s'], '8.2f'):>8} "
                         f"{_f(x['x_gpu'], '8.2f'):>8} {_f(x['x_update'], '6.2f'):>6} {_f(x['x_step'], '6.2f'):>6} "
                         f"{_f(x['peak_mb'], '8.0f'):>8} {la:>9} {idle:>9} {ck:>8}")
        lines.append("  ms/mb = ms per minibatch; upd s/u = x minibatches per update; step ms = one rollout step "
                     "(upload + forward + sample + store + host copies); inf s/u = one rollout window (T steps + "
                     "bootstrap); launches = launch calls per minibatch[/per rollout step]; idle = GPU idle share")
    if phases:
        lines += ["", "rollout phases (bench-rollout, ms per step):"]
        for p in phases:
            lines.append(f"  {p['mode']:<16} " + "  ".join(f"{_phase_name(k)} {v:.2f}" for k, v in p["phases"].items()))
    ov = data.get("overlap")
    if ov:
        m = ov.get("measured") or {}
        lines += ["", f"overlap bench ({data['overlap_file']}): "
                  + ", ".join(f"{k} {v}" for k, v in m.items())]
        if ov.get("a100_projection"):
            lines.append(f"  bench's own A100 projection: {ov['a100_projection']}")
    lines += ["", "RECOMMENDATION:", "  " + rec["compile"], "  " + rec["overlap"]]
    if rec.get("note"):
        lines.append("  note: " + rec["note"])
    return "\n".join(lines)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("paths", nargs="+", help="bench result dirs / files (e.g. runs/bench-w21)")
    ap.add_argument("--envs", type=int, default=2048, help="env slots per update (default 2048)")
    ap.add_argument("--rollout", type=int, default=64, help="rollout steps per update (default 64)")
    ap.add_argument("--epochs", type=int, default=1, help="PPO epochs per update (default 1)")
    ap.add_argument("--live-mode", dest="live_mode", default="default", help="compile mode the lines run today")
    ap.add_argument("--min-gain", dest="min_gain", type=float, default=0.03,
                    help="adopt a new mode only above this projected live speed-up share (default 0.03)")
    ap.add_argument("--live-env", dest="live_env", type=float, default=5.3, help="live t_env_s (default 5.3)")
    ap.add_argument("--live-infer", dest="live_infer", type=float, default=2.4, help="live t_infer_s (default 2.4)")
    ap.add_argument("--live-update", dest="live_update", type=float, default=3.2, help="live t_update_s (3.2)")
    ap.add_argument("--live-total", dest="live_total", type=float, default=12.9, help="live t_total_s (12.9)")
    ap.add_argument("--json", help="also write the rows and the recommendation as JSON to this file (- = stdout)")
    a = ap.parse_args(argv)
    data = collect(a.paths, a.envs)
    if not data["update"] and not data["rollout"] and not data["overlap"]:
        print(f"bench report: no bench data under {', '.join(a.paths)}")
        return 2
    rows = table(data, a.envs, a.rollout, a.epochs)
    phases = phase_rows(data)
    rec = recommend(rows, data, a)
    print(render(data, rows, phases, rec, a))
    if a.json:
        doc = json.dumps({"rows": rows, "phases": phases, "recommendation": rec, "files": data["files"],
                          "meta": data["meta"]}, indent=1)
        if a.json == "-":
            print(doc)
        else:
            with open(a.json, "w", encoding="utf-8") as f:
                f.write(doc + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
