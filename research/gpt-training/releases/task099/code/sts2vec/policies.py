"""Policies for ``sts2vec eval`` (and anything else that steps a VecEnv).

A policy is any callable ``policy(obs, ctx) -> actions``:

- ``obs``: the env's observation dict (name -> ``(N, *shape)`` numpy arrays, see ``sim/ENV_SPEC.md``);
- ``ctx``: a ``PolicyContext`` (schema, which slots need an action, per-slot seeded RNGs, game keys);
- ``actions``: int array ``(N,)``: for every slot with ``ctx.live[i]`` a candidate index in ``[0, obs["cand_n"][i])``
  (row ``i`` of ``obs["cand"]``); other slots are ignored (the evaluator sends -1 for them).

A class is instantiated with no arguments; an instance (or function) is called once per batched step.

Built-ins: ``random`` (uniform over the legal candidates, per-game seeded), ``first`` (always candidate 0) and
``heuristic`` (the hand-written rules in ``heuristic`` below).
"""

from __future__ import annotations

import hashlib
import importlib
import importlib.util
import inspect
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

import numpy as np

from .schema import Schema

Policy = Callable[[dict, "PolicyContext"], np.ndarray]


@dataclass
class PolicyContext:
    """What a policy may use besides the observation. Nothing here carries hidden information."""

    schema: Schema
    num_envs: int
    live: np.ndarray                      # bool [N]: slots that need an action this step
    keys: list[str] = field(default_factory=list)          # "<CHARACTER>-<SEED>" per slot
    ascension: int = 0
    step: int = 0                          # batched step counter (0 at the first decision)
    rngs: list[np.random.Generator] = field(default_factory=list)   # one per slot, seeded from (eval seed, key)

    def cols(self, buffer: str):
        """``ctx.cols("cand").kind`` -> column index (always index tables by field name)."""
        return self.schema.cols(buffer)

    def kind(self, name: str) -> int:
        """RunActionKind value of a name, e.g. ``ctx.kind("EndTurn")``."""
        return self.schema.vocab.enums["RunActionKind"].index_of(name)


def game_rng(seed: int, key: str) -> np.random.Generator:
    """The per-game generator of the seeded policies: sha256(f"{seed}:{key}") — independent of slot order and batch."""
    h = hashlib.sha256(f"{int(seed)}:{key}".encode("utf-8")).digest()
    return np.random.default_rng(int.from_bytes(h[:8], "little"))


# ------------------------------------------------------------------------------------------------ built-ins


def random_policy(obs: dict, ctx: PolicyContext) -> np.ndarray:
    """Uniform over the legal candidates; slot i draws from ``ctx.rngs[i]`` only when it acts, so a game's choices
    do not depend on which other games share the batch."""
    n = obs["cand_n"]
    a = np.full(ctx.num_envs, -1, dtype=np.int32)
    for i in np.flatnonzero(ctx.live & (n > 0)):
        a[i] = int(ctx.rngs[i].integers(0, int(n[i])))
    return a


def first_policy(obs: dict, ctx: PolicyContext) -> np.ndarray:
    """Always the first candidate (the sim's legal-action order: indexed actions first, leave/confirm last)."""
    return np.zeros(ctx.num_envs, dtype=np.int32)


class HeuristicPolicy:
    """A simple, documented rule policy (the "heuristic" baseline). Per slot, in this order:

    1. Prompt (any PromptSelect / PromptConfirm / PromptCancel candidate): confirm when PromptConfirm is listed,
       else select the first listed option.
    2. Combat with a playable card (any PlayCard candidate; listed only when energy allows):
       a. if incoming attack damage exceeds the current block, the card with the largest ``hand_f.block_eff > 0``;
       b. else a Power card;
       c. else an Attack aimed at the live enemy with the lowest hp (or an untargeted Attack);
       d. else any other card (first listed).
       Potions are never used.
    3. EndTurn when listed (combat with nothing to play).
    4. Open card screen (after a REROLL, or a reward that allows more picks): pick the first card (PickRewardCard with
       the lowest option index).
    5. Rewards screen: claim the rewards in screen order — a one-click ClaimReward (potion rewards are skipped while the
       belt is full; the env no longer lists a claim the game would refuse), or for a card reward its first offered card
       (TakeRewardCard: open the reward and take card 0 in one decision, which is what this rule did in two steps before
       the rewards-screen macros) — then ProceedRewards.
    6. Map: travel to the first option (lowest column).
    7. Rest site: HEAL when hp < 50 % of max (else SMITH; else the first option); LeaveRestSite when nothing is left.
    8. Treasure: OpenChest, then TakeTreasureRelic.
    9. Shop: LeaveShop (buys nothing). Event: the first option, else LeaveEvent.
    10. Anything else: candidate 0.
    """

    def __init__(self):
        self._bound: Schema | None = None

    def _bind(self, s: Schema) -> None:
        k = s.vocab.enums["RunActionKind"]
        self.K = {name: k.index_of(name) for name in k}
        self.C = s.cols("cand")
        self.G = s.cols("global_i")
        self.GF = s.cols("global_f")
        self.H = s.cols("hand")
        self.HF = s.cols("hand_f")
        self.E = s.cols("enemies")
        self.R = s.cols("rewards")
        self.P = s.cols("potions")
        self.REST = s.cols("rest")
        ct = s.vocab.enums["CardType"]
        self.ATTACK, self.SKILL, self.POWER = ct.index_of("Attack"), ct.index_of("Skill"), ct.index_of("Power")
        self.POTION_REWARD = s.vocab.enums["RewardType"].index_of("Potion")
        self.TAKE_CARD = self.K.get("TakeRewardCard", -999)          # rewards-screen macro kind (libraries since 2026-11-04)
        ro = s.vocab.enums["RestOption"]
        self.HEAL, self.SMITH = ro.lookup.get("HEAL", -99), ro.lookup.get("SMITH", -99)
        self._bound = s

    def __call__(self, obs: dict, ctx: PolicyContext) -> np.ndarray:
        if self._bound is not ctx.schema:
            self._bind(ctx.schema)
        a = np.full(ctx.num_envs, -1, dtype=np.int32)
        for i in np.flatnonzero(ctx.live & (obs["cand_n"] > 0)):
            a[i] = self.choose(obs, int(i))
        return a

    def choose(self, obs: dict, i: int) -> int:
        K, C = self.K, self.C
        n = int(obs["cand_n"][i])
        cand = obs["cand"][i, :n]
        kinds = cand[:, C.kind]

        def first(kind: str) -> int | None:
            hit = np.flatnonzero(kinds == K[kind])
            return int(hit[0]) if len(hit) else None

        # 1. prompts
        if np.isin(kinds, [K["PromptSelect"], K["PromptConfirm"], K["PromptCancel"]]).any():
            for k in ("PromptConfirm", "PromptSelect"):
                j = first(k)
                if j is not None:
                    return j
            return 0
        # 2. combat card plays
        plays = np.flatnonzero(kinds == K["PlayCard"])
        if len(plays):
            return self._combat(obs, i, cand, plays)
        # 3. end turn
        j = first("EndTurn")
        if j is not None:
            return j
        # 4. card reward
        j = first("PickRewardCard")
        if j is not None:
            return j
        # 5. rewards screen: claims in screen order (a card reward's TakeRewardCard rows sit where its ClaimReward was,
        #    card 0 first, so the first claim row of a card reward takes its first card)
        claims = np.flatnonzero((kinds == K["ClaimReward"]) | (kinds == self.TAKE_CARD))
        if len(claims):
            pots = obs["potions"][i]
            belt_full = int(np.count_nonzero(pots[:, self.P.potion_id])) >= int(obs["global_i"][i, self.G.potion_slots])
            for j in claims:
                if kinds[j] == self.TAKE_CARD:
                    return int(j)
                r = int(cand[j, C.option_index])
                if belt_full and 0 <= r < obs["rewards"].shape[1] and \
                        int(obs["rewards"][i, r, self.R.reward_type]) == self.POTION_REWARD:
                    continue
                return int(j)
        for k in ("ProceedRewards", "Travel"):        # 5 (proceed), 6 (map)
            j = first(k)
            if j is not None:
                return j
        # 7. rest site
        rests = np.flatnonzero(kinds == K["ChooseRestOption"])
        if len(rests):
            ids = [int(obs["rest"][i, int(cand[j, C.option_index]), self.REST.option_id]) for j in rests]
            want = self.HEAL if float(obs["global_f"][i, self.GF.hp_frac]) < 0.5 else self.SMITH
            for j, oid in zip(rests, ids):
                if oid == want:
                    return int(j)
            return int(rests[0])
        # 8-9. treasure, shop, event
        for k in ("OpenChest", "TakeTreasureRelic", "ChooseEventOption", "LeaveShop", "LeaveRestSite", "LeaveEvent"):
            j = first(k)
            if j is not None:
                return j
        return 0

    def _combat(self, obs: dict, i: int, cand: np.ndarray, plays: np.ndarray) -> int:
        C, H, G = self.C, self.H, self.G
        gi = obs["global_i"][i]
        hand = obs["hand"][i]
        slot = cand[plays, C.hand_slot]
        ctype = hand[slot, H.type]
        # a. block when the enemies' visible attack intents exceed the current block
        if int(gi[G.incoming_damage]) > int(gi[G.block]):
            blk = obs["hand_f"][i, slot, self.HF.block_eff]
            if (blk > 0).any():
                return int(plays[int(np.argmax(blk))])
        # b. powers
        p = plays[ctype == self.POWER]
        if len(p):
            return int(p[0])
        # c. attacks at the lowest-hp live enemy (or untargeted)
        att = plays[ctype == self.ATTACK]
        if len(att):
            en = obs["enemies"][i]
            alive = np.flatnonzero(en[:, self.E.present] > 0)
            if len(alive):
                low = int(alive[np.argmin(en[alive, self.E.hp])])
                tgt = cand[att, C.target_slot]
                hit = att[(tgt == low) | (tgt < 0)]
                if len(hit):
                    return int(hit[0])
            return int(att[0])
        # d. anything else playable
        return int(plays[0])


BUILTINS: dict[str, Callable[[], Policy]] = {
    "random": lambda: random_policy,
    "first": lambda: first_policy,
    "heuristic": HeuristicPolicy,
}


def resolve(spec) -> tuple[Policy, str, str]:
    """``spec`` -> (policy callable, display name, policy hash).

    Accepted: a built-in name (``random`` / ``heuristic`` / ``first``), ``package.module:callable``,
    ``path/to/file.py:callable``, or a callable. Classes are instantiated with no arguments. The hash is
    ``builtin:<name>`` for built-ins and the sha256 (first 16 hex) of the defining source file otherwise.
    """
    if callable(spec) and not isinstance(spec, str):
        obj, name = spec, getattr(spec, "__name__", type(spec).__name__)
        src = _source_hash(obj)
        return (obj() if inspect.isclass(obj) else obj), name, src
    spec = str(spec)
    if spec in BUILTINS:
        return BUILTINS[spec](), spec, f"builtin:{spec}"
    if ":" not in spec:
        raise ValueError(f"policy {spec!r}: expected one of {sorted(BUILTINS)} or 'module:callable' / 'file.py:callable'")
    mod_name, attr = spec.rsplit(":", 1)
    if mod_name.endswith(".py") or "/" in mod_name or "\\" in mod_name:
        path = Path(mod_name).resolve()
        if not path.exists():
            raise FileNotFoundError(f"policy file {path} does not exist")
        uniq = "sts2vec_user_policy_" + hashlib.sha256(str(path).encode()).hexdigest()[:12]
        mspec = importlib.util.spec_from_file_location(uniq, path)
        module = importlib.util.module_from_spec(mspec)
        sys.modules[uniq] = module
        mspec.loader.exec_module(module)
        label = f"{path.name}:{attr}"
    else:
        module = importlib.import_module(mod_name)
        label = spec
    obj = module
    for part in attr.split("."):
        obj = getattr(obj, part)
    if inspect.isclass(obj):
        obj = obj()
    if not callable(obj):
        raise TypeError(f"policy {spec!r} is not callable")
    return obj, label, _source_hash(module)


def _source_hash(obj) -> str:
    try:
        f = inspect.getsourcefile(obj if inspect.ismodule(obj) or inspect.isclass(obj) or inspect.isfunction(obj)
                                  else type(obj))
        return "sha256:" + hashlib.sha256(Path(f).read_bytes()).hexdigest()[:16]
    except (TypeError, OSError):
        return "unknown"
