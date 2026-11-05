#!/bin/bash
# w3m_compare.sh N   (docs/w3m-overnight-plan-2026-11-04.md §4-5): the paired comparisons at comparison point uN
# (N in 3564 4064 ... 7564 7664) once both lines reached it, plus the pre-registered rule readings. Run on a VM
# with the w3m wheels installed (VM B), never on the Mac. Pulls the eval dirs (no checkpoints) of M, A and B from GCS
# first (FETCH=0 skips that: dirs already local). Writes $R/cmp/*.{txt,json}; prints one line per rule.
#   env: R=/content/sts2/runs  M=sp3-main  A=w3m-a  B=w3m-b  STS=/content/sts2  BUCKET  FETCH=1
set -u
N=$1; R=${R:-/content/sts2/runs}; M=${M:-sp3-main}; A=${A:-w3m-a}; LB=${B:-w3m-b}
STS=${STS:-/content/sts2}; BUCKET=${BUCKET:-gomoku-zero-20260816-ai-sts2train}
EV="python3 -m sts2train.cli"
if [ "${FETCH:-1}" = 1 ]; then
  for run in "$M" "$A" "$LB"; do
    python3 "$(dirname "$0")/w3m_fetch.py" --key "$STS/gcs-sts2-ckpt.json" --bucket "$BUCKET" \
      --prefix "runs/$run/evals" --dest "$R/$run/evals"
  done
fi
mkdir -p "$R/cmp"
u=$(printf 'u%07d' "$N")
pair() {   # NAME DIR_A DIR_B LABELS
  [ -f "$2/report.json" ] && [ -f "$3/report.json" ] || { echo "skip $1: missing $2 or $3"; return 0; }
  $EV compare "$2" "$3" --labels "$4" --json "$R/cmp/$1.json" > "$R/cmp/$1.txt" 2>&1 || echo "compare $1 failed"
}
for m in greedy-off greedy-on; do                                              # dev-128 greedy-off = PRIMARY
  pair "dev128-$m-$u-B-A" "$R/$A/evals/$u/$m" "$R/$LB/evals/$u/$m" A,B
done
pair "ab512-greedy-off-$u-B-A" "$R/$A/evals/endpoint/$u-ab512-greedy-off" \
  "$R/$LB/evals/endpoint/$u-ab512-greedy-off" A,B                              # CONFIRMATORY
for mu in 2764 3064; do                                                        # macro adoption: A (and B) vs M
  mp=$(printf 'u%07d' "$mu")
  pair "dev128-greedy-off-$u-A-vs-M$mu" "$R/$M/evals/$mp/greedy-off" "$R/$A/evals/$u/greedy-off" "M$mu,A"
  pair "dev128-greedy-off-$u-B-vs-M$mu" "$R/$M/evals/$mp/greedy-off" "$R/$LB/evals/$u/greedy-off" "M$mu,B"
done
python3 - "$R/cmp" "$N" <<'PY'
import json, sys
from pathlib import Path
cmp, N = Path(sys.argv[1]), int(sys.argv[2]); u = f"u{N:07d}"
def get(name):
    p = cmp / f"{name}.json"
    return json.loads(p.read_text()) if p.exists() else None
def pr(name):
    r = get(name)
    if not r:
        return None
    q = r["pairs"][0]; lo, hi = q["t_ci95"]
    return q["delta_mean"], lo, hi, r["arms"]
def line(name, what):
    x = pr(name)
    if x is None:
        print(f"{what}: n/a ({name} missing)"); return None
    d, lo, hi, arms = x
    a = "; ".join(f"{s['label']} prog {s['progress_mean']:.3f} clear {s['clear_rate']:.3f} cyc-plays {s['plays_with_cycle']}"
                  for s in arms)
    print(f"{what}: delta {d:+.4f} [95% t {lo:+.4f}, {hi:+.4f}] | {a}"); return x
p = line(f"dev128-greedy-off-{u}-B-A", "PRIMARY   dev-128 greedy-off B-A")
line(f"dev128-greedy-on-{u}-B-A", "          dev-128 greedy-on  B-A")
c = line(f"ab512-greedy-off-{u}-B-A", "CONFIRM   ab-512  greedy-off B-A")
m = line(f"dev128-greedy-off-{u}-A-vs-M3064", "MACRO     A(uN) - M(u3064) dev-128 greedy-off")
line(f"dev128-greedy-off-{u}-A-vs-M2764", "          A(uN) - M(u2764) dev-128 greedy-off")
line(f"dev128-greedy-off-{u}-B-vs-M3064", "          B(uN) - M(u3064) dev-128 greedy-off")
hist = cmp / "rules.json"
st = json.loads(hist.read_text()) if hist.exists() else {}
st[str(N)] = {"B_A_hi": p[2] if p else None, "B_A_lo": p[1] if p else None, "A_M3064_hi": m[2] if m else None}
hist.write_text(json.dumps(st, indent=1, sort_keys=True))
if N in (3564, 4064):
    a, b = st.get("3564", {}).get("A_M3064_hi"), st.get("4064", {}).get("A_M3064_hi")
    if a is not None and b is not None:
        print("RULE revert (macros): " + ("FIRES - A below M u3064 at u3564 and u4064 (CI upper < 0): stop A and B, "
              "start A0 / B0 with MACROS=none" if a < 0 and b < 0 else "does not fire: keep macros"))
    else:
        print("RULE revert (macros): waiting for both u3564 and u4064")
pts = sorted(int(k) for k, v in st.items() if int(k) >= 4064 and v.get("B_A_hi") is not None)
harm = any(st[str(a2)]["B_A_hi"] < 0 and st[str(b2)]["B_A_hi"] < 0 for a2, b2 in zip(pts, pts[1:]))
print("RULE harm stop (B): " + ("FIRES - two consecutive points from u4064 with B-A CI entirely below 0: stop B"
      if harm else "does not fire"))
if N == int(__import__('os').environ.get('LAST', 7664)) and p:
    helps = p[1] > 0 and c is not None and c[0] > 0          # dev CI above 0; ab-512 same sign (its CI then not below 0)
    hurts = p[2] < 0 and c is not None and c[0] < 0
    print("OFFICIAL u7664 B-A: " + ("harvest helps" if helps else "harvest hurts" if hurts else
          "no detectable difference at +600 M steps"))
for arm in (get(f"dev128-greedy-off-{u}-B-A") or {}).get("arms", []):
    ok = arm["progress_mean"] >= 0.60 and (arm["clear_rate"] or 0) >= 0.10
    print(f"GOAL {arm['label']} {u} dev-128 greedy-off: progress {arm['progress_mean']:.3f} (>= 0.60), clear "
          f"{arm['clear_rate']:.3f} (>= 0.10): {'MET (confirm on ab-512)' if ok else 'not met'}")
PY
