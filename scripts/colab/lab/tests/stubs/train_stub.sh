#!/bin/bash
# train_stub.sh RUN_DIR THREADS ENVS FLAGS... --max-updates END   (stands in for `bash $BUNDLE/run_train.sh` via TRAIN=)
# Records its flags in RUN_DIR/train_calls.log, then "trains" to END: status.json, metrics/evals rows, ckpt + pinned
# checkpoints (JSON, read back by stubs/pymods/torch.py). Knobs (env):
#   STUB_MISMATCH_ONCE_AT=END  the first session to that END writes a compile-gate MISMATCH line and exits 1
#   STUB_NO_PIN_RUN=RUN        for that run (basename): no pinned checkpoint (the driver then stops: rc 1)
#   STUB_TRAIN_SLEEP=S         seconds per session (default 0.3; /bin/sleep, the PATH shim no-ops plain `sleep`)
RUN=$1; shift 3
END=""; prev=""
for a in "$@"; do [ "$prev" = --max-updates ] && END=$a; prev=$a; done
echo "$*" >> "$RUN/train_calls.log"
mkdir -p "$RUN/checkpoints"
/bin/sleep "${STUB_TRAIN_SLEEP:-0.3}"
if [ -n "${STUB_MISMATCH_ONCE_AT:-}" ] && [ "$END" = "$STUB_MISMATCH_ONCE_AT" ] && [ ! -f "$RUN/.mismatch_done" ]; then
  touch "$RUN/.mismatch_done"
  echo "compile check: compiled minibatch update vs eager: loss rel 2.3e-02 > 1e-02 MISMATCH" >> "$RUN/train.log"
  exit 1
fi
echo "stub session -> update $END" >> "$RUN/train.log"
printf '{"update": %s, "state": "finished", "stop_reason": "max_updates"}\n' "$END" > "$RUN/status.json"
printf '{"update": %s}\n' "$END" >> "$RUN/metrics.jsonl"
printf '{"update": %s, "mode": "greedy-off"}\n' "$END" >> "$RUN/evals.jsonl"
printf '{"updates": %s, "weights_hash": "stub"}\n' "$END" > "$(printf '%s/checkpoints/ckpt-%07d.pt' "$RUN" "$END")"
if [ "${STUB_NO_PIN_RUN:-}" != "$(basename "$RUN")" ]; then
  printf '{"updates": %s, "weights_hash": "stub"}\n' "$END" > "$(printf '%s/checkpoints/pinned-%07d.pt' "$RUN" "$END")"
fi
exit 0
