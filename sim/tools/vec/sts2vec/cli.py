"""sts2vec CLI: ``sts2vec bench`` and ``sts2vec check`` (scaffold versions; V3 completes them per CONTRACTS.md §D6)."""

from __future__ import annotations

import argparse
import sys
import time

import numpy as np

from .env import PoolSpec, VecEnv
from .loader import load
from .schema import STATUS, Schema, status_names


def _random_actions(rng: np.random.Generator, cand_n: np.ndarray) -> np.ndarray:
    # uniform over [0, cand_n); -1 (no-op) where there are no candidates
    acts = (rng.random(len(cand_n)) * np.maximum(cand_n, 1)).astype(np.int32)
    acts[cand_n <= 0] = -1
    return acts


def cmd_bench(args) -> int:
    lib = load(args.dll)
    print(f"library {lib.path} ({lib.mode}); {lib.version()}")
    pool = PoolSpec(characters=tuple(args.characters.split(",")), count=args.pool)
    with VecEnv(args.envs, threads=args.threads, pool=pool, dll=args.dll) as env:
        t0 = time.perf_counter()
        obs = env.reset()
        t_reset = time.perf_counter() - t0
        rng = np.random.default_rng(args.seed)
        steps = 0
        t0 = time.perf_counter()
        for _ in range(args.steps):
            obs, reward, term, trunc, status, info = env.step(_random_actions(rng, obs["cand_n"]))
            steps += 1
        dt = time.perf_counter() - t0
        mb = env.buffers.nbytes / 1e6
        print(f"envs={args.envs} threads={args.threads} steps={steps} policy={args.policy}: "
              f"{args.envs * steps / max(dt, 1e-9):,.0f} env-steps/s | {dt / max(steps, 1) * 1e3:.2f} ms/batch | "
              f"{mb:.1f} MB obs/batch | reset {t_reset * 1e3:.0f} ms | GC server (create asserts it)")
        flags = np.bitwise_or.reduce(env.buffers["status"]) if args.envs else 0
        print(f"status flags seen in the last batch: {status_names(int(flags))}")
    return 0


def cmd_check(args) -> int:
    lib = load(args.dll)
    text = lib.schema_json()
    schema = Schema.from_json(text)
    print(f"library {lib.path} ({lib.mode}), runtimeconfig {lib.runtimeconfig}, hostfxr {lib.hostfxr}")
    print(f"version {lib.version()} | schema {len(text):,} bytes, {len(schema.buffers)} buffers, "
          f"{schema.bytes_per_env:,} bytes/env | encoder_version verified: {schema.verify_version()}")
    ok = schema.verify_version() and lib.version() == "sts2vec/" + schema.encoder_version
    pool = PoolSpec(characters=("IRONCLAD",), count=2)
    hashes = []
    for threads in (1, 2):
        with VecEnv(args.envs, threads=threads, pool=pool, dll=args.dll) as env:
            obs = env.reset()
            for b in schema.buffers:
                ok &= env.buffers[b.name].nbytes == args.envs * b.bytes_per_env
            rng = np.random.default_rng(0)
            for _ in range(args.steps):
                obs, *_ = env.step(_random_actions(rng, obs["cand_n"]))
            hashes.append(env.state_hash())
            print(f"threads={threads}: reset+{args.steps} steps ok; status {[status_names(int(s)) for s in env.buffers['status']]}")
    det = bool(np.array_equal(hashes[0], hashes[1]))
    print(f"determinism (1 vs 2 threads, state hashes): {'equal' if det else 'DIFFERENT'}")
    ok &= det
    print("check:", "OK" if ok else "FAILED")
    return 0 if ok else 1


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(prog="sts2vec")
    p.add_argument("--dll", default=None, help="Sts2Sim.Vec.dll (or AOT .dylib/.so); default STS2VEC_DLL or the Release build")
    sub = p.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("bench", help="random-policy throughput")
    b.add_argument("--envs", type=int, default=1024)
    b.add_argument("--threads", type=int, default=8)
    b.add_argument("--steps", type=int, default=500)
    b.add_argument("--policy", choices=["random"], default="random")
    b.add_argument("--characters", default="IRONCLAD,SILENT,DEFECT,REGENT,NECROBINDER")
    b.add_argument("--pool", type=int, default=64, help="fresh pool entries")
    b.add_argument("--seed", type=int, default=0)
    b.set_defaults(fn=cmd_bench)
    c = sub.add_parser("check", help="schema/buffer consistency and determinism smoke")
    c.add_argument("--envs", type=int, default=4)
    c.add_argument("--steps", type=int, default=5)
    c.set_defaults(fn=cmd_check)
    args = p.parse_args(argv)
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())

__all__ = ["main", "STATUS"]
