#!/bin/bash
# mirror_loop.sh LOCAL_DIR [INTERVAL_S=600] — pull only the small run files the dashboard needs
# (metrics.jsonl, evals.jsonl, status.json, config.json, train.log) for every sp*-* / w3m-* / w4-* run in the bucket
# (the grep below; w9-* runs are not matched); each copy bounded by an alarm.
# Checkpoints, per-game eval dirs, cycles.jsonl and episodes.jsonl stay in the bucket (fetch on demand).
L=$1; I=${2:-600}; B=gs://gomoku-zero-20260816-ai-sts2train/runs
while true; do
  ts=$(date -u +%FT%TZ); ok=""
  for r in $(perl -e 'alarm 60; exec @ARGV' gcloud storage ls "$B/" 2>/dev/null | grep -oE '(sp[0-9]+[a-z]*|w3m|w4)-[a-z0-9]+' | sort -u); do
    mkdir -p "$L/$r"
    for f in metrics.jsonl evals.jsonl status.json config.json train.log; do
      perl -e 'alarm 120; exec @ARGV' gcloud storage cp -q "$B/$r/$f" "$L/$r/$f.tmp" 2>/dev/null && mv -f "$L/$r/$f.tmp" "$L/$r/$f"
    done
    ok="$ok $r=$(wc -l < "$L/$r/metrics.jsonl" 2>/dev/null | tr -d ' ')"
  done
  echo "$ts cp ok:$ok"
  sleep "$I"
done
