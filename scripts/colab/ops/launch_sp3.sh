#!/bin/bash
# launch_sp3.sh: main line = continue C1 (cycle cost 0.01) from its u764 pinned checkpoint for +2300 updates (~300 M steps)
set -u
R=/content/sts2/runs/sp3-main; B=/content/sts2/sts2train-542a1e4eb8; DEV=silent-a10-dev-v1
mkdir -p $R/checkpoints
[ -f $R/checkpoints/ckpt-0000764.pt ] || cp /content/sts2/runs/sp2-c1/checkpoints/pinned-0000764.pt $R/checkpoints/ckpt-0000764.pt
eval "$(awk '/^FLAGS=\(/,/--log-every 1\)/' /content/sts2/sp2_serial.sh)"
cd $B
setsid nohup bash run_train.sh $R 12 2048 "${FLAGS[@]}" --max-updates 3064 --eval-at 1264,1764,2264,2764,3064 --cycle-cost 0.01 --keep-ckpts 60 --max-hours 6 > /content/sts2/runs/sp3-main.launch.log 2>&1 < /dev/null &
sleep 1
setsid nohup python3 /content/sts2/gcs_sync.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train --prefix runs --run $R --interval 60 --min-age 60 > /content/sts2/runs/sp3-main.gcs_sync.log 2>&1 < /dev/null &
sleep 3
echo "launched: $(pgrep -fc 'runs/sp3-main') procs"; date -u
