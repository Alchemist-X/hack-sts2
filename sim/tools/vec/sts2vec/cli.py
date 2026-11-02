"""sts2vec CLI (CONTRACTS.md §D6).

- ``sts2vec bench``  random-policy throughput: env-steps/s, ms per batch, obs MB per batch, GC mode, resets/s, status counts;
  the pool's fresh templates are warmed first and that time is printed separately (``--no-warm`` measures cold starts).
- ``sts2vec check``  library/schema/buffer consistency + determinism (two envs with identical config, threads 1 vs 8,
  seeded random policy, byte-identical buffers every step and equal state hashes at the end).
- ``sts2vec digest`` a JSON digest of a seeded random rollout (state hashes + observation bytes); two processes with the
  same arguments must print the same digest (cross-process determinism).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time

import numpy as np

from .env import VecEnv, random_actions
from .loader import load
from .pool import ALL_CHARACTERS, PoolSpec
from .schema import STATUS, SIGNAL_BUFFERS, Schema, status_counts


def _pool(args) -> PoolSpec:
    chars = tuple(c.strip() for c in args.characters.split(",") if c.strip())
    return PoolSpec(characters=chars, ascension=args.ascension, count=args.pool, seed_prefix=args.seed_prefix)


def _env(args, threads: int) -> VecEnv:
    return VecEnv(args.envs, threads=threads, step_cap=args.step_cap, seed_text=args.seed_text, content=args.content,
                  pool=_pool(args), dll=args.dll)


def _add_env_args(p: argparse.ArgumentParser, *, envs: int, steps: int, pool: int) -> None:
    p.add_argument("--envs", type=int, default=envs, help=f"number of env slots (default {envs})")
    p.add_argument("--steps", type=int, default=steps, help=f"batched steps (default {steps})")
    p.add_argument("--characters", default=",".join(ALL_CHARACTERS), help="fresh pool characters (comma separated)")
    p.add_argument("--pool", type=int, default=pool, help=f"fresh pool entries (default {pool})")
    p.add_argument("--ascension", type=int, default=0)
    p.add_argument("--seed-prefix", default="TRAIN")
    p.add_argument("--seed-text", default="VEC", help="config.seed_text (per-slot start sequences)")
    p.add_argument("--content", default="ported_only", choices=["ported_only", "all"])
    p.add_argument("--step-cap", type=int, default=3000)
    p.add_argument("--seed", type=int, default=0, help="random policy seed")


# ------------------------------------------------------------------------------------------------ bench

def cmd_bench(args) -> int:
    lib = load(args.dll)
    schema = Schema.from_json(lib.schema_json())
    print(f"library {lib.path} ({lib.mode}); {lib.version()}; GC {lib.gc_mode}")
    with _env(args, args.threads) as env:
        warmed, t_warm = 0, 0.0
        if args.warm:
            # create every fresh template now (sts2vec_pool_warm), so cold NewRun (~16 ms per entry) stays out of the
            # timed window; --no-warm leaves them to the first reset that uses each entry
            t0 = time.perf_counter()
            warmed = env.pool.warm()
            t_warm = time.perf_counter() - t0
        t0 = time.perf_counter()
        obs = env.reset()
        t_reset = time.perf_counter() - t0
        reset_status = env.status_counts()
        rng = np.random.default_rng(args.seed)
        actions = np.full(env.num_envs, -1, dtype=np.int32)
        for _ in range(args.warmup):
            obs, *_ = env.step(random_actions(rng, obs["cand_n"], actions))
        totals = {k: 0 for k in ["OK", *[n for n, b in STATUS.items() if b]]}
        resets = ended = wins = taken = 0
        native = 0.0
        t0 = time.perf_counter()
        for _ in range(args.steps):
            random_actions(rng, obs["cand_n"], actions)
            taken += int(np.count_nonzero(actions >= 0))
            t1 = time.perf_counter()
            obs, reward, term, trunc, status, info = env.step(actions)
            native += time.perf_counter() - t1
            for k, v in status_counts(status).items():
                totals[k] += v
            resets += int(np.count_nonzero(status & STATUS["RESET"]))
            ended += int(np.count_nonzero(term | trunc))
            wins += int(np.count_nonzero(reward > 0))
        dt = time.perf_counter() - t0
        steps = max(args.steps, 1)
        n = env.num_envs
        result = {
            "envs": n, "threads": args.threads, "steps": args.steps, "warmup": args.warmup, "policy": args.policy,
            "pool": len(env.pool), "mode": lib.mode, "gc": lib.gc_mode, "encoder_version": schema.encoder_version,
            "env_steps_per_s": n * args.steps / max(dt, 1e-9),
            "native_env_steps_per_s": n * args.steps / max(native, 1e-9),
            "ms_per_batch": dt / steps * 1e3, "native_ms_per_batch": native / steps * 1e3,
            "obs_mb_per_batch": env.buffers.observation_nbytes / 1e6, "buffers_mb": env.buffers.nbytes / 1e6,
            "warm": bool(args.warm), "warm_ms": t_warm * 1e3, "warmed_templates": warmed,
            "reset_ms": t_reset * 1e3, "resets_per_s": resets / max(dt, 1e-9), "episodes_ended": ended, "wins": wins,
            "actions_taken": taken, "status_counts": totals, "reset_status_counts": reset_status,
        }
    if args.json:
        print(json.dumps(result))
        return 0
    r = result
    print(f"envs={r['envs']} threads={r['threads']} steps={r['steps']} (+{r['warmup']} warmup) policy={r['policy']} "
          f"pool={r['pool']}")
    if r["warm"]:
        print(f"  pool warm: {r['warmed_templates']:,} templates in {r['warm_ms']:.0f} ms (before the timed window)")
    else:
        print("  pool warm: off (--no-warm: each fresh entry pays its NewRun at its first reset, inside the timed window)")
    print(f"  {r['env_steps_per_s']:,.0f} env-steps/s (native step only {r['native_env_steps_per_s']:,.0f}) | "
          f"{r['ms_per_batch']:.2f} ms/batch (native {r['native_ms_per_batch']:.2f}) | "
          f"{r['obs_mb_per_batch']:.1f} MB obs/batch")
    print(f"  reset {r['reset_ms']:.0f} ms | {r['resets_per_s']:,.0f} resets/s | episodes ended {r['episodes_ended']:,} "
          f"(wins {r['wins']:,}) | actions taken {r['actions_taken']:,}")
    print("  status counts (slot-steps): " + ", ".join(f"{k} {v:,}" for k, v in r["status_counts"].items() if v))
    bad = r["status_counts"]["SIM_ERROR"] + r["status_counts"]["CONTENT_GAP"]
    if bad:
        print(f"  WARNING: {bad} SIM_ERROR/CONTENT_GAP slot-steps (see VecEnv.slot_errors())")
    if r["actions_taken"] == 0:
        print("  note: cand_n was 0 in every slot (stub CandidateTable?) — only no-op steps were measured")
    return 0


# ------------------------------------------------------------------------------------------------ check

def _schema_problems(lib, schema: Schema, n: int) -> list[str]:
    problems = []
    if not schema.verify_version():
        problems.append("encoder_version is not XXH64 of the schema JSON body")
    if lib.version() != "sts2vec/" + schema.encoder_version:
        problems.append(f"version {lib.version()!r} != sts2vec/{schema.encoder_version}")
    for name in SIGNAL_BUFFERS + ("cand", "cand_n"):
        if name not in schema:
            problems.append(f"schema lacks buffer {name}")
    for b in schema.buffers:
        if b.fields is not None and (not b.shape or len(b.fields) != b.shape[-1]):
            problems.append(f"{b.name}: {len(b.fields)} fields but shape {b.shape}")
    for name in ("draw_counts", "discard_counts", "exhaust_counts"):
        if name in schema and schema[name].shape != (2 * schema.v_cards,):
            problems.append(f"{name}: shape {schema[name].shape} != (2 x V_cards = {2 * schema.v_cards},)")
    if "cand" in schema and schema["cand"].shape[0] != schema.caps.get("CANDIDATES"):
        problems.append("cand rows != CAP CANDIDATES")
    for kind, lst in schema.vocab.lists.items():
        if lst[:2] != ["", "?"]:
            problems.append(f"vocab {kind} does not start with '', '?'")
        if len(set(lst)) != len(lst):
            problems.append(f"vocab {kind} has duplicates")
    problems += schema.allocate(n).check()
    return problems


def _rollout_digest(env: VecEnv, steps: int, seed: int) -> dict:
    obs = env.reset()
    rng = np.random.default_rng(seed)
    hobs = hashlib.sha256()
    taken = 0
    for _ in range(steps):
        acts = random_actions(rng, obs["cand_n"])
        taken += int(np.count_nonzero(acts >= 0))
        env.step(acts)
        for name in env.schema.names:
            hobs.update(env.buffers[name].tobytes())
    hashes = env.state_hash()
    return {"state_hash_sha256": hashlib.sha256(hashes.tobytes()).hexdigest(), "obs_sha256": hobs.hexdigest(),
            "actions_taken": taken, "nonzero_hashes": int(np.count_nonzero(hashes)), "envs": env.num_envs,
            "steps": steps, "threads": env.threads, "encoder_version": env.schema.encoder_version}


def cmd_check(args) -> int:
    lib = load(args.dll)
    text = lib.schema_json()
    schema = Schema.from_json(text)
    print(f"library {lib.path} ({lib.mode})")
    print(f"  runtimeconfig {lib.runtimeconfig} | hostfxr {lib.hostfxr} | dotnet root {lib.dotnet_root} | GC {lib.gc_mode}")
    print(f"  version {lib.version()} | schema {len(text):,} bytes, {len(schema.buffers)} buffers, "
          f"{schema.bytes_per_env:,} bytes/env | vocab " +
          ", ".join(f"{k} {len(v)}" for k, v in schema.vocab.lists.items()))
    ok = True
    problems = _schema_problems(lib, schema, args.envs)
    print("schema/buffers: " + ("consistent" if not problems else "PROBLEMS"))
    for p in problems:
        print(f"  - {p}")
    ok &= not problems

    ta, tb = args.threads_a, args.threads_b
    with _env(args, ta) as a, _env(args, tb) as b:
        oa, ob = a.reset(), b.reset()
        rngs = (np.random.default_rng(args.seed), np.random.default_rng(args.seed))
        first_diff = None
        taken = 0
        for step in range(-1, args.steps):
            if step >= 0:
                acts_a = random_actions(rngs[0], oa["cand_n"])
                acts_b = random_actions(rngs[1], ob["cand_n"])
                taken += int(np.count_nonzero(acts_a >= 0))
                a.step(acts_a)
                b.step(acts_b)
            if first_diff is None:
                for name in schema.names:
                    if not np.array_equal(a.buffers[name], b.buffers[name]):
                        rows = np.flatnonzero((a.buffers[name] != b.buffers[name]).reshape(args.envs, -1).any(axis=1))
                        first_diff = (step, name, rows[:8].tolist())
                        break
        ha, hb = a.state_hash(), b.state_hash()
        counts = status_counts(a.status)
        slot_errors = a.slot_errors()
    same_hash = bool(np.array_equal(ha, hb))
    print(f"determinism ({args.envs} envs x {args.steps} steps, threads {ta} vs {tb}, seed {args.seed}): "
          f"state hashes {'equal' if same_hash else 'DIFFERENT'} ({int(np.count_nonzero(ha != hb))} slots differ); "
          f"buffers " + ("byte-identical every step" if first_diff is None else
                         f"DIFFER first at step {first_diff[0]} in {first_diff[1]} (slots {first_diff[2]})"))
    print(f"  actions taken {taken:,}; final status {', '.join(f'{k} {v}' for k, v in counts.items() if v)}")
    if taken == 0:
        print("  note: cand_n was 0 everywhere (stub CandidateTable?) — determinism holds trivially for no-op steps")
    for slot, err in list(slot_errors.items())[:3]:
        print(f"  slot {slot} error: {err.splitlines()[0] if err else ''}")
    ok &= same_hash and first_diff is None
    print("leak test: not exported through the C ABI; covered by the C# suite (VecTests T2)")
    print("check:", "OK" if ok else "FAILED")
    return 0 if ok else 1


def cmd_digest(args) -> int:
    with _env(args, args.threads) as env:
        d = _rollout_digest(env, args.steps, args.seed)
    d["pid"] = os.getpid()
    print(json.dumps(d))
    return 0


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(prog="sts2vec", description="sts2vec: batched Sts2Sim environments")
    p.add_argument("--dll", default=None,
                   help="Sts2Sim.Vec.dll (or a NativeAOT .dylib/.so); default STS2VEC_AOT_LIB / STS2VEC_DLL / the Release build")
    sub = p.add_subparsers(dest="cmd", required=True)

    b = sub.add_parser("bench", help="random-policy throughput")
    _add_env_args(b, envs=1024, steps=500, pool=64)
    b.add_argument("--threads", type=int, default=8)
    b.add_argument("--warmup", type=int, default=10, help="untimed steps after reset (default 10)")
    b.add_argument("--warm", action=argparse.BooleanOptionalAction, default=True,
                   help="create the pool's fresh templates before timing (default on; --no-warm measures cold starts)")
    b.add_argument("--policy", choices=["random"], default="random")
    b.add_argument("--json", action="store_true", help="print one JSON object instead of text")
    b.set_defaults(fn=cmd_bench)

    c = sub.add_parser("check", help="schema/buffer consistency and determinism (threads A vs B)")
    _add_env_args(c, envs=64, steps=200, pool=16)
    c.add_argument("--threads-a", type=int, default=1)
    c.add_argument("--threads-b", type=int, default=8)
    c.set_defaults(fn=cmd_check)

    d = sub.add_parser("digest", help="JSON digest of a seeded random rollout (cross-process determinism)")
    _add_env_args(d, envs=32, steps=100, pool=16)
    d.add_argument("--threads", type=int, default=8)
    d.set_defaults(fn=cmd_digest)

    args = p.parse_args(argv)
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
