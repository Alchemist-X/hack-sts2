"""NN entry point of the STS2-Bench protocol v0.2 (docs/sts2-bench-protocol-v0.2-2026-11-01.md §7 "NN").

A thin wrapper over the policy-agnostic evaluator in the env package (``sts2vec.evaluate``, CLI ``sts2vec eval``):
this module only turns a model (or a checkpoint) into a greedy policy callable ``(obs, ctx) -> actions`` and records
the weights hash. Everything else — one pool entry and one slot per game, no auto-reset, the protocol limits (step_cap
1200 → ``capped``, floor_step_cap 80 → ``stuck``), scoring, the run directory in the LLM runner's layout, the replay
audit — is ``sts2vec.evaluate.evaluate``. ``sts2eval report DIR`` re-renders the same directory.

``evaluate_model`` is what the trainer calls in-process every ``eval_every`` updates (the live model, no reload);
``evaluate`` loads a checkpoint first (``sts2train eval``). A model whose spec differs from the library's is evaluated
only when the model's inputs are identical or widened (``features.check_spec_compat``, the rule ``--resume`` uses: a
checkpoint whose embedded enums gained members at their end runs with those tables widened, the new rows zero); the
run directory's config.json records the checkpoint's encoder_version next to the library's, and ``spec_compat`` when
the specs differ.

A model with aux heads (``--aux-heads``, ``sts2train.heads``) also reads them out at every decision: each game's
``games/<KEY>.summary.json`` gets ``win_prob`` (P(win) under the policy, one value per entry of ``history_actions``)
and ``combat_hp_pred`` (predicted hp / max_hp at the end of the current combat; null outside combat), plus
``win_prob_first`` / ``win_prob_last``; config.json lists the heads and the returned report carries ``head_readout``.

The greedy policy runs with the loop breaker (``sts2train.loopbreak``; ``loop_breaker=False`` / ``sts2train eval
--loop-breaker off`` turns it off): back at an observation it decided at within its last 64 decisions (a no-op such as
buying a potion with a full belt, or a cycle such as Rewards -> card reward -> skip -> Rewards), a game's candidate
rows already taken there are masked, and from the ``loop_breaker_k``-th (3) return on a row that leaves the screen is
preferred, so the game no longer repeats itself until the floor step cap ends it ``stuck``. The evaluator records and
replays the actions actually sent. Every game summary gets ``loop_breaks`` (decisions where the chosen row is not the
model's argmax), ``no_op_actions`` (actions that left the observation unchanged) and ``cycle_revisits`` (returns to an
observation through others; both counted with the breaker off too); config.json records ``loop_breaker`` (mode, k,
window, leave kinds) and report.json / the returned report carry the totals under ``loop_breaker``.

Execution modes (2026-11-04 diagnosis: separate "the policy's own distribution is bad" from "greedy extraction
loops"): ``policy`` greedy (argmax) or sample (``sts2train eval --policy sample``: one categorical draw per decision
from the model's distribution over the legal candidates, float64 inverse CDF, the action RNG of each play seeded from
(``sample_seed``, game key, ``repeat``) by ``cycles.sample_rng``, so a play does not depend on the batch), crossed with
the loop breaker on / off (``--breaker`` = ``--loop-breaker``; sampled with the breaker on, the rows taken at a
revisited observation get probability 0, and ``loop_breaks`` counts the decisions taken under such a mask). All modes
use the same protocol limits. ``--repeats R`` plays the seed set R times (``OUT/r<k>/``, k = 0..R-1, each a protocol
run directory; ``OUT/repeats.json`` lists them); ``--chunk i/n`` plays only the i-th of n equal slices of the seed set
(config.json ``parent_seedset`` records the full set's identity and the slice, so ``sts2train compare`` can merge
slices). Every game summary gets ``eval_mode`` (policy, breaker, sample seed, repeat) and the UI revisit cycles of the
detector ``sts2train.cycles`` (counting only, the same one the trainer runs): ``cycles``, ``cycles_rest``,
``cycles_shop`` and ``cycle_events`` (decision index ``step``, room, floor, length, action path); report.json gets the
totals under ``cycles`` and ``eval_mode``.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import torch
from sts2vec import protocol as PG
from sts2vec.evaluate import FLOOR_STEP_CAP, STEP_CAP, replay  # noqa: F401  (re-exported)
from sts2vec.evaluate import evaluate as vec_evaluate

from .cycles import CycleDetector, sample_rng
from .features import ModelSpec, bind_spec, check_spec_compat, describe_widened, obs_to_torch
from .force import FORCE_MODES, ForcedChoice, parse_force, write_force_stats
from .loopbreak import DEFAULT_K, LoopBreaker

POLICIES = ("greedy", "sample")


def greedy_policy(model, device: str = "cpu", heads: "HeadLog | None" = None, breaker: LoopBreaker | None = None,
                  force: ForcedChoice | None = None):
    """The model's greedy policy as an ``sts2vec`` policy callable (every candidate row, float32). With `heads` (a
    model with aux heads) every call also records the heads' readouts of the slots that act; with `breaker` the argmax
    goes through the loop breaker (``LoopBreaker.greedy``; it learns the game keys from the first call); with `force`
    (``sts2train.force``) the forced decisions' logits are restricted to the chosen forced row first."""

    def act(obs: dict, ctx) -> np.ndarray:
        with torch.no_grad():
            if heads is None:
                logits, _ = model(obs_to_torch(obs, device))
            else:
                logits, _, h = model(obs_to_torch(obs, device), aux=True)
                heads.record(obs, ctx, h)
        if force is not None:
            logits = force.apply(obs, logits, ctx.live, ctx.keys)
        if breaker is None:
            return logits.argmax(-1).cpu().numpy().astype(np.int32)
        if breaker.keys is None:
            breaker.keys = list(ctx.keys)
        return breaker.greedy(obs, logits, ctx.live)

    return act


def sample_policy(model, device: str = "cpu", heads: "HeadLog | None" = None, breaker: LoopBreaker | None = None, *,
                  seed: int = 0, repeat: int = 0, force: ForcedChoice | None = None):
    """The model's sampling policy as an ``sts2vec`` policy callable: per live slot one draw from the softmax of its
    legal candidates' logits (float64 inverse CDF on one uniform of the play's own RNG, ``cycles.sample_rng(seed,
    game key, repeat)``, drawn only when the slot acts, so a play does not depend on which games share the batch).
    With `breaker` the logits go through ``LoopBreaker.apply`` first (rows taken at a revisited observation get
    probability 0) and ``commit`` records the actions (a loop break = a decision under a mask)."""
    rngs: list = []

    def act(obs: dict, ctx) -> np.ndarray:
        if not rngs:
            rngs.extend(sample_rng(seed, k, repeat) for k in ctx.keys)
        with torch.no_grad():
            if heads is None:
                logits, _ = model(obs_to_torch(obs, device))
            else:
                logits, _, h = model(obs_to_torch(obs, device), aux=True)
                heads.record(obs, ctx, h)
        if force is not None:       # a forced decision keeps one live row: the draw takes it
            logits = force.apply(obs, logits, ctx.live, ctx.keys)
        if breaker is not None:
            if breaker.keys is None:
                breaker.keys = list(ctx.keys)
            logits = breaker.apply(obs, logits, ctx.live)
        z = logits.double().cpu().numpy()
        cand_n = np.asarray(obs["cand_n"])
        a = np.full(len(cand_n), -1, dtype=np.int32)
        for i in np.flatnonzero(np.asarray(ctx.live, dtype=bool) & (cand_n > 0)).tolist():
            zi = z[i, : int(cand_n[i])]
            c = np.cumsum(np.exp(zi - zi.max()))
            a[i] = min(int(np.searchsorted(c, rngs[i].random() * c[-1], side="right")), len(zi) - 1)
        if breaker is not None:
            breaker.commit(a)
        return a

    return act


def with_cycles(act, det: CycleDetector):
    """`act` with the cycle detector watching: it observes every call's observation for the live slots and records
    the actions sent (counting only; the actions are `act`'s)."""

    def f(obs: dict, ctx) -> np.ndarray:
        det.observe(obs, ctx.live)
        a = act(obs, ctx)
        det.commit(a, obs)
        return a

    return f


def mode_name(policy: str, loop_breaker: bool) -> str:
    """``greedy-on`` / ``greedy-off`` / ``sample-on`` / ``sample-off``."""
    return f"{policy}-{'on' if loop_breaker else 'off'}"


def parse_mode(mode: str) -> tuple[str, bool]:
    """(policy, loop breaker on) of a mode name (`mode_name`); ValueError otherwise."""
    pol, _, br = str(mode).partition("-")
    if pol not in POLICIES or br not in ("on", "off"):
        raise ValueError(f"eval mode {mode!r}: expected one of greedy-on, greedy-off, sample-on, sample-off")
    return pol, br == "on"


def write_cycle_stats(det: CycleDetector, keys: list[str], out_dir: str | Path, mode: dict) -> dict:
    """Add ``eval_mode``, the per-game row aliases ``decisions`` (= ``steps``), ``wall_s`` (= ``seconds``: from the
    evaluation's start to the game's end, games run batched) and ``stalled`` (ended ``stuck``), and the cycle counts /
    events to every game summary, and the totals (``cycles``) and ``eval_mode`` to report.json; -> the totals."""
    out = Path(out_dir)
    per = det.slot_totals()
    stuck = 0
    for i, key in enumerate(keys):
        p = out / "games" / f"{key}.summary.json"
        s = json.loads(p.read_text(encoding="utf-8"))
        c = per[i] if i < len(per) else {"cycles": 0, "cycles_rest": 0, "cycles_shop": 0, "cycle_events": []}
        s.update(eval_mode=mode, decisions=s.get("steps"), wall_s=s.get("seconds"),
                 stalled=s.get("result") == "stuck", cycles=c["cycles"], cycles_rest=c["cycles_rest"],
                 cycles_shop=c["cycles_shop"],
                 cycle_events=[{k: e[k] for k in ("step", "phase", "prompt", "act", "floor", "len", "path")}
                               for e in c["cycle_events"]])
        stuck += s.get("result") == "stuck"
        with open(p, "w", encoding="utf-8") as f:
            json.dump(s, f, ensure_ascii=False, indent=1)
    t = det.totals_dict()
    games_with = sum(1 for c in per if c["cycles"] > 0)
    stats = {"cycles": t["cycles"], "cycles_rest": t["cycles_rest"], "cycles_shop": t["cycles_shop"],
             "cycle_steps": t["cycle_steps"], "games_with_cycle": games_with, "games": len(keys),
             "cycles_per_game": t["cycles"] / len(keys) if keys else None, "stuck": stuck,
             "rejected": {k[len("rejected_"):]: v for k, v in t.items() if k.startswith("rejected_")}}
    rp = out / "report.json"
    if rp.exists():
        r = json.loads(rp.read_text(encoding="utf-8"))
        r["cycles"] = stats
        r["eval_mode"] = mode
        with open(rp, "w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False, indent=1)
    return stats


def write_loop_stats(breaker: LoopBreaker, out_dir: str | Path) -> dict:
    """Add ``loop_breaks`` / ``no_op_actions`` / ``cycle_revisits`` to every game summary and the totals
    (``loop_breaker``) to report.json; -> the totals."""
    out = Path(out_dir)
    keys = breaker.keys or []
    for i, key in enumerate(keys):
        p = out / "games" / f"{key}.summary.json"
        s = json.loads(p.read_text(encoding="utf-8"))
        s.update(loop_breaker=breaker.mode, loop_breaks=int(breaker.loop_breaks[i]),
                 no_op_actions=int(breaker.no_op_actions[i]), cycle_revisits=int(breaker.cycle_revisits[i]))
        with open(p, "w", encoding="utf-8") as f:
            json.dump(s, f, ensure_ascii=False, indent=1)
    t = breaker.totals()
    stats = {"mode": t["mode"], "k": t["k"], "window": t["window"], "loop_breaks": t["loop_breaks"],
             "no_op_actions": t["no_op_actions"], "cycle_revisits": t["cycle_revisits"],
             "games_with_loop_breaks": t["slots_with_loop_breaks"], "games_with_no_ops": t["slots_with_no_ops"],
             "games_with_cycles": t["slots_with_cycles"]}
    rp = out / "report.json"
    if rp.exists():
        r = json.loads(rp.read_text(encoding="utf-8"))
        r["loop_breaker"] = stats
        with open(rp, "w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False, indent=1)
    return stats


class HeadLog:
    """Per game, per decision readouts of the aux heads during ``sts2vec.evaluate``: the evaluator calls the policy
    once per batched step and appends one ``history_actions`` entry per slot in ``ctx.live``, so recording exactly
    those slots keeps the lists aligned with ``history_actions``. ``write`` adds them to the game summaries."""

    def __init__(self, is_combat_col: int):
        self.col = is_combat_col
        self.win: list[list[float]] = []
        self.combat: list[list[float | None]] = []
        self.keys: list[str] = []

    def record(self, obs: dict, ctx, h: dict) -> None:
        if not self.win:
            self.keys = list(ctx.keys)
            self.win = [[] for _ in range(ctx.num_envs)]
            self.combat = [[] for _ in range(ctx.num_envs)]
        wp = torch.sigmoid(h["win_logit"]).cpu().numpy()
        cp = torch.sigmoid(h["combat_logit"]).cpu().numpy()
        in_combat = obs["global_i"][:, self.col] != 0
        for i in np.flatnonzero(ctx.live):
            self.win[i].append(round(float(wp[i]), 6))
            self.combat[i].append(round(float(cp[i]), 6) if in_combat[i] else None)

    def write(self, out_dir: str | Path) -> dict:
        """Add ``win_prob`` / ``combat_hp_pred`` (+ first / last win_prob) to every game summary; -> a readout."""
        firsts, lasts = [], []
        for i, key in enumerate(self.keys):
            p = Path(out_dir) / "games" / f"{key}.summary.json"
            s = json.loads(p.read_text(encoding="utf-8"))
            if len(s.get("history_actions") or []) != len(self.win[i]):
                raise RuntimeError(f"{key}: {len(self.win[i])} head readouts for {len(s.get('history_actions') or [])} "
                                   "recorded actions")
            first = self.win[i][0] if self.win[i] else None
            last = self.win[i][-1] if self.win[i] else None
            s.update(win_prob=self.win[i], combat_hp_pred=self.combat[i], win_prob_first=first, win_prob_last=last)
            with open(p, "w", encoding="utf-8") as f:
                json.dump(s, f, ensure_ascii=False, indent=1)
            firsts += [first] if first is not None else []
            lasts += [last] if last is not None else []
        return {"games": len(self.keys), "win_prob_first_mean": float(np.mean(firsts)) if firsts else None,
                "win_prob_last_mean": float(np.mean(lasts)) if lasts else None}


def evaluate_model(model, spec, seedset: str, out_dir: str, *, weights_hash: str, checkpoint: str | None = None,
                   threads: int = 8, device: str = "cpu", step_cap: int = STEP_CAP,
                   floor_step_cap: int = FLOOR_STEP_CAP, spec_compat: dict | None = None, loop_breaker: bool = True,
                   loop_breaker_k: int = DEFAULT_K, policy: str = "greedy", sample_seed: int = 0, repeat: int = 0,
                   parent_seedset: dict | None = None, log=print, allow_blind: bool = False,
                   blind_purpose: str | None = None, macros=None, force=None, force_mode: str = FORCE_MODES[0],
                   force_seed: int = 0) -> dict:
    """Play `seedset` with `model` (`policy` greedy, or sample with the play RNG of (`sample_seed`, game, `repeat`))
    and write the protocol run directory `out_dir`; returns the report (with the loop breaker's totals under
    ``loop_breaker``, the cycle detector's under ``cycles``). `parent_seedset` (a slice's full set: identity and the
    slice) goes into config.json. A seed set with games of a frozen blind set is refused
    (``sts2vec.protocol.BlindSetError``, before any game) unless `allow_blind` and `blind_purpose` are given; the
    access is then logged to ``out_dir/blind-access.jsonl``.

    Raises `features.SpecMismatch` (before any game is played) when `spec` (the model's) differs from the library's in
    the model's inputs; an encoder_version change outside them is accepted and noted. `spec_compat` = the record of a
    checkpoint loaded with `load_checkpoint(..., library=...)` (a widened model's `spec` already has the library's
    sizes; the record says which enums were widened): it is logged and kept in config.json instead of this check's.

    `macros` (None / "rest,shop"; ``sts2vec.VecEnv(macros=...)``): the env's room macro candidates; they must be the
    ones the model was trained with (`checkpoint_macros`). Recorded as ``limits.macros`` in config.json when on.

    `force` (selectors, ``sts2train.force``; None / "" / "none" = off, the plain policy) with `force_mode` greedy /
    uniform (`force_seed`): the forced-choice what-if eval; config.json records ``force``, report.json and the returned
    report gain the counts under ``force``, every game summary its own."""
    from sts2vec import load, schema_of

    schema = schema_of(load())
    library = ModelSpec.from_schema(schema)
    compat = check_spec_compat(spec, library, what="evaluate")
    if spec_compat is not None:
        compat = spec_compat
    if compat["other_differences"] or compat.get("widened_enums"):
        inputs = "identical"
        if compat.get("widened_enums"):
            inputs = (f"identical except for widened enums: {describe_widened(compat['widened_enums'])} (new rows "
                      "zero)")
        log(f"note: checkpoint encoder_version {compat['checkpoint_encoder_version']}, library "
            f"{compat['library_encoder_version']}: the model's inputs are {inputs} (other differences: "
            f"{'; '.join(compat['other_differences']) or 'none'})")
    if policy not in POLICIES:
        raise ValueError(f"policy {policy!r}: expected one of {POLICIES}")
    bind_spec(spec)
    was_training = model.training
    model.eval()
    name = f"nn:{Path(checkpoint).name}" if checkpoint else "nn:live"
    if policy == "sample":
        name += f":sample:s{int(sample_seed)}:r{int(repeat)}"
    heads = HeadLog(spec.cols["global_i"]["is_combat"]) if getattr(model, "aux_heads", False) else None
    breaker = LoopBreaker(library, k=loop_breaker_k, enabled=loop_breaker)     # leave kinds: the library's ids
    force = parse_force(force)
    fc = ForcedChoice(library, force, force_mode, force_seed) if force else None
    if fc is not None:
        name += f":force-{force_mode}:{force}"
        log(fc.describe())
    if policy == "greedy":
        act = greedy_policy(model, device, heads, breaker, **({"force": fc} if fc is not None else {}))
    else:       # the breaker off still counts no-ops / cycle revisits and returns the logits unchanged
        act = sample_policy(model, device, heads, breaker, seed=sample_seed, repeat=repeat,
                            **({"force": fc} if fc is not None else {}))
    ss = PG.validate_seedset(dict(seedset)) if isinstance(seedset, dict) else PG.load_seedset(seedset)
    keys = [PG.game_key(g) for g in ss["games"]]
    det = CycleDetector(library, card_name=schema.vocab.cards.name, keep_events=True)
    act = with_cycles(act, det)
    mode = {"policy": policy, "breaker": breaker.mode, "name": mode_name(policy, loop_breaker),
            "sample_seed": int(sample_seed) if policy == "sample" else None,
            "repeat": int(repeat) if policy == "sample" else None}
    extra = {"heads": ["win_prob", "combat_hp_pred"]} if heads is not None else {}
    extra["loop_breaker"] = {"mode": breaker.mode, "k": breaker.k, "window": breaker.window,
                             "leave_kinds": list(breaker.leave_kinds)}
    extra["eval_mode"] = mode
    extra["cycle_detector"] = {"window": det.window, "anchors": ["RestSite", "Shop"],
                               "prompt_kinds": [det.pkind_names[int(k)] for k in det.prompt_kind_ids]}
    if parent_seedset is not None:
        extra["parent_seedset"] = parent_seedset
    if fc is not None:
        extra["force"] = {"selectors": fc.labels, "mode": force_mode,
                          "seed": int(force_seed) if force_mode == "uniform" else None}

    try:
        rep = vec_evaluate(
            ss, act, out_dir, threads=threads, step_cap=step_cap, floor_step_cap=floor_step_cap,
            seed=int(sample_seed) if policy == "sample" else 0,
            policy_name=name, policy_hash=f"weights:{weights_hash}", log=log, allow_blind=allow_blind,
            purpose=blind_purpose, summary_extra={"weights_hash": weights_hash}, macros=macros,
            config_extra={"model": "sts2train entity-encoder PPO",
                          "checkpoint": str(checkpoint) if checkpoint else None,
                          "weights_hash": weights_hash, "params": sum(p.numel() for p in model.parameters()),
                          "arch": getattr(model, "arch", None), "device": str(device), "greedy": policy == "greedy",
                          "checkpoint_encoder_version": spec.encoder_version, **extra,
                          **({"spec_compat": compat} if compat["other_differences"] or compat.get("widened_enums")
                             else {})})
        if heads is not None:
            rep["head_readout"] = heads.write(out_dir)
            r = rep["head_readout"]
            log(f"aux heads: per-step win_prob / combat_hp_pred written to {Path(out_dir) / 'games'}; mean win_prob "
                f"at the first decision {r['win_prob_first_mean']}, at the last {r['win_prob_last_mean']}")
        lb = rep["loop_breaker"] = write_loop_stats(breaker, out_dir)
        log(f"loop breaker {lb['mode']} (k {lb['k']}): {lb['loop_breaks']} loop breaks in "
            f"{lb['games_with_loop_breaks']} games, {lb['no_op_actions']} no-op actions in {lb['games_with_no_ops']} "
            f"games, {lb['cycle_revisits']} cycle revisits in {lb['games_with_cycles']} games")
        cy = rep["cycles"] = write_cycle_stats(det, keys, out_dir, mode)
        if fc is not None:
            fo = rep["force"] = write_force_stats(fc, out_dir, keys)
            log(f"forced choice ({force_mode}): {fo['forced_total']} forced decisions ({fo['changed_total']} changed "
                f"the policy's argmax) in {fo['games_forced']} games; per selector {fo['forced_decisions']}")
        rep["eval_mode"] = mode
        log(f"UI revisit cycles ({mode['name']}): {cy['cycles']} (rest {cy['cycles_rest']}, shop "
            f"{cy['cycles_shop']}) in {cy['games_with_cycle']}/{cy['games']} games; stuck {cy['stuck']}")
        return rep
    finally:
        model.train(was_training)


def checkpoint_macros(ck: dict | None) -> str:
    """The room macros ("rest,shop", "" = off) of a checkpoint's training config (``TrainConfig.macros``; checkpoints
    from before the option have none)."""
    return str(((ck or {}).get("config") or {}).get("macros") or "")


def slice_seedset(seedset, chunk: str | None) -> tuple[dict, dict | None]:
    """(the seed set, or its `chunk` = "i/n" slice: the i-th of n equal parts, named ``<name>-slice<i>of<n>``;
    parent record {name, id, sha256, games, chunk} or None)."""
    ss = PG.validate_seedset(dict(seedset)) if isinstance(seedset, dict) else PG.load_seedset(str(seedset))
    if not chunk:
        return ss, None
    ci, cn = (int(x) for x in str(chunk).split("/"))
    if not 0 <= ci < cn:
        raise ValueError(f"chunk {chunk!r}: expected i/n with 0 <= i < n")
    per = -(-len(ss["games"]) // cn)
    games = ss["games"][ci * per:(ci + 1) * per]
    if not games:
        raise ValueError(f"chunk {chunk}: no games ({len(ss['games'])} games in {cn} slices)")
    parent = {"name": ss.get("name"), "id": ss.get("id"), "sha256": PG.checksum(ss), "games": len(ss["games"]),
              "chunk": f"{ci}/{cn}"}
    sub = {k: v for k, v in ss.items() if k not in ("games", "sha256", "id", "frozen", "version")}
    sub.update(name=f"{ss.get('name') or ss.get('id')}-slice{ci}of{cn}", games=games)
    return PG.validate_seedset(sub), parent


def evaluate(seedset: str, checkpoint: str, out_dir: str, *, threads: int = 8, device: str = "cpu",
             step_cap: int = STEP_CAP, floor_step_cap: int = FLOOR_STEP_CAP, loop_breaker: bool = True,
             loop_breaker_k: int = DEFAULT_K, policy: str = "greedy", sample_seed: int = 0, repeats: int = 1,
             chunk: str | None = None, log=print, allow_blind: bool = False,
             blind_purpose: str | None = None, macros=None, force=None, force_mode: str = FORCE_MODES[0]) -> dict:
    """`sts2train eval`: load `checkpoint` for the library sts2vec loads (`load_checkpoint(library=...)`: refused with
    `SpecMismatch` before any game, widened when its embedded enums only gained members) and evaluate it. The weights
    hash is the checkpoint's (a widened model adds only zero rows). `repeats` > 1 (sample mode): ``out_dir/r<k>``
    per repeat and ``out_dir/repeats.json``; the returned report is the first repeat's with ``repeats`` (one row per
    repeat, the pooled progress mean). `macros` None = the checkpoint's training env's (`checkpoint_macros`); "" /
    "none" = off. `force` / `force_mode`: `evaluate_model` (the uniform draw's seed = `sample_seed`)."""
    from sts2vec import load, schema_of

    from .isolation import check_blind_eval
    from .ppo import load_checkpoint

    repeats = int(repeats)
    if repeats < 1 or (repeats > 1 and policy != "sample"):
        raise ValueError(f"repeats {repeats}: >= 1, and > 1 only with --policy sample (greedy plays are identical)")
    check_blind_eval(seedset, allow_blind=allow_blind, purpose=blind_purpose)     # refuse before loading anything
    ss, parent = slice_seedset(seedset, chunk)
    model, spec, ck = load_checkpoint(checkpoint, device, library=ModelSpec.from_schema(schema_of(load())))
    from sts2vec.pool import parse_macros

    macros = ",".join(parse_macros(checkpoint_macros(ck) if macros is None else macros))
    if macros:
        log(f"room macros: {macros}")
    reps = []
    for r in range(repeats):
        out = Path(out_dir) / f"r{r}" if repeats > 1 else Path(out_dir)
        reps.append(evaluate_model(model, spec, ss, str(out), weights_hash=ck.get("weights_hash") or "?",
                                   checkpoint=checkpoint, threads=threads, device=device, step_cap=step_cap,
                                   floor_step_cap=floor_step_cap, spec_compat=ck.get("spec_compat"),
                                   loop_breaker=loop_breaker, loop_breaker_k=loop_breaker_k, policy=policy,
                                   sample_seed=sample_seed, repeat=r, parent_seedset=parent, log=log,
                                   allow_blind=allow_blind, blind_purpose=blind_purpose, macros=macros or None,
                                   force=force, force_mode=force_mode, force_seed=sample_seed))
    if repeats == 1:
        return reps[0]
    rows = [{"repeat": r, "dir": f"r{r}", "games": x["headline"]["games"],
             "progress_mean": x["headline"]["progress_mean"], "replayable": x["headline"]["replayable"],
             "results": x["headline"]["results"], "cycles": x["cycles"]["cycles"],
             "games_with_cycle": x["cycles"]["games_with_cycle"],
             "loop_breaks": (x.get("loop_breaker") or {}).get("loop_breaks")} for r, x in enumerate(reps)]
    games = sum(x["games"] for x in rows)
    pooled = sum(x["progress_mean"] * x["games"] for x in rows) / games if games else None
    summary = {"policy": policy, "breaker": "on" if loop_breaker else "off", "sample_seed": int(sample_seed),
               "repeats": repeats, "checkpoint": str(checkpoint), "weights_hash": ck.get("weights_hash"),
               "seedset": ss.get("name"), "parent_seedset": parent, "runs": rows, "plays": games,
               "progress_mean_pooled": pooled}
    with open(Path(out_dir) / "repeats.json", "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=1)
    rep = dict(reps[0])
    rep["repeats"] = summary
    return rep
