"""sts2vec CLI (CONTRACTS.md §D6).

- ``sts2vec bench``  random-policy throughput: env-steps/s, ms per batch, obs MB per batch, GC mode, resets/s, status counts;
  the pool's fresh templates are warmed first and that time is printed separately (``--no-warm`` measures cold starts).
- ``sts2vec bench-branch``  search primitives (ABI 2): µs per branched slot (``branch`` exact / determinized) and per
  ``restore_many`` element, against the pre-ABI-2 paths (snapshot / clone_determinized + ``reset(mask, start_ids)``,
  one ``restore`` per slot), plus the pool entries and RSS those old paths leave behind.
- ``sts2vec check``  library/schema/buffer consistency + determinism (two envs with identical config, threads 1 vs 8,
  seeded random policy, byte-identical buffers every step and equal state hashes at the end).
- ``sts2vec digest`` a JSON digest of a seeded random rollout (state hashes + observation bytes); two processes with the
  same arguments must print the same digest (cross-process determinism).
- ``sts2vec eval``   play a protocol seed set with any policy (``random`` / ``heuristic`` / ``first`` /
  ``module:callable`` / ``file.py:callable``) and write the protocol run directory (``sts2vec.evaluate``).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import statistics
import sys
import time
from pathlib import Path

import numpy as np

from .env import FLOOR_STEP_CAP, VecEnv, random_actions
from .loader import load
from .pool import ALL_CHARACTERS, PoolSpec
from .schema import STATUS, SIGNAL_BUFFERS, Schema, status_counts


def _pool(args) -> PoolSpec:
    chars = tuple(c.strip() for c in args.characters.split(",") if c.strip())
    return PoolSpec(characters=chars, ascension=args.ascension, count=args.pool, seed_prefix=args.seed_prefix)


def _env(args, threads: int) -> VecEnv:
    return VecEnv(args.envs, threads=threads, step_cap=args.step_cap, floor_step_cap=args.floor_step_cap,
                  seed_text=args.seed_text, content=args.content, pool=_pool(args), dll=args.dll)


def _add_env_args(p: argparse.ArgumentParser, *, envs: int, steps: int | None, pool: int) -> None:
    p.add_argument("--envs", type=int, default=envs, help=f"number of env slots (default {envs})")
    if steps is not None:
        p.add_argument("--steps", type=int, default=steps, help=f"batched steps (default {steps})")
    p.add_argument("--characters", default=",".join(ALL_CHARACTERS), help="fresh pool characters (comma separated)")
    p.add_argument("--pool", type=int, default=pool, help=f"fresh pool entries (default {pool})")
    p.add_argument("--ascension", type=int, default=0)
    p.add_argument("--seed-prefix", default="TRAIN")
    p.add_argument("--seed-text", default="VEC", help="config.seed_text (per-slot start sequences)")
    p.add_argument("--content", default="ported_only", choices=["ported_only", "all"])
    p.add_argument("--step-cap", type=int, default=3000)
    p.add_argument("--floor-step-cap", type=int, default=FLOOR_STEP_CAP,
                   help=f"non-combat decisions on one floor before truncation (FLOOR_STALL; default {FLOOR_STEP_CAP}; 0 = off)")
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


# ------------------------------------------------------------------------------------------------ bench-branch

def cmd_bench_branch(args) -> int:
    lib = load(args.dll)
    if not lib.supports_branch and not args.old_only:
        print(f"library {lib.path} is ABI {lib.abi_version}: sts2vec_branch is not supported; rebuild it, or pass "
              "--old-only to time the snapshot + reset path alone", file=sys.stderr)
        return 2
    k = args.k
    with VecEnv(args.envs, threads=args.threads, step_cap=args.step_cap, floor_step_cap=args.floor_step_cap,
                seed_text=args.seed_text, content=args.content, pool=_pool(args), dll=args.dll) as env:
        n = env.num_envs
        roots_n = n // (k + 1)
        if roots_n < 1:
            print(f"--envs {n} is too small for --k {k}", file=sys.stderr)
            return 2
        env.pool.warm()
        env.reset()
        rng = np.random.default_rng(args.seed)
        for _ in range(args.advance):  # move every slot into the run (map, combat, rewards, shops, prompts)
            env.step(random_actions(rng, env.obs["cand_n"]))
        gi = env.schema.cols("global_i")
        roots = np.arange(roots_n, dtype=np.int32)
        dsts = (roots_n + np.arange(roots_n * k, dtype=np.int32)).reshape(roots_n, k)
        flat = dsts.ravel()
        owner = np.repeat(roots, k)  # the root of each destination, in flat order
        mask = np.zeros(n, np.uint8)
        mask[flat] = 1
        slots_total = roots_n * k
        in_combat = int(np.count_nonzero(env.obs["global_i"][roots, gi.is_combat]))
        pool0 = len(env.pool)
        res: dict[str, list[float]] = {}
        created = 0
        # one snapshot per root, created once: the start entries of the restore comparisons (the roots never move)
        root_entry = np.array([env.snapshot(int(r)) for r in roots], dtype=np.int32)
        dst_entry = root_entry[owner]
        start_ids = np.full(n, -1, np.int32)
        start_ids[flat] = dst_entry
        sid = np.full(n, -1, np.int32)
        root_of_dst = np.repeat(np.arange(roots_n), k)

        def old_exact(t: dict, rnd: int) -> list[int]:
            t0 = time.perf_counter()
            snaps = [env.snapshot(int(r)) for r in roots]
            t["old_exact_snapshot"] = time.perf_counter() - t0
            sid[flat] = np.asarray(snaps, np.int32)[root_of_dst]
            env.reset(mask=mask, start_ids=sid)
            t["old_exact"] = time.perf_counter() - t0
            return snaps

        def old_det(t: dict, rnd: int) -> list[int]:
            t0 = time.perf_counter()
            dets = [env.clone_determinized(int(r), rnd * k + j) for r in roots for j in range(k)]
            t["old_det_clone"] = time.perf_counter() - t0
            sid[flat] = np.asarray(dets, np.int32)
            env.reset(mask=mask, start_ids=sid)
            t["old_det"] = time.perf_counter() - t0
            return dets

        def timed(key: str, fn):
            def run(t: dict, rnd: int) -> list[int]:
                t0 = time.perf_counter()
                fn(rnd)
                t[key] = time.perf_counter() - t0
                return []
            return run

        ops = [
            old_exact,
            old_det,
            timed("restore_loop", lambda rnd: [env.restore(int(d), int(e)) for d, e in zip(flat, dst_entry)]),
            timed("reset_mask", lambda rnd: env.reset(mask=mask, start_ids=start_ids)),
        ]
        if not args.old_only:
            ops += [
                timed("branch_exact", lambda rnd: [env.branch(int(r), dsts[i], determinized=False) for i, r in enumerate(roots)]),
                timed("branch_det", lambda rnd: [env.branch(int(r), dsts[i], seed=rnd) for i, r in enumerate(roots)]),
                timed("many_exact", lambda rnd: env.branch(owner, flat, determinized=False)),
                timed("many_det", lambda rnd: env.branch(owner, flat, seed=rnd)),
                timed("restore_many", lambda rnd: env.restore_many(flat, dst_entry)),
            ]
        can_release = lib.supports_pool_release
        for rnd in range(args.warmup_rounds + args.rounds):  # the warm-up rounds tier up the JIT and are not reported
            t: dict[str, float] = {}
            entries: list[int] = []
            shift = rnd % len(ops)  # rotate the order, so GC pauses do not always land on the same operation
            for op in ops[shift:] + ops[:shift]:
                entries += op(t, rnd)
            created += len(entries)
            if can_release:
                env.pool.release(entries)  # ABI 1 cannot: there these entries stay in the pool for good
            if rnd >= args.warmup_rounds:
                for key, sec in t.items():
                    res.setdefault(key, []).append(sec / slots_total * 1e6)
        bad = int(np.count_nonzero(env.status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"])))
        if can_release:
            env.pool.release(root_entry)
            assert len(env.pool) == pool0
        pool_end = len(env.pool)
        med = {key: statistics.median(v) for key, v in res.items()}
        best = {key: min(v) for key, v in res.items()}

        def ratio(old: str, new: str) -> float | None:
            return med[old] / med[new] if old in med and new in med else None
        result = {
            "envs": n, "threads": args.threads, "k": k, "roots": roots_n, "branched_slots_per_round": slots_total,
            "rounds": args.rounds, "warmup_rounds": args.warmup_rounds, "advance": args.advance,
            "roots_in_combat": in_combat, "mode": lib.mode, "abi_version": lib.abi_version, "us_per_slot": med,
            "us_per_slot_best": best,
            "old_only": bool(args.old_only),
            "us_per_branch_call": {"exact": med["branch_exact"] * k, "determinized": med["branch_det"] * k}
            if "branch_exact" in med else None,
            "speedup_vs_old": {"branch_exact": ratio("old_exact", "branch_exact"), "branch_det": ratio("old_det", "branch_det"),
                               "many_exact": ratio("old_exact", "many_exact"), "many_det": ratio("old_det", "many_det"),
                               "restore_many_vs_loop": ratio("restore_loop", "restore_many"),
                               "restore_many_vs_reset_mask": ratio("reset_mask", "restore_many")},
            "old_path_entries_per_round": {"exact": roots_n, "determinized": slots_total},
            "old_path_entries_created": created, "pool_entries_at_end": pool_end, "sim_error_or_content_gap_slots": bad,
            "load_average": os.getloadavg()[0] if hasattr(os, "getloadavg") else None,
        }
    if args.json:
        print(json.dumps(result))
        return 0
    r, m, x, b = result, result["us_per_slot"], result["speedup_vs_old"], result["us_per_slot_best"]
    print(f"library {lib.path} ({lib.mode}), ABI {lib.abi_version}; envs={n} threads={args.threads} k={k}: {roots_n} roots "
          f"({in_combat} in combat after {args.advance} random steps) x {k} = {slots_total} branched slots per round; "
          f"us per branched slot (wall): median of {args.rounds} rounds after {args.warmup_rounds} warm-up rounds (best)")
    print(f"  {'':34s} {'exact':>15s} {'determinized':>15s}")

    def row(label: str, a: str, c: str, note: str = "") -> None:
        print(f"  {label:34s} {m[a]:7.2f} ({b[a]:5.2f}) {m[c]:7.2f} ({b[c]:5.2f})   {note}")

    if not r["old_only"]:
        row("branch, one call per root", "branch_exact", "branch_det",
            f"{r['us_per_branch_call']['exact']:.0f} / {r['us_per_branch_call']['determinized']:.0f} us per call")
        row("branch, one call for all roots", "many_exact", "many_det", "sts2vec_branch_many")
    row("old: snapshot/clone_det + reset", "old_exact", "old_det",
        f"of which snapshot {m['old_exact_snapshot']:.2f} / clone_determinized {m['old_det_clone']:.2f}")
    if not r["old_only"]:
        print(f"  speedup vs old: per root {x['branch_exact']:.1f}x / {x['branch_det']:.1f}x, all roots {x['many_exact']:.1f}x / "
              f"{x['many_det']:.1f}x")
        print(f"  restore_many {m['restore_many']:.2f} ({b['restore_many']:.2f}) us/element -> {x['restore_many_vs_loop']:.1f}x "
              f"one restore call per slot, {x['restore_many_vs_reset_mask']:.1f}x reset(mask, start_ids)")
    print(f"  one restore call per slot {m['restore_loop']:.2f} ({b['restore_loop']:.2f}) us/element; reset(mask, start_ids) "
          f"{m['reset_mask']:.2f} ({b['reset_mask']:.2f})")
    print(f"  pool entries the old paths create: {roots_n} + {slots_total} per round ({r['old_path_entries_created']:,} in this "
          f"run; {'released after each round here' if can_release else 'ABI 1 cannot release them'}; pool at the end "
          f"{r['pool_entries_at_end']:,}){'; branch creates none' if not r['old_only'] else ''}")
    if r["load_average"] is not None:
        print(f"  load average {r['load_average']:.1f} (other processes share the cores: compare rows of one run)")
    if bad:
        print(f"  WARNING: {bad} slots with SIM_ERROR/CONTENT_GAP")
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
    print(f"  ABI {lib.abi_version} | exports {len(lib.exports)}" + (f" (missing: {', '.join(lib.missing_exports)})" if lib.missing_exports else ""))
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


def cmd_eval(args) -> int:
    from .evaluate import evaluate

    stem = Path(args.policy.split(":")[0]).stem if ":" in args.policy else args.policy
    out = args.out or f"tmp/eval/vec-{stem}-{Path(args.seedset).stem}-{time.strftime('%Y%m%d-%H%M%S')}"
    from .protocol import BlindSetError

    try:
        rep = evaluate(args.seedset, args.policy, out, threads=args.threads, step_cap=args.step_cap,
                       floor_step_cap=args.floor_step_cap, seed=args.seed, dll=args.dll,
                       log=lambda m: print(m, flush=True), allow_blind=args.allow_blind, purpose=args.purpose)
    except BlindSetError as e:
        print(f"error: {e}", file=sys.stderr, flush=True)
        return 2
    h = rep["headline"]
    print(json.dumps({"out": out, "progress_mean": h["progress_mean"], "progress_ci95": h["progress_ci95"],
                      "wins": h["wins"], "games": h["games"], "results": {k: v for k, v in h["results"].items() if v},
                      "floor_mean": h["floor_mean"], "act_reached": h["act_reached"], "steps_mean": h["steps_mean"],
                      "replayable": h["replayable"]}, indent=1))
    return 0 if h["replayable"] == h["games"] else 1


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

    bb = sub.add_parser("bench-branch", help="search primitives: branch / restore_many vs the snapshot + reset path (ABI 2)")
    _add_env_args(bb, envs=1024, steps=None, pool=64)
    bb.add_argument("--threads", type=int, default=8)
    bb.add_argument("--k", type=int, default=8, help="destinations per root (default 8)")
    bb.add_argument("--rounds", type=int, default=7, help="timed rounds; medians are reported (default 7)")
    bb.add_argument("--warmup-rounds", type=int, default=3, help="untimed rounds first, so the JIT has tiered up (default 3)")
    bb.add_argument("--advance", type=int, default=60, help="random steps before timing, so roots are mid-run (default 60)")
    bb.add_argument("--old-only", action="store_true",
                    help="time only the pre-ABI-2 paths (snapshot / clone_determinized + reset, one restore per slot); "
                         "runs against an ABI 1 library")
    bb.add_argument("--json", action="store_true", help="print one JSON object instead of text")
    bb.set_defaults(fn=cmd_bench_branch)

    c = sub.add_parser("check", help="schema/buffer consistency and determinism (threads A vs B)")
    _add_env_args(c, envs=64, steps=200, pool=16)
    c.add_argument("--threads-a", type=int, default=1)
    c.add_argument("--threads-b", type=int, default=8)
    c.set_defaults(fn=cmd_check)

    d = sub.add_parser("digest", help="JSON digest of a seeded random rollout (cross-process determinism)")
    _add_env_args(d, envs=32, steps=100, pool=16)
    d.add_argument("--threads", type=int, default=8)
    d.set_defaults(fn=cmd_digest)

    e = sub.add_parser("eval", aliases=["evaluate"], help="play a protocol seed set with a policy; write config/run/games/report (protocol v0.2)")
    e.add_argument("--policy", required=True,
                   help="random | heuristic | first | package.module:callable | path/to/file.py:callable")
    e.add_argument("--seedset", required=True, help="std-a0-v1 | std-a10-v1 | smoke-a0-v1 | a file stem | a JSON path")
    e.add_argument("--out", default=None, help="run directory (default tmp/eval/vec-<policy>-<seedset>-<time>)")
    e.add_argument("--threads", type=int, default=8)
    e.add_argument("--seed", type=int, default=0, help="seed of the per-game policy RNGs (ctx.rngs)")
    e.add_argument("--step-cap", type=int, default=1200, help="protocol step cap (result 'capped'; default 1200)")
    e.add_argument("--floor-step-cap", type=int, default=FLOOR_STEP_CAP,
                   help=f"non-combat decisions on one floor before 'stuck' (env floor_step_cap; protocol default "
                        f"{FLOOR_STEP_CAP}; 0 = off)")
    e.add_argument("--allow-blind", action="store_true",
                   help="allow a seed set with games of a frozen blind set (role blind; refused otherwise); needs "
                        "--purpose, logged to OUT/blind-access.jsonl")
    e.add_argument("--purpose", default=None, help="why a blind set is evaluated (with --allow-blind)")
    e.set_defaults(fn=cmd_eval)

    args = p.parse_args(argv)
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
