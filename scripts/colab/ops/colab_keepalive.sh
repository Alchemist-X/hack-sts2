#!/bin/bash
# colab_keepalive.sh — runs on the Tokyo VM, independent of the Mac.
# Every INTERVAL s, for each session listed in ~/sts2keep/sessions.txt (one per line: NAME DRIVER_LOG):
#   1. ping the kernel (colab exec) — GPU runtimes are reclaimed ~20 min after the last kernel activity;
#   2. check whether the driver has finished (its log's last line ends with ': done' or is 'sp2: done');
#      when it has, stop the runtime (colab stop) so it no longer bills, and drop it from the list.
# Hardened 2026-11-05: timeout -k (SIGKILL after grace), output captured through a file (an orphaned child holding the
# pipe can no longer block the loop), one immediate retry on a failed ping.
# Logs to ~/sts2keep/keepalive.log. Stop the loop by removing ~/sts2keep/RUN.
export PATH=$HOME/.local/bin:$PATH
I=${1:-300}; D=$HOME/sts2keep; touch $D/RUN
ping() {   # NAME LOG -> prints "ping-ok|<last log line>" on success
  local tmp=$D/.ping.$1
  timeout -k 5 90 colab --auth=adc exec -s "$1" --timeout 60 > "$tmp" 2>&1 <<EOP
import subprocess
p = subprocess.run(["bash", "-lc", "tail -1 $2 2>/dev/null"], capture_output=True, text=True)
print("ping-ok|" + p.stdout.strip()[-120:])
EOP
  cat "$tmp"
}
while [ -f $D/RUN ]; do
  while read -r S LOG; do
    [ -z "$S" ] && continue
    out=$(ping "$S" "$LOG")
    echo "$out" | grep -q "ping-ok" || { sleep 10; out=$(ping "$S" "$LOG"); }
    ts=$(date -u +%FT%TZ)
    if echo "$out" | grep -q "ping-ok"; then
      last=$(echo "$out" | grep -o "ping-ok|.*" | cut -d'|' -f2-)
      echo "$ts $S ping ok | $last" >> $D/keepalive.log
      if echo "$last" | grep -qE ": done$|sp2: done"; then
        echo "$ts $S driver done -> stopping runtime" >> $D/keepalive.log
        timeout -k 5 120 colab --auth=adc stop -s "$S" >> $D/keepalive.log 2>&1
        grep -v "^$S " $D/sessions.txt > $D/sessions.tmp; mv $D/sessions.tmp $D/sessions.txt
      fi
    else
      echo "$ts $S ping FAILED (after retry): $(echo "$out" | tail -1 | cut -c1-160)" >> $D/keepalive.log
    fi
  done < $D/sessions.txt
  sleep "$I"
done
