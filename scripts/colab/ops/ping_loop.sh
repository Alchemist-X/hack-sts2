#!/bin/bash
# ping_loop.sh SESSION [INTERVAL_S=300] — keep the Colab kernel active (GPU runtimes were reclaimed ~20 min after the
# last kernel activity, twice today). Each ping is bounded by a 90 s alarm; two consecutive failures write ALERT.
# ALERT-<SESSION> files go to $STS2_OPS_DIR (default: this script's directory).
S=$1; I=${2:-300}; LOG_DIR=${STS2_OPS_DIR:-$(cd "$(dirname "$0")" && pwd)}; fails=0
while true; do
  ts=$(date -u +%FT%TZ)
  if out=$(perl -e 'alarm 90; exec @ARGV' colab --auth=adc exec -s "$S" --timeout 60 <<<'print("ping-ok")' 2>&1) && echo "$out" | grep -q ping-ok; then
    echo "$ts ping ok"; fails=0; rm -f "$LOG_DIR/ALERT-$S"
  else
    fails=$((fails+1)); echo "$ts ping FAILED ($fails): $(echo "$out" | tail -1 | cut -c1-160)"
    [ $fails -ge 2 ] && echo "$ts $S ping failed twice" > "$LOG_DIR/ALERT-$S"
  fi
  sleep "$I"
done
