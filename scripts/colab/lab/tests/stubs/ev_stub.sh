#!/bin/bash
# ev_stub.sh eval --out OUT ARGS...   (stands in for `python3 -m sts2train.cli` via EV=)
# Writes OUT/report.json (OUT/repeats.json with --repeats) and appends its args to OUT/../../../ev_calls.log.
# STUB_EV_SKIP=SUBSTR: an OUT containing SUBSTR fails (rc 1) without writing a report.
shift
OUT=""; REP=0; prev=""
for a in "$@"; do [ "$prev" = --out ] && OUT=$a; [ "$a" = --repeats ] && REP=1; prev=$a; done
[ -n "$OUT" ] || exit 2
RUN=$(dirname "$(dirname "$(dirname "$OUT")")")
echo "$*" >> "$RUN/ev_calls.log"
if [ -n "${STUB_EV_SKIP:-}" ] && case "$OUT" in *"$STUB_EV_SKIP"*) true ;; *) false ;; esac; then exit 1; fi
mkdir -p "$OUT"
if [ $REP = 1 ]; then echo '{"repeats": 3}' > "$OUT/repeats.json"; else echo '{"win_rate": 0.1}' > "$OUT/report.json"; fi
