#!/bin/bash
# r2_compare.sh N   (docs/round2-plan-2026-11-05.md "预注册判据" / "读结果"): the round-2 comparisons at update uN
# (segment ends 8164 8664 9164; u9164 = the pre-registered point) of line C (runs/w4-c, --macros all) against the
# control D (runs/w4-d, --macros rest,shop), plus A's fork point u7664 as a reference, and the death profile of both
# lines' dev-128 greedy-off evals. Run like w3m_compare.sh: on a VM with the bundle installed (sts2train importable),
# never on the Mac. Pulls the eval dirs (no checkpoints) of C, D and A from GCS first (FETCH=0 skips that: dirs
# already local). Writes $R/cmp/r2-*.{txt,json,md}; prints one line per comparison and the rule reading.
#   env: R=/content/sts2/runs  C=w4-c  D=w4-d  A=w3m-a  AU=7664  STS=/content/sts2  BUCKET  FETCH=1
set -u
N=$1; R=${R:-/content/sts2/runs}; LC=${C:-w4-c}; LD=${D:-w4-d}; A=${A:-w3m-a}; AU=${AU:-7664}
STS=${STS:-/content/sts2}; BUCKET=${BUCKET:-gomoku-zero-20260816-ai-sts2train}
EV="python3 -m sts2train.cli"
HERE="$(cd "$(dirname "$0")" && pwd)"
if [ "${FETCH:-1}" = 1 ]; then
  for run in "$LC" "$LD" "$A"; do
    python3 "$HERE/w3m_fetch.py" --key "$STS/gcs-sts2-ckpt.json" --bucket "$BUCKET" \
      --prefix "runs/$run/evals" --dest "$R/$run/evals"
  done
fi
mkdir -p "$R/cmp"
u=$(printf 'u%07d' "$N"); au=$(printf 'u%07d' "$AU")
pair() {   # NAME DIR_A DIR_B LABELS   (delta = B - A)
  [ -f "$2/report.json" ] && [ -f "$3/report.json" ] || { echo "skip $1: missing $2 or $3"; return 0; }
  $EV compare "$2" "$3" --labels "$4" --json "$R/cmp/$1.json" > "$R/cmp/$1.txt" 2>&1 || echo "compare $1 failed"
}
for m in greedy-off greedy-on; do                                              # dev-128 greedy-off = PRIMARY
  pair "r2-dev128-$m-$u-C-D" "$R/$LD/evals/$u/$m" "$R/$LC/evals/$u/$m" D,C
done
pair "r2-ab512-greedy-off-$u-C-D" "$R/$LD/evals/endpoint/$u-ab512-greedy-off" \
  "$R/$LC/evals/endpoint/$u-ab512-greedy-off" D,C                              # AUXILIARY (segment ends)
pair "r2-ab512-greedy-on-$u-C-D" "$R/$LD/evals/endpoint/$u-ab512-greedy-on" \
  "$R/$LC/evals/endpoint/$u-ab512-greedy-on" D,C                               # u9164 only (skipped elsewhere)
for L in C D; do                                                               # REFERENCE: each line vs the fork
  [ "$L" = C ] && run=$LC || run=$LD
  pair "r2-dev128-greedy-off-$u-$L-vs-A$AU" "$R/$A/evals/$au/greedy-off" "$R/$run/evals/$u/greedy-off" "A$AU,$L"
done
# death profile of both lines' dev-128 greedy-off evals (+ the fork point when present): stdlib, python3 -I
DP=(); for x in "C=$R/$LC/evals/$u/greedy-off" "D=$R/$LD/evals/$u/greedy-off" "A$AU=$R/$A/evals/$au/greedy-off"; do
  [ -d "${x#*=}/games" ] && DP+=("$x")
done
if [ ${#DP[@]} -gt 0 ]; then
  python3 -I "$HERE/../analysis/death_profile.py" "${DP[@]}" --json "$R/cmp/r2-death-$u.json" \
    > "$R/cmp/r2-death-$u.md" 2>&1 && echo "death profile -> $R/cmp/r2-death-$u.md (${DP[*]%%=*})" \
    || echo "death_profile failed (see $R/cmp/r2-death-$u.md)"
else
  echo "skip death profile: no dev-128 greedy-off dirs at $u"
fi
python3 - "$R/cmp" "$N" "$AU" <<'PY'
import json, sys
from pathlib import Path
cmp, N, AU = Path(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]); u = f"u{N:07d}"
def get(name):
    p = cmp / f"{name}.json"
    return json.loads(p.read_text()) if p.exists() else None
def line(name, what):
    r = get(name)
    if not r:
        print(f"{what}: n/a ({name} missing)"); return None
    q = r["pairs"][0]; lo, hi = q["t_ci95"]; blo, bhi = q["bootstrap_ci95"]
    a = "; ".join(f"{s['label']} prog {s['progress_mean']:.3f} clear {s['clear_rate']:.3f} cyc-plays {s['plays_with_cycle']}"
                  for s in r["arms"])
    print(f"{what}: delta {q['delta_mean']:+.4f} [95% t {lo:+.4f}, {hi:+.4f}] [95% boot {blo:+.4f}, {bhi:+.4f}] | {a}")
    return q
p = line(f"r2-dev128-greedy-off-{u}-C-D", "PRIMARY   dev-128 greedy-off C-D")
line(f"r2-dev128-greedy-on-{u}-C-D", "          dev-128 greedy-on  C-D")
line(f"r2-ab512-greedy-off-{u}-C-D", "AUX       ab-512  greedy-off C-D")
line(f"r2-ab512-greedy-on-{u}-C-D", "          ab-512  greedy-on  C-D")
line(f"r2-dev128-greedy-off-{u}-C-vs-A{AU}", f"REF       C(uN) - A(u{AU}) dev-128 greedy-off")
line(f"r2-dev128-greedy-off-{u}-D-vs-A{AU}", f"          D(uN) - A(u{AU}) dev-128 greedy-off")
if p:   # plan "预注册判据" 1, on the bootstrap interval; only official at u9164
    lo, hi = p["bootstrap_ci95"]
    v = ("ADOPT --macros all (CI entirely > 0)" if lo > 0 else
         "DO NOT ADOPT (CI entirely < 0): main line keeps rest,shop" if hi < 0 else
         "ADOPT if MACRO_FALLBACK < 1% of prompt decisions and SIM_ERROR = 0 (non-inferior: CI lower > -0.01); "
         "check both in C's training logs" if lo > -0.01 else
         "NO CONCLUSION: rerun with an independent --sample-seed")
    print(f"RULE {'(official) ' if N == 9164 else '(interim, official at u9164) '}{u} C-D: {v}")
PY
