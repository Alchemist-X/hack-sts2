set -e
# wave 10: default-path hashes (pool / stream / step-cap, same flags as smoke_w9_explore.sh) + the Travel:Elite /
# ProceedRewards:skipcard explore smoke (stream, rollout 160, eps 0.3; 64 envs x 12 updates: an untrained policy
# rarely reaches the rows with elites, 3 updates x 32 envs saw one elite decision)
C="--preset smoke --envs 32 --rollout 16 --minibatch 256 --epochs 1 --device cpu --threads 4 --torch-threads 4 --max-updates 3 --log-every 1 --ckpt-every 1 --characters SILENT --ascension 10 --start-saves none --init-seed 1 --sample-seed 1 --env-seed 1 --floor-step-cap 80 --cycle-cost 0.01"
T="uv run --project sim/tools/train sts2train train"
rm -rf tmp/smk-w10; mkdir -p tmp/smk-w10
$T $C --out tmp/smk-w10/pool --step-cap 3000 --start-mode pool --pool 64 > tmp/smk-w10/pool.out 2>&1
$T $C --out tmp/smk-w10/stream --step-cap 3000 --start-mode stream --seed-stream-prefix SPX > tmp/smk-w10/stream.out 2>&1
$T $C --out tmp/smk-w10/stream-short --step-cap 120 --start-mode stream --seed-stream-prefix SPY > tmp/smk-w10/stream-short.out 2>&1
$T $C --out tmp/smk-w10/travel --step-cap 3000 --start-mode stream --seed-stream-prefix SPX --explore-kinds Travel:Elite,ProceedRewards:skipcard --explore-eps 0.3 --rollout 160 --minibatch 1024 --envs 64 --max-updates 12 > tmp/smk-w10/travel.out 2>&1
sim/tools/train/.venv/bin/python - <<'PY'
import json
for d in ("pool", "stream", "stream-short", "travel"):
    s = json.load(open(f"tmp/smk-w10/{d}/summary.json"))
    rows = [json.loads(l) for l in open(f"tmp/smk-w10/{d}/metrics.jsonl")]
    print(d, s["weights_hash"], "explore", s.get("explore"), "sim_error", s["sim_error"])
    for r in rows:
        ks = {k: v for k, v in r.items() if k.startswith("explore") and "_macro" not in k}
        if ks: print("  u", r["update"], json.dumps(ks))
    if d == "travel":
        for it in ("Travel:Elite", "ProceedRewards:skipcard"):
            st = sum(r[f"explore_drawn.{it}"] for r in rows)
            pr = sum(r[f"explore_present.{it}"] for r in rows)
            tk = sum(r[f"explore_taken.{it}"] for r in rows)
            pm = [r[f"explore_pmass.{it}"] for r in rows]
            print(f"  TOTAL {it}: present {pr} taken {tk} drawn {st} pmass/update {pm}")
            assert st > 0, it
PY
