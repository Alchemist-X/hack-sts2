#!/bin/bash
# dash_refresh.sh [OPS_DIR] — one refresh of the dashboard's dataset files (no upload).
#   1. reads the Colab balance and burn rate (colab --auth=adc usage), appends them to OPS_DIR/balance_log.jsonl
#   2. pulls segment-end eval reports not cached yet from the bucket into OPS_DIR/endpoints/<run>/<name>/
#      (report.json / repeats.json only; skip with DASH_NO_PULL=1)
#   3. runs dash_data.py with the repo's ledger / line events / system files and the ops dir's state / events / mirror
#   4. prints the out files with their sizes
# The upload of OUT to the Dashboard artifact stays a separate step (README "Refresh (v2)").
# OPS_DIR (default $STS2_OPS_DIR) holds dash_state.json, dash_events.jsonl, out/, endpoints/; the mirror is
# OPS_DIR/mirror, else OPS_DIR/../mirror. Every path can be overridden: DASH_STATE, DASH_EVENTS, DASH_OUT, DASH_MIRROR,
# DASH_ENDPOINTS, DASH_LINES, DASH_LINE_EVENTS, DASH_SYSTEM, DASH_RESULTS, DASH_BALANCE_LOG.
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$HERE/../.." && pwd)
OPS=${1:-${STS2_OPS_DIR:-}}
[ -n "$OPS" ] && [ -d "$OPS" ] || { echo "usage: $0 OPS_DIR  (or set STS2_OPS_DIR)" >&2; exit 2; }
OPS=$(cd "$OPS" && pwd)
STATE=${DASH_STATE:-$OPS/dash_state.json}
EVENTS=${DASH_EVENTS:-$OPS/dash_events.jsonl}
OUT=${DASH_OUT:-$OPS/out}
if [ -n "${DASH_MIRROR:-}" ]; then MIRROR=$DASH_MIRROR
elif [ -d "$OPS/mirror" ]; then MIRROR=$OPS/mirror
else MIRROR=$(dirname "$OPS")/mirror; fi
ENDPOINTS=${DASH_ENDPOINTS:-$OPS/endpoints}
LINES=${DASH_LINES:-$REPO/docs/results/lines-silent-a10.json}
LINE_EVENTS=${DASH_LINE_EVENTS:-$REPO/docs/results/line-events-silent-a10.jsonl}
SYSTEM=${DASH_SYSTEM:-$HERE/system-silent-a10.json}
RESULTS=${DASH_RESULTS:-$(ls -1 "$REPO"/docs/results/evals-silent-a10-*.json 2>/dev/null | tail -1)}
BUCKET=gs://gomoku-zero-20260816-ai-sts2train/runs
BLOG=${DASH_BALANCE_LOG:-$OPS/balance_log.jsonl}
for f in "$LINES" "$LINE_EVENTS" "$SYSTEM"; do [ -f "$f" ] || echo "note: $f missing (its dataset will be empty or computed only)" >&2; done

# 1. balance ("colab" is often a shell alias; resolve the binary)
COLAB=$(command -v colab 2>/dev/null || true)
[ -z "$COLAB" ] && for c in "$HOME/.local/bin/colab" /opt/homebrew/bin/colab /usr/local/bin/colab; do [ -x "$c" ] && COLAB=$c && break; done
BAL=-; RATE=-
if [ -n "$COLAB" ]; then
  U=$(perl -e 'alarm 60; exec @ARGV' "$COLAB" --auth=adc usage 2>/dev/null)
  B=$(printf '%s\n' "$U" | grep -i balance | grep -oE '[0-9]+(\.[0-9]+)?' | head -1)
  R=$(printf '%s\n' "$U" | grep -i 'usage rate' | grep -oE '[0-9]+(\.[0-9]+)?' | head -1)
  [ -n "$B" ] && BAL=$B
  [ -n "$R" ] && RATE=$R
fi
if [ "$BAL" != "-" ]; then
  printf '{"time": "%s", "balance": %s, "rate": %s}\n' "$(date -u +%FT%TZ)" "$BAL" "${R:-null}" >> "$BLOG"
else
  echo "warning: Colab balance not read (colab CLI missing or failed); units_used will be empty" >&2
fi
echo "balance: $BAL units, rate: $RATE units/h"

# 2. segment-end reports (incremental; each gcloud call bounded by an alarm)
if [ -z "${DASH_NO_PULL:-}" ] && command -v gcloud >/dev/null 2>&1; then
  pulled=0
  for d in $(perl -e 'alarm 120; exec @ARGV' gcloud storage ls "$BUCKET/*/evals/endpoint/" 2>/dev/null | grep -E '/evals/endpoint/u[0-9]+[^/]*/$'); do
    run=$(printf '%s' "$d" | sed -E 's#.*/runs/([^/]+)/evals/endpoint/.*#\1#')
    name=$(basename "$d")
    dst=$ENDPOINTS/$run/$name
    [ -f "$dst/report.json" ] || [ -f "$dst/repeats.json" ] && continue
    mkdir -p "$dst"
    for f in report.json repeats.json; do
      perl -e 'alarm 120; exec @ARGV' gcloud storage cp -q "$d$f" "$dst/$f.tmp" 2>/dev/null && mv -f "$dst/$f.tmp" "$dst/$f" && pulled=$((pulled+1))
      rm -f "$dst/$f.tmp"
    done
  done
  echo "endpoint reports pulled: $pulled (cache $ENDPOINTS)"
fi

# 3. build
mkdir -p "$OUT"
python3 -I "$HERE/dash_data.py" "$STATE" "$EVENTS" "$OUT" "$BAL" \
  --lines "$LINES" --line-events "$LINE_EVENTS" --system "$SYSTEM" \
  --mirror "$MIRROR" --endpoints "$ENDPOINTS" --results "${RESULTS:-}" --balance-log "$BLOG" --units-rate "$RATE" \
  || { echo "dash_data.py failed" >&2; exit 1; }

# 4. out files
echo "out: $OUT"
for n in status plan timeline metrics evals lines line_events throughput system; do
  f=$OUT/$n.json
  [ -f "$f" ] && printf '  %-18s %9d B\n' "$n.json" "$(wc -c < "$f" | tr -d ' ')"
done
