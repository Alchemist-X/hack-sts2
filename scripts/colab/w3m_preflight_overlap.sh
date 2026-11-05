#!/bin/bash
# w3m_preflight_overlap.sh BUNDLE SRC CKPT [OUT]   (docs/w3m-overnight-plan-2026-11-04.md §3; optional)
#
# Decides whether line A may run --overlap double. Run it on an idle A100 (GPU B in its 22:30-00:15 EST gap, or GPU A
# before line A's first session); ~5 min bench + ~3-4 min compile check.
#   BUNDLE  the unpacked w3m bundle (sts2train-<sha>, AOT 7aaa846a6a35); its wheels must be installed (run_train.sh
#           does that; or: pip install --no-deps --force-reinstall $BUNDLE/wheels/*.whl)
#   SRC     a source tree of the same commit (bench/overlap_bench.py is not in the wheel), e.g. git archive unpacked
#   CKPT    any local checkpoint of the line's architecture (e.g. M's pinned-0002764.pt pulled from GCS)
#   OUT     scratch dir (default /content/sts2/preflight-overlap)
#
# Pass rule (fixed in advance, w3 plan §3.3): OVERLAP=double iff
#   (1) bench infer_ratio r_inf <= 1.4, (2) the bench's double t_total_s < off's (measured total_speedup > 1), and
#   (3) the 3-update real-recipe check logs "compiled rollout forward vs eager: ... OK" and writes t_overlap_wait_s.
# Otherwise OVERLAP=off. The verdict goes to OUT/decision.txt; it is decided once and never changed within line A.
set -u
B=$1; SRC=$2; CK=$3; OUT=${4:-/content/sts2/preflight-overlap}
ARCH=${ARCH:-192,384,3,6,4}      # the line's model (d, s, depth, heads, ff_mult); the w3 plan's quick form: --preset small
mkdir -p "$OUT"
export STS2VEC_AOT_LIB="$B/lib/Sts2Sim.Vec.so" STS2VEC_SEEDSETS="$B/seedsets"
echo "== bench (off vs double, 2 interleaved repeats, 6 updates each, 2048 envs x 64)"
python3 "$SRC/sim/tools/train/bench/overlap_bench.py" --device cuda --envs 2048 --rollout 64 --updates 6 \
    --arch "$ARCH" --threads 10 --torch-threads 2 --minibatch 4096 --epochs 1 --repeats 2 --out "$OUT/bench" \
    > "$OUT/bench.out" 2>&1 || { tail -30 "$OUT/bench.out"; echo "bench failed: OVERLAP=off" | tee "$OUT/decision.txt"; exit 1; }
echo "== compile check: the real line-A recipe, --overlap double, 3 updates from a copy of $CK"
U=$(python3 -c "import sys,torch; print(int(torch.load(sys.argv[1], map_location='cpu', weights_only=False)['updates']))" "$CK")
rm -rf "$OUT/run" "$OUT/src"; mkdir -p "$OUT/src/checkpoints"; cp "$CK" "$OUT/src/checkpoints/pinned-$(printf %07d "$U").pt"
SYNC=0 EVALS=0 FORK_U=$U ENDS=$((U + 3)) LAST=$((U + 4600)) WAIT_RUN=/nonexistent \
  bash "$SRC/scripts/colab/w3m_line.sh" "$B" "$OUT/run" a "$OUT/src/checkpoints/pinned-$(printf %07d "$U").pt" double \
  > "$OUT/compile.out" 2>&1
python3 - "$OUT" <<'PY'
import json, sys
from pathlib import Path
out = Path(sys.argv[1])
b = json.loads((out / "bench" / "bench.json").read_text())
m = b.get("measured") or {}
r_inf, tot = m.get("infer_ratio"), m.get("total_speedup")
log = (out / "run" / "train.log").read_text() if (out / "run" / "train.log").exists() else ""
comp = any("compiled rollout forward vs eager" in l and "OK" in l for l in log.splitlines())
rows = [json.loads(l) for l in open(out / "run" / "metrics.jsonl")] if (out / "run" / "metrics.jsonl").exists() else []
wait = [r.get("t_overlap_wait_s") for r in rows]
roll = [r.get("t_rollout_s") for r in rows]
ok = r_inf is not None and r_inf <= 1.4 and tot is not None and tot > 1.0 and comp and len(rows) >= 1 and None not in wait
verdict = "double" if ok else "off"
text = (f"r_inf {r_inf} (<= 1.4), total_speedup {tot} (> 1), compile_ok {comp}, updates {len(rows)}, "
        f"t_overlap_wait_s {wait}, t_rollout_s {roll}, a100_projection {b.get('a100_projection')}\nOVERLAP={verdict}\n")
(out / "decision.txt").write_text(text)
print(text, end="")
PY
