#!/bin/bash
# mirror_loop.sh LOCAL_DIR [INTERVAL_S=600] — pull only the small run files the dashboard needs for
# EVERY run prefix under runs/ in the bucket (no name whitelist): metrics.jsonl, evals.jsonl, status.json,
# config.json, train.log, summary.json, pair.json, line_status.json (each when present) and evals/pairs/**.
# Two shallow bucket listings per pass decide what exists; only present files are copied.
# Every gcloud call is bounded by an alarm; every file lands via tmp + mv so readers never see partial files.
# Checkpoints, per-game eval dirs, cycles.jsonl and episodes.jsonl stay in the bucket (fetch on demand).
# DRY_RUN=1: list the bucket for real (read-only), print the cp commands instead of running them, one pass.
L=$1; I=${2:-600}; B=gs://gomoku-zero-20260816-ai-sts2train/runs
[ -n "$L" ] || { echo "usage: $0 LOCAL_DIR [INTERVAL_S]" >&2; exit 2; }
FILES="metrics.jsonl evals.jsonl status.json config.json train.log summary.json pair.json line_status.json"
gls() { perl -e 'alarm 90; exec @ARGV' gcloud storage ls "$@" 2>/dev/null; }
fetch() { # fetch SRC_URL DEST_PATH
  if [ -n "$DRY_RUN" ]; then echo "DRY: gcloud storage cp -q $1 $2.tmp && mv -f $2.tmp $2"; return 0; fi
  mkdir -p "$(dirname "$2")"
  perl -e 'alarm 120; exec @ARGV' gcloud storage cp -q "$1" "$2.tmp" 2>/dev/null && mv -f "$2.tmp" "$2"
}
while true; do
  ts=$(date -u +%FT%TZ); ok=""
  top=$(gls "$B/*/")                                  # every run's top-level objects and sub-prefixes
  pairs=$(gls "$B/*/evals/" | grep -E '/evals/pairs/$') # evals/ listed shallowly (it holds per-game dirs)
  for r in $(printf '%s\n' "$top" | sed -n "s#^$B/\([^/][^/]*\)/.*#\1#p" | sort -u); do
    [ -n "$DRY_RUN" ] || mkdir -p "$L/$r"
    for f in $FILES; do
      printf '%s\n' "$top" | grep -qxF "$B/$r/$f" && fetch "$B/$r/$f" "$L/$r/$f"
    done
    if printf '%s\n' "$pairs" | grep -qxF "$B/$r/evals/pairs/"; then
      for u in $(gls "$B/$r/evals/pairs/**"); do
        case "$u" in */) continue ;; esac
        fetch "$u" "$L/$r/${u#"$B/$r/"}"
      done
    fi
    ok="$ok $r=$({ wc -l < "$L/$r/metrics.jsonl"; } 2>/dev/null | tr -d ' ')"
  done
  echo "$ts cp ok:$ok"
  [ -n "$DRY_RUN" ] && break
  sleep "$I"
done
