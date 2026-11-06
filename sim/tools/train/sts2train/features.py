"""Observation plumbing between sts2vec numpy buffers and the torch model.

`ModelSpec` captures everything the model needs from the published schema (vocab sizes, enum sizes, column indices,
capacities) so a checkpoint can rebuild its model without a live library, and records the schema's encoder_version.
Tables are always indexed by the schema's field names (never hard-coded offsets).

`check_spec_compat` decides whether a checkpoint's spec can drive this library (``--resume``, ``sts2train eval``): the
model's inputs must be identical, while ``encoder_version`` and anything the model does not read may differ (a schema
append such as fc2eb34c07bc8513 -> 5dbfa09dd9dcb5ed adds buffers and enum members but leaves the 27 inputs unchanged).
One model-input change is accepted as well: an embedded enum that gained members appended at its end ("widened", e.g.
RunActionKind 21 -> 23 with the rewards-screen macros of e9baaefc39811631). Every old value keeps its meaning, so the
checkpoint's embedding rows are kept and the new members' rows start at zero (``model.widen_state_dict``).
"""

from __future__ import annotations

import copy
from dataclasses import asdict, dataclass, field

import numpy as np
import torch

# Buffers the model reads (everything else in `obs` is ignored).
USED_BUFFERS = (
    "global_i", "global_f", "hand", "hand_f", "hand_dmg", "deck", "draw_counts", "discard_counts", "exhaust_counts",
    "relics", "potions", "powers_player", "orbs", "enemies", "enemy_intents", "enemy_powers", "allies", "map_nodes",
    "rewards", "card_reward", "card_reward_alts", "reward_choices", "shop", "rest", "event_options", "prompt_options", "cand", "cand_n",
)
# Buffers whose leading row axis is truncated (training stores at most this many rows; eval passes all of them).
ROW_TRUNCATED = ("cand", "prompt_options")

REWARD_INPUT_ARCH = "reward_choices_v1"
PUBLIC_HAND_INPUT_ARCH = "reward_choices_public_handselect_v1"
INPUT_ARCHES = (REWARD_INPUT_ARCH, PUBLIC_HAND_INPUT_ARCH)

def checked_input_arch(marker):
    if type(marker) is not str or marker not in INPUT_ARCHES:
        raise ValueError("unknown or untyped public input architecture")
    return marker


ENUMS = ("RunPhase", "RunActionKind", "IntentType", "MapPointType", "RewardType", "ChoicePromptKind", "CardType",
         "TargetType", "CardRarity", "RestOption", "CardRewardAlt", "ShopCategory", "PileType", "RelicStatus",
         "PromptItemKind")
# Vocabulary lists the model embeds (model.Policy; ENUMS are the enums it embeds). tests/test_resume_compat.py pins
# both tuples to the model's parameter count.
MODEL_VOCABS = ("cards", "relics", "potions", "powers", "monsters", "encounters", "events", "orbs", "enchantments",
                "afflictions", "acts", "characters")


@dataclass
class ModelSpec:
    encoder_version: str
    vocab: dict[str, int]                     # list name -> size (cards, relics, potions, ...)
    enums: dict[str, int]                     # enum name -> size
    enum_names: dict[str, list[str]]          # member names of the embedded enums (ENUMS): pointer wiring
                                              # (RunActionKind / PromptItemKind) and widening checks; specs written
                                              # before 2026-11-04 hold RunActionKind / PromptItemKind / RewardType only
    cols: dict[str, dict[str, int]]           # buffer -> field -> column
    shapes: dict[str, list[int]]              # buffer -> per-env shape
    card_vars: int
    aux_fields: list[str] = field(default_factory=list)

    input_arch: str = ""                       # pre-reward checkpoints have no input architecture marker

    @classmethod
    def from_schema(cls, schema, *, input_arch=REWARD_INPUT_ARCH) -> "ModelSpec":
        checked_input_arch(input_arch)
        vocab = {k: len(v) for k, v in schema.vocab.lists.items()}
        enums = {k: len(v) for k, v in schema.vocab.enums.items()}
        names = {k: [schema.vocab.enums[k].name(i) for i in range(len(schema.vocab.enums[k]))] for k in ENUMS}
        cols, shapes = {}, {}
        if "reward_choices" not in schema:
            raise SpecMismatch("reward_choices_v1 requires the native reward_choices buffer")
        for name in USED_BUFFERS + ("aux", "last_episode"):
            b = schema[name]
            shapes[name] = list(b.shape)
            if b.fields:
                cols[name] = {f: i for i, f in enumerate(b.fields)}
        required_kinds = {"TakeRewardCard", "TakeRewardAlternative"}
        required_cols = set(cols["hand"]) | {"reward_index", "option", "alt_id"}
        if not required_kinds.issubset(names["RunActionKind"]) or not required_cols.issubset(cols.get("reward_choices", {})):
            raise SpecMismatch("reward_choices_v1 requires the native reward choice table and both reward macro kinds")
        return cls(input_arch=input_arch, encoder_version=schema.encoder_version, vocab=vocab, enums=enums, enum_names=names, cols=cols,
                   shapes=shapes, card_vars=len(schema.card_vars), aux_fields=list(schema["aux"].fields))

    def to_json(self) -> dict:
        return asdict(self)

    @classmethod
    def from_json(cls, d: dict) -> "ModelSpec":
        return cls(**d)


def public_hand_slots(spec, x):
    """B0 public-only proof, returning candidate hand indices or -1 without mutating x.

    Native hand_n/prompt_option_n are pre-clamp counts. cand_n is clamped at
    native cap, so cap equality is conservatively unknown. No global status
    claim: unrelated native table flags are not model inputs.
    """
    hand, options, cand = x["hand"], x["prompt_options"], x["cand"]
    batch, width = cand.shape[:2]
    out = torch.full((batch, width), -1, dtype=torch.long, device=cand.device)
    H, P = hand.shape[1], options.shape[1]
    O = min(H, P)
    if width == 0 or O == 0:
        return out
    g, h, o, c = (spec.cols[k] for k in ("global_i", "hand", "prompt_options", "cand"))
    gi = x["global_i"]
    hn, pn, cn = gi[:, g["hand_n"]], gi[:, g["prompt_option_n"]], x["cand_n"]
    hi = torch.arange(H, device=hand.device)
    oi = torch.arange(O, device=hand.device)
    hp = hi[None, :] < hn[:, None]
    op = oi[None, :] < pn[:, None]
    hid, hup = hand[..., h["card_id"]], hand[..., h["upgrade"]]
    opts = options[:, :O]
    pid, pup = opts[..., o["item_id"]], opts[..., o["upgrade"]]
    active = ((gi[:, g["decision_kind"]] == spec.enum_names["RunPhase"].index("Prompt"))
              & (gi[:, g["prompt_kind"]] == spec.enum_names["ChoicePromptKind"].index("HandSelect"))
              & (gi[:, g["prompt_pile"]] == spec.enum_names["PileType"].index("Hand"))
              & (gi[:, g["is_combat"]] == 1))
    complete = ((hn > 0) & (hn <= H) & (pn > 0) & (pn <= O) & (pn <= hn)
                & (cn > 0) & (cn <= width) & (cn < spec.shapes["cand"][0]))
    # Known identities only. '?' cannot prove card identity; hand padding must
    # agree with true hand_n, not quietly introduce stale visible cards.
    complete &= torch.where(hp, (hid > 1) & (hid < spec.vocab["cards"]) & (hup >= 0), (hid == 0) & (hup == 0)).all(1)
    good_option = ((pid > 1) & (pid < spec.vocab["cards"]) & (pup >= 0)
                   & (opts[..., o["kind_of_item"]] == spec.enum_names["PromptItemKind"].index("card"))
                   & (opts[..., o["ordinal"]] == oi[None, :]))
    complete &= (~op | good_option).all(1)
    matches = ((pid[:, :, None] == hid[:, None, :]) & (pup[:, :, None] == hup[:, None, :])
               & op[:, :, None] & hp[:, None, :])
    nmatch = matches.sum(2)
    complete &= (~op | (nmatch > 0)).all(1)
    # The native HandSelect is an ordered subset without replacement. Reject
    # stale/foreign snapshots whose public multiplicities cannot be a subset.
    hand_same = ((hid[:, :, None] == hid[:, None, :]) & (hup[:, :, None] == hup[:, None, :])
                 & hp[:, :, None] & hp[:, None, :]).sum(2)
    complete &= (~hp | (matches.sum(1) <= hand_same)).all(1)
    ordered_equal = (~op | ((pid == hid[:, :O]) & (pup == hup[:, :O]))).all(1)
    full = (pn == hn) & ordered_equal
    filtered = pn < hn
    unique = matches.to(torch.int64).argmax(2)
    option_slots = torch.where(full[:, None], oi[None, :].expand(batch, -1),
                               torch.where(filtered[:, None] & (nmatch == 1), unique, -1))
    option_slots = torch.where((active & complete)[:, None] & op, option_slots, -1)
    index = cand[..., c["option_index"]].long()
    safe = index.clamp(0, O - 1)
    slots = option_slots.gather(1, safe)
    item = pid.gather(1, safe)
    eligible = ((torch.arange(width, device=cand.device)[None, :] < cn[:, None])
                & (cand[..., c["kind"]] == spec.enum_names["RunActionKind"].index("PromptSelect"))
                & (index >= 0) & (index < pn[:, None]) & (index < O)
                & (cand[..., c["A"]] == index) & (cand[..., c["card_id"]] == item))
    return torch.where(eligible, slots, out)


class SpecMismatch(ValueError):
    """A checkpoint's model inputs differ from the library's (`check_spec_compat`)."""


def _cols_delta(old: dict | None, new: dict | None) -> str:
    if old is None or new is None:
        return f"{'absent' if old is None else len(old)} -> {'absent' if new is None else len(new)} columns"
    parts = [f"-{k}" for k in old if k not in new] + [f"+{k}" for k in new if k not in old]
    parts += [f"{k} {old[k]}->{new[k]}" for k in old if k in new and old[k] != new[k]]
    return ", ".join(parts[:8]) + (f" (+{len(parts) - 8} more)" if len(parts) > 8 else "")


def _widening(o_size, n_size, o_names, n_names) -> dict | None:
    """{"from", "to", "added"} when an embedded enum grew only by members appended at its end, else None. Verified by
    member names: both specs must record them (`ModelSpec.enum_names`) and the checkpoint's must be the library's
    first members, so an inserted, renamed or reordered member (every later value shifts) is never taken for one."""
    if not (isinstance(o_size, int) and isinstance(n_size, int) and 0 < o_size < n_size):
        return None
    if o_names is None or n_names is None or len(o_names) != o_size or len(n_names) != n_size:
        return None
    if list(n_names[:o_size]) != list(o_names):
        return None
    return {"from": o_size, "to": n_size, "added": list(n_names[o_size:])}


def describe_widened(widened: dict) -> str:
    """'RunActionKind 21 -> 23 (+TakeRewardCard, +TakeRewardAlternative)' for each widened enum, '; '-joined."""
    return "; ".join(f"{k} {w['from']} -> {w['to']} (" + ", ".join(f"+{x}" for x in w["added"]) + ")"
                     for k, w in sorted(widened.items()))


def spec_diff(old: "ModelSpec | dict", new: "ModelSpec | dict") -> tuple[list[str], dict[str, dict], list[str]]:
    """(model-input differences, widened enums, other differences) between a checkpoint's spec `old` and the
    library's `new`.

    Model inputs (what the weights, the column lookups and the pointer wiring depend on; compared exactly): shape and
    column map of every buffer in USED_BUFFERS, the size of every vocabulary list the model embeds (MODEL_VOCABS) and
    of every enum it embeds (ENUMS), the member names recorded in `enum_names`, `card_vars`. The one exception is an
    embedded enum that only gained members appended at its end, checked by member names (`_widening`): it is listed
    in `widened` ({enum: {"from", "to", "added"}}), not as a difference, because the checkpoint can drive the library
    once that enum's embedding tables are widened (old rows kept, new rows zero: `model.widen_state_dict`). Shrunk,
    reordered or renamed members, and growth of an enum whose member names the checkpoint does not record, stay
    model-input differences. Anything else may differ and is only reported: `encoder_version` (it changes with every
    schema edit; appended buffers are not part of a spec at all), other enums (EndKind, ItemKind, ...) and vocabulary
    lists, and the aux / last_episode blocks (the trainer reads those by field name from the live schema, never
    through the checkpoint)."""
    o = old.to_json() if isinstance(old, ModelSpec) else dict(old)
    n = new.to_json() if isinstance(new, ModelSpec) else dict(new)
    block: list[str] = []
    other: list[str] = []
    widened: dict[str, dict] = {}
    if o.get("input_arch", "") != n.get("input_arch", ""):
        block.append(f"input architecture {o.get('input_arch') or 'baseline'} -> {n.get('input_arch') or 'baseline'}")
    if o.get("encoder_version") != n.get("encoder_version"):
        other.append(f"encoder_version {o.get('encoder_version')} -> {n.get('encoder_version')}")
    oshp, nshp, ocol, ncol = o.get("shapes", {}), n.get("shapes", {}), o.get("cols", {}), n.get("cols", {})
    for b in USED_BUFFERS:
        if oshp.get(b) != nshp.get(b):
            block.append(f"buffer {b}: shape {oshp.get(b)} -> {nshp.get(b)}")
        if ocol.get(b) != ncol.get(b):
            block.append(f"buffer {b}: columns {_cols_delta(ocol.get(b), ncol.get(b))}")
    for b in ("aux", "last_episode"):
        if oshp.get(b) != nshp.get(b) or ocol.get(b) != ncol.get(b):
            other.append(f"buffer {b}: {_cols_delta(ocol.get(b), ncol.get(b))} (read by name from the library)")
    ov, nv = o.get("vocab", {}), n.get("vocab", {})
    for k in sorted(set(ov) | set(nv) | set(MODEL_VOCABS)):
        if ov.get(k) == nv.get(k):
            continue
        txt = f"vocab {k}: {ov.get(k, 'absent')} -> {nv.get(k, 'absent')}"
        if k in MODEL_VOCABS:
            block.append(txt)
        else:
            other.append(txt + " (not read by the model)")
    oe, ne = o.get("enums", {}), n.get("enums", {})
    on, nn_ = o.get("enum_names", {}), n.get("enum_names", {})
    for k in sorted(set(oe) | set(ne)):
        if oe.get(k) == ne.get(k):
            continue
        txt = f"enum {k}: {oe.get(k, 'absent')} -> {ne.get(k, 'absent')}"
        if k not in ENUMS:
            other.append(txt + " (not read by the model)")
            continue
        w = _widening(oe.get(k), ne.get(k), on.get(k), nn_.get(k))
        if w is not None:
            widened[k] = w
        elif isinstance(oe.get(k), int) and isinstance(ne.get(k), int) and oe[k] < ne[k] and k not in on:
            block.append(txt + " (the checkpoint records no member names, so appended members cannot be told from "
                               "inserted ones)")
        else:
            block.append(txt)
    for k in sorted(set(on) | set(nn_)):
        if k in on and k not in widened and on.get(k) != nn_.get(k):
            a, b = on.get(k) or [], nn_.get(k) or []
            moved = [x for x in a if x in b and a.index(x) != b.index(x)]
            block.append(f"enum {k} members: -{[x for x in a if x not in b]} +{[x for x in b if x not in a]}"
                         + (f" moved {moved[:6]}" if moved else ""))
    if o.get("card_vars") != n.get("card_vars"):
        block.append(f"card_vars {o.get('card_vars')} -> {n.get('card_vars')}")
    return block, widened, other


def check_spec_compat(old: "ModelSpec | dict", new: "ModelSpec | dict", *, what: str = "resume") -> dict:
    """Accept a checkpoint spec `old` for the library spec `new` when the model's inputs are identical or only widened
    (`spec_diff`), else raise `SpecMismatch` listing every difference. Returns the record kept in config.json:
    ``{"checkpoint_encoder_version", "library_encoder_version", "model_inputs": "identical" | "widened",
    "widened_enums": {enum: {"from", "to", "added"}}, "other_differences"}``; with "widened" the model is built from
    `widen_spec(old, new, ...)` (= the library's sizes) and loads the checkpoint through `model.widen_state_dict`."""
    block, widened, other = spec_diff(old, new)
    o = old.to_json() if isinstance(old, ModelSpec) else old
    n = new.to_json() if isinstance(new, ModelSpec) else new
    ov, nv = o.get("encoder_version"), n.get("encoder_version")
    if block:
        hint = ("resume with the library the checkpoint was trained on (its bundle), or start a fresh run (a new --out)"
                if what == "resume" else "evaluate with the library the checkpoint was trained on (its bundle)")
        raise SpecMismatch(
            f"cannot {what}: the checkpoint's model inputs differ from this library's (checkpoint encoder_version "
            f"{ov}, library {nv}):\n  - " + "\n  - ".join(block)
            + "\nOnly an encoder_version change, embedded enums that gained members at their end (widened: the "
            "checkpoint's embedding rows are kept, the new rows start at zero) and changes outside the model's inputs "
            f"(appended buffers, enums the model does not embed, aux / last_episode columns) are accepted; {hint}.")
    return {"checkpoint_encoder_version": ov, "library_encoder_version": nv,
            "model_inputs": "widened" if widened else "identical", "widened_enums": widened,
            "other_differences": other}


def widen_spec(old: "ModelSpec | dict", new: "ModelSpec | dict", widened: dict) -> ModelSpec:
    """The checkpoint spec `old` with the sizes and member names of the `widened` enums taken from `new` (everything
    else, encoder_version included, stays the checkpoint's): the spec of the model that runs a widened checkpoint on
    `new`. Its model inputs equal `new`'s (`spec_diff` reports only differences outside them)."""
    d = copy.deepcopy(old.to_json() if isinstance(old, ModelSpec) else dict(old))
    n = new.to_json() if isinstance(new, ModelSpec) else new
    for k in widened:
        d["enums"][k] = n["enums"][k]
        d.setdefault("enum_names", {})[k] = list(n["enum_names"][k])
    return ModelSpec.from_json(d)


TORCH_DTYPES = {np.dtype(np.int8): torch.int8, np.dtype(np.int16): torch.int16, np.dtype(np.int32): torch.int32,
                np.dtype(np.int64): torch.int64, np.dtype(np.uint8): torch.uint8, np.dtype(np.float16): torch.float16,
                np.dtype(np.float32): torch.float32, np.dtype(np.float64): torch.float64}


def _row_need(obs: dict, max_rows: int | None) -> tuple[int, int]:
    """(candidate rows, prompt-option rows) a batch needs: the largest cand_n / prompt_option_n, capped at max_rows."""
    cand_n = obs["cand_n"]
    gi = obs["global_i"]
    c_need = int(cand_n.max()) if len(cand_n) else 1
    p_need = int(gi[:, _PROMPT_N_COL].max()) if len(gi) else 1
    c_cap = obs["cand"].shape[1] if max_rows is None else min(max_rows, obs["cand"].shape[1])
    p_cap = obs["prompt_options"].shape[1] if max_rows is None else min(max_rows, obs["prompt_options"].shape[1])
    return max(1, min(c_need, c_cap)), max(1, min(p_need, p_cap))


def _static_need(obs: dict, max_rows: int | None) -> tuple[int, int]:
    """(candidate rows, prompt-option rows) of the static-shape path: `max_rows` capped at the table widths."""
    c_w, p_w = obs["cand"].shape[1], obs["prompt_options"].shape[1]
    return (c_w, p_w) if max_rows is None else (min(max_rows, c_w), min(max_rows, p_w))


def obs_to_device(obs: dict[str, np.ndarray], device: torch.device | str = "cpu", *,
                  max_rows: int | None = None, static: bool = False) -> dict[str, torch.Tensor]:
    """The model's input tensors in their source dtypes (int16 pile counts stay int16), copied to ``device``.

    `cand` / `prompt_options` are cut to the rows the batch needs (`_row_need`), or with `static` to exactly
    `max_rows` rows (capped at the table widths) so every call has the same shapes (compiled / CUDA-graph forwards).
    The env may overwrite its buffers right after this returns: on CUDA each buffer is first copied into pinned host
    memory and then uploaded asynchronously (no stream sync per buffer); on cpu / mps the copies are synchronous (on a
    cpu device the tensors may alias the env buffers until then)."""
    c, p = _static_need(obs, max_rows) if static else _row_need(obs, max_rows)
    device = torch.device(device)
    pinned = device.type == "cuda"
    out = {}
    for name in USED_BUFFERS:
        a = obs[name]
        if name == "cand":
            a = a[:, :c]
        elif name == "prompt_options":
            a = a[:, :p]
        t = torch.from_numpy(np.ascontiguousarray(a))
        out[name] = t.pin_memory().to(device, non_blocking=True) if pinned else t.to(device)
    return out


def obs_to_torch(obs: dict[str, np.ndarray], device: torch.device | str = "cpu", *, max_rows: int | None = None,
                 index: np.ndarray | None = None) -> dict[str, torch.Tensor]:
    """The model's input tensors from an obs dict (env views or stored rollout arrays).

    `index` selects rows (minibatch). `cand` / `prompt_options` are cut to the largest row count actually needed in the
    batch (max cand_n / prompt_option_n), capped at `max_rows` when given.
    """
    out = {}
    cand_n = obs["cand_n"] if index is None else obs["cand_n"][index]
    for name in USED_BUFFERS:
        a = obs[name] if index is None else obs[name][index]
        out[name] = a
    c_need = int(cand_n.max()) if len(cand_n) else 1
    gi = out["global_i"]
    p_need = int(gi[:, _PROMPT_N_COL].max()) if len(gi) else 1
    c_cap = out["cand"].shape[1] if max_rows is None else min(max_rows, out["cand"].shape[1])
    p_cap = out["prompt_options"].shape[1] if max_rows is None else min(max_rows, out["prompt_options"].shape[1])
    out["cand"] = out["cand"][:, :max(1, min(c_need, c_cap))]
    out["prompt_options"] = out["prompt_options"][:, :max(1, min(p_need, p_cap))]
    t = {}
    for k, a in out.items():
        a = np.ascontiguousarray(a)
        if a.dtype == np.int16 or a.dtype == np.uint8:
            a = a.astype(np.int32)
        t[k] = torch.from_numpy(a).to(device, non_blocking=True)
    return t


# Set by `bind_spec` (column of global_i.prompt_option_n); default matches the published schema.
_PROMPT_N_COL = 42


def bind_spec(spec: ModelSpec) -> None:
    global _PROMPT_N_COL
    _PROMPT_N_COL = spec.cols["global_i"]["prompt_option_n"]


class RolloutStore:
    """Preallocated [T, N, ...] observation tensors on ``device`` (source dtypes; cand / prompt_options padded to
    `max_rows` rows) + numpy per-step scalars (actions, log-probs, values, rewards, dones, valid).

    The rollout writes the very tensors it ran inference on (`put`), so a GPU learner never copies observations back
    to the host; `batch(index)` gathers a minibatch on the device and cuts `cand` / `prompt_options` to its needs,
    computed from host copies of cand_n / prompt_option_n (`rows`), so a minibatch costs no device->host sync."""

    def __init__(self, spec: ModelSpec, steps: int, num_envs: int, max_rows: int, dtypes: dict[str, np.dtype],
                 device: torch.device | str = "cpu", *, transition_aux: bool = False):
        self.T, self.N, self.max_rows = steps, num_envs, max_rows
        self.device = torch.device(device)
        self._prompt_col = spec.cols["global_i"]["prompt_option_n"]
        self.obs: dict[str, torch.Tensor] = {}
        for name in USED_BUFFERS:
            shape = list(spec.shapes[name])
            if name in ROW_TRUNCATED:
                shape[0] = min(shape[0], max_rows)
            self.obs[name] = torch.zeros((steps, num_envs, *shape), dtype=TORCH_DTYPES[np.dtype(dtypes[name])],
                                         device=self.device)
        self.actions = np.zeros((steps, num_envs), dtype=np.int64)
        self.logp = np.zeros((steps, num_envs), dtype=np.float32)
        self.values = np.zeros((steps, num_envs), dtype=np.float32)
        self.rewards = np.zeros((steps, num_envs), dtype=np.float32)
        self.dones = np.zeros((steps, num_envs), dtype=np.float32)
        self.valid = np.zeros((steps, num_envs), dtype=bool)
        self.cand_n_h = np.zeros((steps, num_envs), dtype=np.int64)       # host copies of the row counts (`rows`)
        self.prompt_n_h = np.zeros((steps, num_envs), dtype=np.int64)
        self.transition_y = np.zeros((steps, num_envs, 4), dtype=np.float32) if transition_aux else None
        self.transition_mask = np.zeros((steps, num_envs, 4), dtype=bool) if transition_aux else None

    def put_transition(self, t, target, mask):
        if self.transition_y is None or target.shape != (self.N, 4) or mask.shape != (self.N, 4):
            raise ValueError("transition rollout storage unavailable or misaligned")
        if not np.isfinite(target[mask]).all():
            raise FloatingPointError("nonfinite transition label")
        self.transition_y[t] = np.where(mask, target, 0)
        self.transition_mask[t] = mask

    def put(self, t: int, x: dict[str, torch.Tensor], obs: dict[str, np.ndarray] | None = None) -> None:
        """Store step t from `obs_to_device` tensors (row-cut buffers are zero-padded to `max_rows`). `obs` = the host
        observations `x` was made from (their cand_n / prompt_option_n feed `rows`; read back from `x` if not given)."""
        for name in USED_BUFFERS:
            dst, src = self.obs[name][t], x[name]
            if name in ROW_TRUNCATED:
                w = min(src.shape[1], dst.shape[1])
                dst[:, :w].copy_(src[:, :w])
                dst[:, w:].zero_()
            else:
                dst.copy_(src)
        if obs is None:
            obs = {"cand_n": x["cand_n"].cpu().numpy(), "global_i": x["global_i"].cpu().numpy()}
        self.cand_n_h[t] = obs["cand_n"]
        self.prompt_n_h[t] = obs["global_i"][:, self._prompt_col]

    def put_obs(self, t: int, obs: dict[str, np.ndarray]) -> None:
        self.put(t, obs_to_device(obs, self.device, max_rows=self.max_rows), obs)

    def rows(self, index: np.ndarray) -> tuple[int, int]:
        """(candidate rows, prompt-option rows) the flat samples `index` need: their largest cand_n /
        prompt_option_n clamped to [1, stored width], from the host copies (no device sync)."""
        index = np.asarray(index)
        if not len(index):
            return 1, 1
        c = int(self.cand_n_h.reshape(-1)[index].max())
        p = int(self.prompt_n_h.reshape(-1)[index].max())
        return max(1, min(c, self.obs["cand"].shape[2])), max(1, min(p, self.obs["prompt_options"].shape[2]))

    def batch(self, index: torch.Tensor | np.ndarray, *, rows: tuple[int, int] | None = None,
              static: bool = False) -> dict[str, torch.Tensor]:
        """Model inputs of the flat (t * N + i) samples `index`, gathered on the device. `cand` / `prompt_options` are
        cut to `rows` (default: `rows(index)`, what the samples need); `static` keeps the stored `max_rows` width, so
        every minibatch has the same shapes (compiled / CUDA-graph updates)."""
        idx = torch.as_tensor(index, device=self.device, dtype=torch.long)
        x = {k: v.flatten(0, 1).index_select(0, idx) for k, v in self.obs.items()}
        if static:
            return x
        if rows is None:
            rows = self.rows(index.cpu().numpy() if torch.is_tensor(index) else index)
        x["cand"] = x["cand"][:, :rows[0]]
        x["prompt_options"] = x["prompt_options"][:, :rows[1]]
        return x

    @property
    def nbytes(self) -> int:
        return sum(v.numel() * v.element_size() for v in self.obs.values())
