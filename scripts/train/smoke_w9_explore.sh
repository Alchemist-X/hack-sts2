set -e
C="--preset smoke --envs 32 --rollout 16 --minibatch 256 --epochs 1 --device cpu --threads 4 --torch-threads 4 --max-updates 3 --log-every 1 --ckpt-every 1 --characters SILENT --ascension 10 --start-saves none --init-seed 1 --sample-seed 1 --env-seed 1 --floor-step-cap 80 --cycle-cost 0.01"
T="uv run --project sim/tools/train sts2train train"
rm -rf tmp/smk-w9; mkdir -p tmp/smk-w9
$T $C --out tmp/smk-w9/pool --step-cap 3000 --start-mode pool --pool 64 > tmp/smk-w9/pool.out 2>&1
$T $C --out tmp/smk-w9/stream --step-cap 3000 --start-mode stream --seed-stream-prefix SPX > tmp/smk-w9/stream.out 2>&1
$T $C --out tmp/smk-w9/stream-short --step-cap 120 --start-mode stream --seed-stream-prefix SPY > tmp/smk-w9/stream-short.out 2>&1
$T $C --out tmp/smk-w9/explore --step-cap 3000 --start-mode stream --seed-stream-prefix SPX --macros rest,shop --explore-kinds ChooseRestOption,BuyEntry --explore-eps 0.3 > tmp/smk-w9/explore.out 2>&1
$T $C --out tmp/smk-w9/explore-long --step-cap 3000 --start-mode stream --seed-stream-prefix SPX --macros rest,shop --explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --rollout 160 --minibatch 1024 > tmp/smk-w9/explore-long.out 2>&1
$T $C --out tmp/smk-w9/explore-harvest --step-cap 3000 --start-mode stream --seed-stream-prefix SPZ --macros rest,shop --explore-kinds ChooseRestOption,BuyEntry --explore-eps 0.3 --explore-logp decoupled --harvest boss:4,elite:4,act2:4 --rollout 64 > tmp/smk-w9/explore-harvest.out 2>&1
$T $C --out tmp/smk-w9/explore-overlap --step-cap 3000 --start-mode stream --seed-stream-prefix SPX --macros rest,shop --explore-kinds ChooseRestOption,BuyEntry --explore-eps 0.3 --overlap double > tmp/smk-w9/explore-overlap.out 2>&1
sim/tools/train/.venv/bin/python - <<'PY'
import json
for d in ("pool","stream","stream-short","explore","explore-long","explore-harvest","explore-overlap"):
    s=json.load(open(f"tmp/smk-w9/{d}/summary.json"))
    rows=[json.loads(l) for l in open(f"tmp/smk-w9/{d}/metrics.jsonl")]
    print(d, s["weights_hash"], "explore", s.get("explore"), "sim_error", s["sim_error"], "harvest" , (s.get("harvest") or {}).get("captured"))
    for r in rows:
        ks={k:v for k,v in r.items() if k.startswith("explore")}
        if ks: print("  u",r["update"], json.dumps(ks))
PY
