#!/usr/bin/env python3
"""check_page.py — cross-check the dashboard page against the dataset files the builder produced.

Usage: python3 -I check_page.py [PAGE_HTML] [OUT_DIR]
  PAGE_HTML  default: index.html next to this script
  OUT_DIR    default: $DASH_OUT, else $STS2_OPS_DIR/out

Checks (exit 1 on any failure; warnings do not fail):
  1. every data-source id in the markup is a produced dataset, and every data-field resolves (via its nearest
     data-source ancestor) to a field present in that dataset's rows; data-row-key fields likewise;
  2. every dataset id the script reads (ds('x') / row0('x')) is produced; the status keys read by field(...), the
     metrics keys passed to the chart helpers and the key arrays, all exist in the produced rows;
  3. every dataset field named in the dashboard v2 contract exists in every output row (null values allowed);
  4. every line_events.line, every lines.fork_line / control and every paired[].vs is a lines.id;
  5. the page is under 200 KB, every out file under 3 MB;
  6. every contract dataset is read by the page; data-draw names in the markup and the script's registrations agree
     (warnings);
  7. the page's <script> passes `node --check` when node is on PATH; one <style>, one <script>, the dash-one-writer
     meta first, no innerHTML / fetch / storage calls.
"""
import html.parser
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

CONTRACT = {
    "status": "updated_at stage stage_label takeaway run gpu_session update env_steps_m progress_fresh_recent entropy "
              "approx_kl sps cycles_per_update_recent cycle_games_frac_recent units_used units_cap balance next_report "
              "rows_metrics rows_evals lines_running best_ab512 best_ab512_line best_ab512_update best_dev128 "
              "best_dev128_line best_dev128_update act3_best units_rate_per_h",
    "plan": "stage label status target_updates started eta note",
    "timeline": "time kind event lines",
    "metrics": "run update env_steps time progress_fresh progress_all entropy approx_kl sps episodes t_env_s t_infer_s "
               "t_update_s t_total_s gpu_mem_peak_mb harvest_live harvest_start_share save_episodes save_wins "
               "save_progress_gain_mean boss2_kills boss_bonus_sum explained_variance clipfrac grad_norm cycles_total "
               "cycle_games_frac act3_frac fresh_win_rate",
    "evals": "run update seedset seedset_id mode progress_mean ci_lo ci_hi games wins clear_rate act3 stuck interventions "
             "cycles floor_mean weights_hash endpoint time",
    "lines": "id run title hypothesis fork_line fork_update flags control gpu_session started ended status updates_from "
             "updates_to updates_done gpu_hours units_est endpoints best_ab512 best_ab512_update best_dev128 "
             "best_dev128_update paired verdict stop_reason lessons docs order",
    "line_events": "line time kind event update value",
    "throughput": "run gpu_session hardware updates hours upd_per_hour sps_mean t_env_mean t_infer_mean t_update_mean "
                  "t_total_mean gpu_peak_mb units_per_1000_updates compile envs rollout_steps note",
    "system": "key value note",
}
CONTRACT = {k: v.split() for k, v in CONTRACT.items()}
ENUMS = {("plan", "status"): {"running", "pending", "done", "failed"},
         ("timeline", "kind"): {"ok", "info", "bad"},
         ("lines", "status"): {"running", "done", "stopped", "planned", "lost"},
         ("lines", "verdict"): {"adopted", "neutral", "rejected", "stopped", "pending"},
         ("line_events", "kind"): {"start", "segment", "eval", "pair", "decision", "incident", "stop", "note"},
         ("throughput", "compile"): {"eager", "compiled", "mixed", "unknown"},
         ("evals", "mode"): {"greedy_off", "greedy_on", "sample_off", "sample_on"},
         ("evals", "seedset"): {"dev-128", "ab-512", "blind-256", "test-100", "other"}}
PAGE_MAX, FILE_MAX = 200 * 1024, 3 * 1024 * 1024

here = os.path.dirname(os.path.abspath(__file__))
page_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "index.html")
out_dir = sys.argv[2] if len(sys.argv) > 2 else (os.environ.get("DASH_OUT") or
                                                  os.path.join(os.environ.get("STS2_OPS_DIR", "."), "out"))
errors, warns = [], []
err = errors.append
warn = warns.append

# ------------------------------------------------------------------------------------------------ datasets
data = {}
for ds in CONTRACT:
    p = os.path.join(out_dir, ds + ".json")
    if not os.path.isfile(p):
        err(f"missing out file {ds}.json")
        continue
    size = os.path.getsize(p)
    if size >= FILE_MAX:
        err(f"{ds}.json is {size} B (limit {FILE_MAX})")
    try:
        rows = json.load(open(p, encoding="utf-8"))
    except Exception as e:
        err(f"{ds}.json does not parse: {e}")
        continue
    if not isinstance(rows, list) or not all(isinstance(r, dict) for r in rows):
        err(f"{ds}.json is not an array of row objects")
        continue
    data[ds] = rows
fields = {ds: set().union(*[set(r) for r in rows]) if rows else set() for ds, rows in data.items()}

# 3. contract fields present in every row (null allowed), allowed values
for ds, rows in data.items():
    want = set(CONTRACT[ds])
    if not rows:
        warn(f"{ds}.json has no rows")
    for i, r in enumerate(rows):
        miss = want - set(r)
        if miss:
            err(f"{ds}[{i}] lacks contract fields {sorted(miss)}")
            break
    extra = fields[ds] - want
    if extra:
        warn(f"{ds}: fields outside the contract {sorted(extra)}")
    for (d2, f), allowed in ENUMS.items():
        if d2 != ds:
            continue
        bad = sorted({str(r.get(f)) for r in rows if r.get(f) is not None and r.get(f) not in allowed})
        if bad:
            err(f"{ds}.{f} has values outside {sorted(allowed)}: {bad[:5]}")
if "status" in data and len(data["status"]) != 1:
    err(f"status has {len(data['status'])} rows (want 1)")

# 4. referential integrity
if "lines" in data:
    lids = [r.get("id") for r in data["lines"]]
    if len(lids) != len(set(lids)):
        err("lines.id not unique")
    lid = set(lids)
    for r in data.get("line_events", []):
        if r.get("line") not in lid:
            err(f"line_events.line {r.get('line')!r} is not a lines.id")
    for r in data["lines"]:
        for f in ("fork_line", "control"):
            v = r.get(f)
            if v not in (None, "") and v not in lid:
                err(f"lines[{r.get('id')}].{f} = {v!r} is not a lines.id")
        for f in ("endpoints", "paired"):
            v = r.get(f)
            try:
                arr = json.loads(v) if isinstance(v, str) and v else (v or [])
            except Exception:
                err(f"lines[{r.get('id')}].{f} is not a JSON string")
                continue
            if not isinstance(arr, list):
                err(f"lines[{r.get('id')}].{f} is not a JSON array")
                continue
            if f == "paired":
                for p in arr:
                    if isinstance(p, dict) and p.get("vs") not in lid:
                        err(f"lines[{r.get('id')}].paired.vs = {p.get('vs')!r} is not a lines.id")
    for ds2, f in (("metrics", "run"), ("evals", "run"), ("throughput", "run")):
        unknown = sorted({str(r.get(f)) for r in data.get(ds2, []) if r.get(f) not in lid})
        if unknown:
            warn(f"{ds2}.{f} values that are not lines.id: {unknown}")
    for r in data.get("status", []):
        for f in ("best_ab512_line", "best_dev128_line"):
            if r.get(f) not in (None, "") and r.get(f) not in lid:
                err(f"status.{f} = {r.get(f)!r} is not a lines.id")
        for x in str(r.get("lines_running") or "").split(","):
            if x.strip() and x.strip() not in lid:
                err(f"status.lines_running names {x.strip()!r}, not a lines.id")

# ------------------------------------------------------------------------------------------------ page markup
src = open(page_path, encoding="utf-8").read()
psize = len(src.encode("utf-8"))
if psize >= PAGE_MAX:
    err(f"page is {psize} B (limit {PAGE_MAX})")


class P(html.parser.HTMLParser):
    VOID = {"meta", "br", "img", "input", "hr", "link", "col", "source", "wbr", "area", "base", "embed", "track", "param"}

    def __init__(self):
        super().__init__()
        self.stack = []      # (tag, source)
        self.marks = []      # (source, attr, value, line)
        self.first = None
        self.styles = self.scripts = 0
        self.script_text = []
        self._in_script = False

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if self.first is None:
            self.first = (tag, a)
        if tag == "style":
            self.styles += 1
        if tag == "script":
            self.scripts += 1
            self._in_script = True
        src_ = a.get("data-source") or (self.stack[-1][1] if self.stack else None)
        line = self.getpos()[0]
        if "data-source" in a:
            self.marks.append((a["data-source"], "data-source", a["data-source"], line))
        for k in ("data-field", "data-row-key"):
            if k in a:
                self.marks.append((src_, k, a[k], line))
        if tag not in self.VOID:
            self.stack.append((tag, src_))

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID and self.stack:
            self.stack.pop()

    def handle_endtag(self, tag):
        if tag == "script":
            self._in_script = False
        for i in range(len(self.stack) - 1, -1, -1):
            if self.stack[i][0] == tag:
                del self.stack[i:]
                break

    def handle_data(self, d):
        if self._in_script:
            self.script_text.append(d)


pp = P()
pp.feed(src)
if not pp.first or pp.first[0] != "meta" or pp.first[1].get("name") != "dash-one-writer":
    err("page does not start with <meta name=\"dash-one-writer\">")
if pp.styles != 1 or pp.scripts != 1:
    err(f"page has {pp.styles} <style> and {pp.scripts} <script> (want 1 and 1)")
n_marks = 0
for source, attr, value, line in pp.marks:
    n_marks += 1
    if source is None:
        err(f"line {line}: {attr}={value!r} has no data-source ancestor")
        continue
    if source not in data:
        err(f"line {line}: data-source {source!r} is not a produced dataset")
        continue
    if attr == "data-field" and value not in fields[source]:
        err(f"line {line}: data-field {value!r} not in {source} rows")
    if attr == "data-row-key":
        for k in value.split(","):
            if k.strip() not in fields[source]:
                err(f"line {line}: data-row-key part {k!r} not in {source} rows")

# ------------------------------------------------------------------------------------------------ page script
js = "".join(pp.script_text)
for bad in (r"\.innerHTML\b", r"\.outerHTML\b", r"insertAdjacentHTML", r"\bfetch\s*\(", r"XMLHttpRequest",
            r"\blocalStorage\b", r"\bsessionStorage\b", r"\bindexedDB\b", r"\bWebSocket\b", r"\beval\s*\("):
    if re.search(bad, js):
        err(f"script uses {bad}")
read_ids = set(re.findall(r"\b(?:ds|row0|notReady)\(\s*'([a-z_]+)'\s*\)", js))
for i in sorted(read_ids):
    if i not in data:
        err(f"script reads dataset {i!r}, not produced")
for i in sorted(set(CONTRACT) - read_ids):
    err(f"dataset {i!r} is produced but the page never reads it")
draw_marks = set(re.findall(r'data-draw="([a-z0-9_-]+)"', src.replace(js, '')))
draw_regs = set(re.findall(r"\bD\(\s*'([^']+)'", js)) | set(re.findall(r"dash\.draw\(\s*'([^']+)'", js))
draw_regs |= set(re.findall(r"\bfield\(\s*'([^']+)'", js))
for n in sorted(draw_marks - draw_regs):
    warn(f"data-draw={n!r} in markup has no draw registration")
for n in sorted(draw_regs - draw_marks):
    warn(f"draw {n!r} registered but no element carries data-draw={n!r}")
for k in re.findall(r"\bfield\(\s*'[^']*'\s*,\s*'([a-z0-9_]+)'", js):
    if k not in fields.get("status", set()):
        err(f"script reads status.{k}, not in status rows")
metric_keys = set(re.findall(r"runsChart\(\s*\w+\s*,\s*'([a-z0-9_]+)'", js))
metric_keys |= set(re.findall(r"\[\s*'([a-z][a-z0-9_]*)'\s*,\s*'[^']*[^\x00-\x7f][^']*'", js))   # ['key', '中文标签' ...]
metric_keys |= {k for k in re.findall(r"'(t_[a-z]+_s)'", js)}
mf = fields.get("metrics", set())
for k in sorted(metric_keys):
    if k not in mf:
        err(f"script charts metrics.{k}, not in metrics rows")
# metrics keys with no non-null value anywhere (charts would stay empty)
for k in sorted(metric_keys & mf):
    if not any(r.get(k) is not None for r in data.get("metrics", [])):
        warn(f"metrics.{k} is null in every row")

node = shutil.which("node")
if node:
    with tempfile.NamedTemporaryFile("w", suffix=".js", delete=False, encoding="utf-8") as t:
        t.write(js)
    r = subprocess.run([node, "--check", t.name], capture_output=True, text=True)
    os.unlink(t.name)
    if r.returncode:
        msg = [x for x in r.stderr.splitlines() if "Error" in x] or r.stderr.strip().splitlines() or ["?"]
        err("node --check failed: " + msg[0])
    node_msg = "node --check ok" if not r.returncode else "node --check FAILED"
else:
    node_msg = "node not found: syntax check skipped"
    warn(node_msg)

# ------------------------------------------------------------------------------------------------ report
print(f"page {page_path}: {psize} B, {n_marks} marks, datasets read by script {sorted(read_ids)}, "
      f"{len(metric_keys)} metric keys; {node_msg}")
for ds in CONTRACT:
    if ds in data:
        print(f"  {ds + '.json':18s} {len(data[ds]):6d} rows {os.path.getsize(os.path.join(out_dir, ds + '.json')):9d} B")
for w in warns:
    print("warning:", w)
for e in errors:
    print("ERROR:", e)
print("OK" if not errors else f"{len(errors)} error(s)")
sys.exit(1 if errors else 0)
