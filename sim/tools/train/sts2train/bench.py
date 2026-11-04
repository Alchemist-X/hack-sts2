"""Synthetic benchmark of the PPO update and the rollout inference forward (``sts2train bench-update``).

Builds the policy of a preset (or explicit size flags), fills a ``RolloutStore`` with random observations of the
schema's shapes and valid value ranges (``random_obs``), then times K PPO minibatch updates through the trainer's own
code path (``ppo.MinibatchUpdater``: device gather, forward + loss, backward, grad clip, Adam) and K rollout steps
(``ppo.InferenceRunner``: upload, forward, sampling, store write, the host copies the env needs). Compile modes run
the trainer's eager-vs-compiled check first (``ppo.check_compiled``; it also triggers the compile). ``--modes``
runs several compile modes in one process with a speed-up summary; ``--deck-counts`` / ``--aux-heads`` time the
model with those parts (the aux heads' loss on random targets and masks); ``--profile`` adds ``torch.profiler`` tables:
top aten ops by self device time (CUDA) or self CPU time (cpu / mps) and, on CUDA, the top kernels with the summed
kernel time per minibatch (GPU busy share of the wall time).

The numbers that matter come from the training GPU (an A100 in the Colab runtime); cpu / mps runs are only for
validating the code paths with small settings (a cpu / mps bench refuses an estimated > 3 GiB of activations).
"""

from __future__ import annotations

import contextlib
import gc
import json
import os
import time
from pathlib import Path

import numpy as np
import torch

from .features import USED_BUFFERS, ModelSpec, RolloutStore, bind_spec, obs_to_device

# Column value ranges for random observations: [lo, hi) per field name (vocab / enum sizes resolved from the spec).
_VOCAB_COLS = {"card_id": "cards", "relic_id": "relics", "potion_id": "potions", "power_id": "powers",
               "monster_id": "monsters", "orb_id": "orbs", "enchant_id": "enchantments",
               "affliction_id": "afflictions", "event_id": "events", "boss_id": "encounters",
               "second_boss_id": "encounters", "act_id": "acts", "character": "characters",
               "treasure_relic_id": "relics", "item_id": "cards"}
_ENUM_COLS = {"phase": "RunPhase", "decision_kind": "RunPhase", "prompt_kind": "ChoicePromptKind",
              "prompt_pile": "PileType", "target": "TargetType", "rarity": "CardRarity", "point_type": "MapPointType",
              "reward_type": "RewardType", "status": "RelicStatus", "target_type": "TargetType",
              "category": "ShopCategory", "option_id": "RestOption", "kind_of_item": "PromptItemKind"}
_ENUM_BY_BUFFER = {("hand", "type"): "CardType", ("card_reward", "type"): "CardType",
                   ("enemy_intents", "type"): "IntentType", ("cand", "kind"): "RunActionKind"}
_FIXED = {"present": (0, 2), "usable_now": (0, 2), "is_debuff": (0, 2), "visited": (0, 2), "current": (0, 2),
          "legal_next": (0, 2), "enabled": (0, 2), "locked": (0, 2), "proceed": (0, 2), "stocked": (0, 2),
          "on_sale": (0, 2), "affordable": (0, 2), "used_up": (0, 2), "is_card_reward": (0, 2), "playable": (0, 2),
          "behind": (0, 2), "flank": (0, 3), "intent_n": (0, 4), "keyword_mask": (0, 256),
          "playable_reason_id": (0, 128), "child_mask": (0, 128), "key_hash": (-2 ** 31, 2 ** 31 - 1),
          "prompt_site_hash": (-2 ** 31, 2 ** 31 - 1), "hand_slot": (-1, 10), "target_slot": (-1, 6),
          "potion_slot": (-1, 10), "option_index": (-1, 16), "A": (-1, 20), "B": (-1, 20), "target_uid": (0, 100),
          "event_option_n": (0, 9), "upgrade": (0, 2), "cost_current": (-2, 4), "cost_base": (-2, 4)}
# Tables whose leading axis is a row list (rows past a random per-sample count are zero, as in the env).
_ROW_TABLES = ("hand", "deck", "relics", "potions", "powers_player", "orbs", "enemies", "allies", "rewards",
               "card_reward", "shop", "rest", "event_options", "prompt_options", "cand")
FLOAT_BUFFERS = ("global_f", "hand_f", "hand_dmg")
DEFAULT_DTYPES = {name: np.dtype(np.float32) if name in FLOAT_BUFFERS else
                  np.dtype(np.int16) if name.endswith("_counts") else np.dtype(np.int32) for name in USED_BUFFERS}


def _col_range(spec: ModelSpec, buf: str, col: str) -> tuple[int, int]:
    if (buf, col) in _ENUM_BY_BUFFER:
        return 0, max(spec.enums.get(_ENUM_BY_BUFFER[buf, col], 1), 1)
    if col in _FIXED:
        return _FIXED[col]
    if col in _VOCAB_COLS:
        return 0, max(spec.vocab.get(_VOCAB_COLS[col], 1), 1)
    if col in _ENUM_COLS:
        return 0, max(spec.enums.get(_ENUM_COLS[col], 1), 1)
    if col.startswith("var_id_"):
        return 0, max(spec.card_vars, 1)
    return 0, 30                                        # amounts, hp, prices, counters, ...


def random_obs(spec: ModelSpec, n: int, rng: np.random.Generator, *, dtypes: dict | None = None,
               max_cand: int = 64, max_prompt: int = 64) -> dict[str, np.ndarray]:
    """Random env-like observations (the buffers the model reads, full env widths) with valid ids / enums /
    pointers: per-sample row counts (rows past them are zero), cand_n in [1, max_cand], prompt_option_n in
    [0, max_prompt] (20 % of samples in a prompt), sparse pile counts."""
    dtypes = {**DEFAULT_DTYPES, **(dtypes or {})}
    obs: dict[str, np.ndarray] = {}
    for name in USED_BUFFERS:
        shape = (n, *spec.shapes[name])
        cols = spec.cols.get(name)
        if name in FLOAT_BUFFERS:
            a = rng.random(shape, dtype=np.float32) * 20.0
        elif name.endswith("_counts"):
            a = np.zeros(shape, dtype=np.int64)
            k = rng.integers(0, shape[1], size=(n, 24))
            np.add.at(a, (np.arange(n)[:, None], k), rng.integers(0, 3, size=(n, 24)))
        elif name == "card_reward_alts":
            a = rng.integers(0, max(spec.enums.get("CardRewardAlt", 1), 1), size=shape)
        elif name == "cand_n":
            a = rng.integers(1, max_cand + 1, size=shape)
        elif cols:
            a = np.zeros(shape, dtype=np.int64)
            for col, i in cols.items():
                lo, hi = _col_range(spec, name, col)
                a[..., i] = rng.integers(lo, hi, size=shape[:-1])
        else:
            a = rng.integers(0, 10, size=shape)
        obs[name] = a
    gi, gc = obs["global_i"], spec.cols["global_i"]
    in_prompt = rng.random(n) < 0.2
    gi[:, gc["prompt_option_n"]] = np.where(in_prompt, rng.integers(1, max_prompt + 1, size=n), 0)
    for name in _ROW_TABLES:
        a = obs[name]
        rows = a.shape[1]
        if name == "cand":
            cnt = obs["cand_n"]
        elif name == "prompt_options":
            cnt = gi[:, gc["prompt_option_n"]]
        elif name == "event_options":
            cnt = gi[:, gc["event_option_n"]]
        else:
            cnt = rng.integers(0, rows + 1, size=n)
        a[np.arange(rows)[None, :] >= np.minimum(cnt, rows)[:, None]] = 0
    obs["enemy_intents"][obs["enemies"][..., spec.cols["enemies"]["present"]] == 0] = 0
    mp = obs["map_nodes"]
    mp[rng.random(mp.shape[:-1]) < 0.5] = 0
    return {k: np.ascontiguousarray(v.astype(dtypes[k])) for k, v in obs.items()}


# ------------------------------------------------------------------------------------------------ bench-update

LOCAL_ACT_LIMIT = 3 * 2 ** 30      # cpu / mps: refuse a minibatch whose estimated saved activations exceed this
STORE_STEPS = 4                    # synthetic rollout store = STORE_STEPS x minibatch samples (minibatches sample it)


class BenchSizeError(RuntimeError):
    """A cpu / mps benchmark above LOCAL_ACT_LIMIT of estimated activations."""


def load_spec(path: str | None = None) -> tuple[ModelSpec, dict]:
    """(ModelSpec, buffer dtypes) from a checkpoint / spec JSON at `path`, or from the sts2vec library's schema."""
    if path:
        p = Path(path)
        if p.suffix == ".json":
            d = json.loads(p.read_text())
            d = d.get("spec", d)
        else:
            d = torch.load(p, map_location="cpu", weights_only=False)["spec"]
        return ModelSpec.from_json(d), dict(DEFAULT_DTYPES)
    from sts2vec import load, schema_of

    schema = schema_of(load())
    return ModelSpec.from_schema(schema), {k: schema[k].dtype for k in USED_BUFFERS}


def _sync(device: torch.device) -> None:
    if device.type == "cuda":
        torch.cuda.synchronize(device)
    elif device.type == "mps":
        torch.mps.synchronize()


def _dynamo_counts() -> dict:
    with contextlib.suppress(Exception):
        from torch._dynamo.utils import counters

        return {"graph_breaks": int(sum(counters["graph_break"].values())),
                "unique_graphs": int(counters["stats"].get("unique_graphs", 0))}
    return {}


def _profile_table(prof, device: torch.device, steps: int, top: int) -> dict:
    """Per minibatch: the top aten ops by self device time (CUDA; compiled kernels launched outside aten ops are not in
    it) or self CPU time (cpu / mps), with % shares; on CUDA also the top kernels by device time with their share of
    all kernel time, the kernel count and the summed kernel time (GPU busy time, to set against the wall time)."""
    from torch.autograd import DeviceType

    n = max(steps, 1)
    on_dev = device.type == "cuda"
    evs = prof.key_averages()

    def dev_t(e):
        return getattr(e, "self_device_time_total", None) or getattr(e, "self_cuda_time_total", 0.0) or 0.0

    def table(items, key):
        rows = [{"op": e.key, "self_ms": key(e) / 1000.0 / n, "calls": e.count // n} for e in items if key(e) > 0]
        total = sum(r["self_ms"] for r in rows)
        rows.sort(key=lambda r: -r["self_ms"])
        for r in rows:
            r["share"] = r["self_ms"] / total if total else 0.0
        return rows[:top], total

    cpu_side = [e for e in evs if getattr(e, "device_type", DeviceType.CPU) == DeviceType.CPU]
    ops, _ = table(cpu_side, dev_t if on_dev else (lambda e: e.self_cpu_time_total))
    out = {"basis": "self device time" if on_dev else "self CPU time", "ops": ops}
    if on_dev:
        kern = [e for e in evs if getattr(e, "device_type", None) == DeviceType.CUDA]
        out["kernels"], out["kernel_ms_per_minibatch"] = table(kern, dev_t)
        out["kernels_per_minibatch"] = sum(e.count for e in kern) // n
    return out


def _bench_mode(a, mode: str, spec: ModelSpec, dtypes: dict, device: torch.device, log) -> dict:
    from .model import Policy, activation_bytes_per_sample, model_arch, param_count
    from .ppo import (InferenceRunner, MinibatchUpdater, check_compiled, check_compiled_forward, resolve_config,
                      use_static_rows)

    given = {k: getattr(a, k) for k in ("d", "s", "depth", "heads", "ff_mult") if getattr(a, k, None) is not None}
    if a.preset:
        given["preset"] = a.preset
    given.update(minibatch=a.minibatch, max_rows=a.max_rows, compile=mode, amp=bool(a.amp), grad_ckpt=bool(a.grad_ckpt),
                 deck_counts=bool(getattr(a, "deck_counts", False)), aux_heads=bool(getattr(a, "aux_heads", False)))
    if a.static_rows is not None:
        given["static_rows"] = a.static_rows
    cfg = resolve_config(given)
    arch = model_arch(cfg)
    static = use_static_rows(cfg)
    amp = cfg.amp and device.type == "cuda"
    B, R = cfg.minibatch, cfg.max_rows
    torch.manual_seed(a.seed)
    rng = np.random.default_rng(a.seed)
    model = Policy(spec, **arch)
    model.grad_ckpt = cfg.grad_ckpt
    n_params = param_count(model)
    act = activation_bytes_per_sample(model, spec, rows=R)
    if device.type in ("cpu", "mps") and act * B > LOCAL_ACT_LIMIT:
        raise BenchSizeError(f"{device.type}: a minibatch of {B} saves an estimated {act * B / 2 ** 30:.1f} GiB of "
                             f"activations ({act / 2 ** 20:.2f} MiB/sample, fp32) > "
                             f"{LOCAL_ACT_LIMIT / 2 ** 30:.0f} GiB; use a smaller --minibatch locally (the large "
                             f"preset is for --device cuda)")
    model = model.to(device)
    opt = torch.optim.Adam(model.parameters(), lr=3e-4, eps=1e-5)
    updater = MinibatchUpdater(model, opt, cfg, amp, device)
    runner = InferenceRunner(model, amp, mode, device, static=static)
    label = f"compile={mode}" + (" static-rows" if static else " dynamic-rows") + (" amp" if amp else "") + \
        (" grad-ckpt" if cfg.grad_ckpt else "") + (" deck-counts" if cfg.deck_counts else "") + \
        (" aux-heads" if cfg.aux_heads else "")
    log(f"== {label}: {n_params:,} parameters ({', '.join(f'{k}={v}' for k, v in arch.items())}), minibatch {B}, "
        f"max_rows {R}, device {device}")

    # synthetic rollout: STORE_STEPS x B samples, valid actions, uniform-policy log-probs, random advantages / returns
    store = RolloutStore(spec, STORE_STEPS, B, R, dtypes, device)
    for t in range(STORE_STEPS):
        store.put_obs(t, random_obs(spec, B, rng, dtypes=dtypes, max_cand=R, max_prompt=R))
    n = STORE_STEPS * B
    legal = np.minimum(store.cand_n_h.reshape(-1), R)
    to = lambda x: torch.from_numpy(np.ascontiguousarray(x)).to(device)  # noqa: E731
    d_act = to(np.floor(rng.random(n) * legal).astype(np.int64))
    d_logp = to((-np.log(legal)).astype(np.float32))
    d_adv, d_ret, d_val = (to(rng.standard_normal(n).astype(np.float32)) for _ in range(3))
    d_aux = ()             # --aux-heads: random targets in [0, 1] and masks (90 % win targets, 50 % combat targets)
    if cfg.aux_heads:
        d_aux = tuple(to(x.astype(np.float32)) for x in (rng.random(n), rng.random(n) < 0.9, rng.random(n),
                                                          rng.random(n) < 0.5))

    def minibatches(k):
        return [np.sort(rng.choice(n, B, replace=False)) for _ in range(k)]

    def inputs(mb_h, mb):
        return (store.batch(mb, rows=None if static else store.rows(mb_h), static=static), d_act[mb], d_logp[mb],
                d_adv[mb], d_ret[mb], d_val[mb], *(x[mb] for x in d_aux))

    def run_updates(k):
        mbs = minibatches(k)
        d_mbs = torch.split(to(np.concatenate(mbs)), B)
        stats = [updater(*inputs(mh, mb)) for mh, mb in zip(mbs, d_mbs)]
        return torch.stack(stats).cpu().numpy()

    out = {"mode": mode, "label": label, "static_rows": static, "amp": amp, "grad_ckpt": cfg.grad_ckpt,
           "params": n_params, "arch": arch, "minibatch": B, "max_rows": R, "device": str(device)}
    model.train()
    tf32 = device.type == "cuda" and torch.backends.cuda.matmul.allow_tf32
    if mode != "off" and a.check:
        # eager vs compiled forward + backward on one fixed minibatch (no optimizer step); includes the first compile
        mh = minibatches(1)[0]
        t0 = time.perf_counter()
        chk = check_compiled(updater, inputs(mh, to(mh)), amp=amp, tf32=tf32)
        out["compile_s"] = round(time.perf_counter() - t0, 1)
        out["check"] = chk
        log(f"   numerics (one minibatch, compiled vs eager): loss {chk['loss_compiled']:.6f} vs "
            f"{chk['loss_eager']:.6f} (rel {chk['loss_rel']:.1e}, tol {chk['tol_loss']:g}); gradient norm "
            f"{chk['grad_norm_compiled']:.6f} vs {chk['grad_norm_eager']:.6f}; worst parameter {chk['worst_param']} "
            f"rel {chk['worst_param_grad_rel']:.1e} (tol {chk['tol_grad']:g}): {'OK' if chk['ok'] else 'MISMATCH'}; "
            f"compile + first runs {out['compile_s']} s")
    if device.type == "cuda":
        torch.cuda.reset_peak_memory_stats(device)
    t0 = time.perf_counter()
    run_updates(a.warmup)
    _sync(device)
    out["warmup_s"] = round(time.perf_counter() - t0, 2)
    t0 = time.perf_counter()
    st = run_updates(a.steps)
    _sync(device)
    dt = time.perf_counter() - t0
    out.update(update_ms=1000 * dt / a.steps, update_samples_per_s=a.steps * B / dt,
               update_loss_finite=bool(np.isfinite(st[:, 5]).all()))
    if device.type == "cuda":
        out["gpu_peak_mb"] = round(torch.cuda.max_memory_allocated(device) / 2 ** 20, 1)
    log(f"   update: {out['update_ms']:.1f} ms per minibatch of {B} ({out['update_samples_per_s']:,.0f} samples/s; "
        f"{a.steps} timed after {a.warmup} warm-up in {out['warmup_s']} s)"
        + (f"; GPU peak {out['gpu_peak_mb']:,.0f} MB" if "gpu_peak_mb" in out else ""))

    if a.profile:
        from torch.profiler import ProfilerActivity, profile

        acts = [ProfilerActivity.CPU] + ([ProfilerActivity.CUDA] if device.type == "cuda" else [])
        with profile(activities=acts) as prof:
            run_updates(a.profile_steps)
            _sync(device)
        out["profile"] = pr = _profile_table(prof, device, a.profile_steps, a.top)
        log(f"   profile, top aten ops by {pr['basis']} per minibatch ({a.profile_steps} minibatches):")
        for r in pr["ops"]:
            log(f"     {r['share'] * 100:5.1f}%  {r['self_ms']:8.2f} ms  {r['calls']:5d}x  {r['op'][:100]}")
        if "kernels" in pr:
            busy = pr["kernel_ms_per_minibatch"] / out["update_ms"]
            log(f"   profile, top CUDA kernels: {pr['kernels_per_minibatch']} kernels and "
                f"{pr['kernel_ms_per_minibatch']:.1f} ms of kernel time per minibatch (GPU busy {busy:.0%} of the timed "
                f"{out['update_ms']:.1f} ms):")
            for r in pr["kernels"]:
                log(f"     {r['share'] * 100:5.1f}%  {r['self_ms']:8.2f} ms  {r['calls']:5d}x  {r['op'][:100]}")

    # rollout inference: one env step's model work = upload + forward + sampling + store + host copies
    model.eval()
    N = a.envs or B
    obs = random_obs(spec, N, rng, dtypes=dtypes, max_cand=R, max_prompt=R)
    istore = RolloutStore(spec, 1, N, R, dtypes, device)

    def rollout_step():
        x = obs_to_device(obs, device, max_rows=R, static=static)
        act_, logp, value = runner.sample(x)
        istore.put(0, x, obs)
        return act_.cpu().numpy(), logp.cpu().numpy(), value.cpu().numpy()

    if mode != "off" and a.check:
        x = obs_to_device(obs, device, max_rows=R, static=static)
        chk = check_compiled_forward(runner, x, amp=amp, tf32=tf32)
        out["check_forward"] = chk
        log(f"   numerics (rollout forward, compiled vs eager): logits rel {chk['logits_rel']:.1e}, values rel "
            f"{chk['value_rel']:.1e} (tol {chk['tol']:g}): {'OK' if chk['ok'] else 'MISMATCH'}")
    for _ in range(a.warmup):
        rollout_step()
    t0 = time.perf_counter()
    for _ in range(a.infer_steps):
        rollout_step()
    dt = time.perf_counter() - t0
    x = obs_to_device(obs, device, max_rows=R, static=static)
    _sync(device)
    t1 = time.perf_counter()
    for _ in range(a.infer_steps):
        runner.forward(x)
    _sync(device)
    df = time.perf_counter() - t1
    out.update(infer_batch=N, rollout_step_ms=1000 * dt / a.infer_steps, infer_forward_ms=1000 * df / a.infer_steps,
               infer_samples_per_s=a.infer_steps * N / dt, **_dynamo_counts())
    log(f"   rollout inference, batch {N}: {out['rollout_step_ms']:.1f} ms per step (upload + forward + sample + store "
        f"+ host copies; {out['infer_samples_per_s']:,.0f} samples/s), forward alone {out['infer_forward_ms']:.1f} ms"
        + (f"; dynamo: {out.get('unique_graphs')} graphs, {out.get('graph_breaks')} graph breaks"
           if mode != "off" and "unique_graphs" in out else ""))
    return out


def bench_update(a, log=print) -> list[dict]:
    """`sts2train bench-update`: one result dict per compile mode (`--modes`, default the single `--compile`)."""
    from .ppo import cap_mps_memory, pick_device

    device = pick_device(a.device)
    if device.type == "mps":
        cap_mps_memory(log)
    torch.set_num_threads(a.torch_threads or min(os.cpu_count() or 4, 16))
    if device.type == "cuda":
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
    spec, dtypes = load_spec(a.spec)
    bind_spec(spec)
    log(f"bench-update: torch {torch.__version__}, device {device}"
        + (f" ({torch.cuda.get_device_name(device)}), TF32 matmul {torch.backends.cuda.matmul.allow_tf32}"
           if device.type == "cuda" else "") + f", schema {spec.encoder_version}")
    if device.type != "cuda":
        log("NOTE: cpu / mps numbers only validate the code paths; the numbers that matter come from the training GPU "
            "(A100): run the same command there with --device cuda.")
    modes = [m.strip() for m in (a.modes or a.compile or "off").split(",") if m.strip()]
    results = []
    for mode in modes:
        with contextlib.suppress(Exception):
            from torch._dynamo.utils import counters

            torch._dynamo.reset()
            counters.clear()
        results.append(_bench_mode(a, mode, spec, dtypes, device, log))
        gc.collect()
        if device.type == "cuda":
            torch.cuda.empty_cache()
    if len(results) > 1:
        base = results[0]
        log("summary (speed-up vs the first mode):")
        for r in results:
            log(f"   {r['label']:<44} update {r['update_ms']:8.1f} ms/minibatch "
                f"(x{base['update_ms'] / r['update_ms']:.2f})   rollout step {r['rollout_step_ms']:7.1f} ms "
                f"(x{base['rollout_step_ms'] / r['rollout_step_ms']:.2f})")
    if a.json:
        Path(a.json).parent.mkdir(parents=True, exist_ok=True)
        Path(a.json).write_text(json.dumps({"torch": torch.__version__, "device": str(device), "results": results},
                                           indent=1, default=str))
    return results
