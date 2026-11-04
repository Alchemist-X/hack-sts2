"""Observation plumbing between sts2vec numpy buffers and the torch model.

`ModelSpec` captures everything the model needs from the published schema (vocab sizes, enum sizes, column indices,
capacities) so a checkpoint can rebuild its model without a live library, and records the schema's encoder_version.
Tables are always indexed by the schema's field names (never hard-coded offsets).

`check_spec_compat` decides whether a checkpoint's spec can drive this library (``--resume``, ``sts2train eval``): the
model's inputs must be identical, while ``encoder_version`` and anything the model does not read may differ (a schema
append such as fc2eb34c07bc8513 -> 5dbfa09dd9dcb5ed adds buffers and enum members but leaves the 27 inputs unchanged).
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, field

import numpy as np
import torch

# Buffers the model reads (everything else in `obs` is ignored).
USED_BUFFERS = (
    "global_i", "global_f", "hand", "hand_f", "hand_dmg", "deck", "draw_counts", "discard_counts", "exhaust_counts",
    "relics", "potions", "powers_player", "orbs", "enemies", "enemy_intents", "enemy_powers", "allies", "map_nodes",
    "rewards", "card_reward", "card_reward_alts", "shop", "rest", "event_options", "prompt_options", "cand", "cand_n",
)
# Buffers whose leading row axis is truncated (training stores at most this many rows; eval passes all of them).
ROW_TRUNCATED = ("cand", "prompt_options")

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
    enum_names: dict[str, list[str]]          # RunActionKind / PromptItemKind names (pointer wiring)
    cols: dict[str, dict[str, int]]           # buffer -> field -> column
    shapes: dict[str, list[int]]              # buffer -> per-env shape
    card_vars: int
    aux_fields: list[str] = field(default_factory=list)

    @classmethod
    def from_schema(cls, schema) -> "ModelSpec":
        vocab = {k: len(v) for k, v in schema.vocab.lists.items()}
        enums = {k: len(v) for k, v in schema.vocab.enums.items()}
        names = {k: [schema.vocab.enums[k].name(i) for i in range(len(schema.vocab.enums[k]))]
                 for k in ("RunActionKind", "PromptItemKind", "RewardType")}
        cols, shapes = {}, {}
        for name in USED_BUFFERS + ("aux", "last_episode"):
            b = schema[name]
            shapes[name] = list(b.shape)
            if b.fields:
                cols[name] = {f: i for i, f in enumerate(b.fields)}
        return cls(encoder_version=schema.encoder_version, vocab=vocab, enums=enums, enum_names=names, cols=cols,
                   shapes=shapes, card_vars=len(schema.card_vars), aux_fields=list(schema["aux"].fields))

    def to_json(self) -> dict:
        return asdict(self)

    @classmethod
    def from_json(cls, d: dict) -> "ModelSpec":
        return cls(**d)


class SpecMismatch(ValueError):
    """A checkpoint's model inputs differ from the library's (`check_spec_compat`)."""


def _cols_delta(old: dict | None, new: dict | None) -> str:
    if old is None or new is None:
        return f"{'absent' if old is None else len(old)} -> {'absent' if new is None else len(new)} columns"
    parts = [f"-{k}" for k in old if k not in new] + [f"+{k}" for k in new if k not in old]
    parts += [f"{k} {old[k]}->{new[k]}" for k in old if k in new and old[k] != new[k]]
    return ", ".join(parts[:8]) + (f" (+{len(parts) - 8} more)" if len(parts) > 8 else "")


def spec_diff(old: "ModelSpec | dict", new: "ModelSpec | dict") -> tuple[list[str], list[str]]:
    """(model-input differences, other differences) between a checkpoint's spec `old` and the library's `new`.

    Model inputs (what the weights, the column lookups and the pointer wiring depend on; compared exactly): shape and
    column map of every buffer in USED_BUFFERS, the size of every vocabulary list the model embeds (MODEL_VOCABS) and
    of every enum it embeds (ENUMS), the member names recorded in `enum_names` (RunActionKind / PromptItemKind pointer
    wiring, RewardType), `card_vars`. Anything else may differ and is only reported: `encoder_version` (it changes with
    every schema edit; appended buffers are not part of a spec at all), other enums (EndKind, ItemKind, ...) and
    vocabulary lists, and the aux / last_episode blocks (the trainer reads those by field name from the live schema,
    never through the checkpoint)."""
    o = old.to_json() if isinstance(old, ModelSpec) else dict(old)
    n = new.to_json() if isinstance(new, ModelSpec) else dict(new)
    block: list[str] = []
    other: list[str] = []
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
    for k in sorted(set(oe) | set(ne)):
        if oe.get(k) == ne.get(k):
            continue
        txt = f"enum {k}: {oe.get(k, 'absent')} -> {ne.get(k, 'absent')}"
        if k in ENUMS:
            block.append(txt)
        else:
            other.append(txt + " (not read by the model)")
    on, nn_ = o.get("enum_names", {}), n.get("enum_names", {})
    for k in sorted(set(on) | set(nn_)):
        if k in on and on.get(k) != nn_.get(k):
            a, b = on.get(k) or [], nn_.get(k) or []
            moved = [x for x in a if x in b and a.index(x) != b.index(x)]
            block.append(f"enum {k} members: -{[x for x in a if x not in b]} +{[x for x in b if x not in a]}"
                         + (f" moved {moved[:6]}" if moved else ""))
    if o.get("card_vars") != n.get("card_vars"):
        block.append(f"card_vars {o.get('card_vars')} -> {n.get('card_vars')}")
    return block, other


def check_spec_compat(old: "ModelSpec | dict", new: "ModelSpec | dict", *, what: str = "resume") -> dict:
    """Accept a checkpoint spec `old` for the library spec `new` when the model's inputs are identical
    (`spec_diff`), else raise `SpecMismatch` listing every difference. Returns the record kept in config.json:
    ``{"checkpoint_encoder_version", "library_encoder_version", "model_inputs": "identical", "other_differences"}``."""
    block, other = spec_diff(old, new)
    o = old.to_json() if isinstance(old, ModelSpec) else old
    n = new.to_json() if isinstance(new, ModelSpec) else new
    ov, nv = o.get("encoder_version"), n.get("encoder_version")
    if block:
        hint = ("resume with the library the checkpoint was trained on (its bundle), or start a fresh run (a new --out)"
                if what == "resume" else "evaluate with the library the checkpoint was trained on (its bundle)")
        raise SpecMismatch(
            f"cannot {what}: the checkpoint's model inputs differ from this library's (checkpoint encoder_version "
            f"{ov}, library {nv}):\n  - " + "\n  - ".join(block)
            + "\nOnly an encoder_version change and changes outside the model's inputs (appended buffers, enums the "
            f"model does not embed, aux / last_episode columns) are accepted; {hint}.")
    return {"checkpoint_encoder_version": ov, "library_encoder_version": nv, "model_inputs": "identical",
            "other_differences": other}


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


def obs_to_device(obs: dict[str, np.ndarray], device: torch.device | str = "cpu", *,
                  max_rows: int | None = None) -> dict[str, torch.Tensor]:
    """The model's input tensors in their source dtypes (int16 pile counts stay int16), copied to ``device``.

    `cand` / `prompt_options` are cut to the rows the batch needs (`_row_need`). The copies are synchronous, so the
    env may overwrite its buffers right after this returns (on a cpu device the tensors may alias them until then)."""
    c, p = _row_need(obs, max_rows)
    out = {}
    for name in USED_BUFFERS:
        a = obs[name]
        if name == "cand":
            a = a[:, :c]
        elif name == "prompt_options":
            a = a[:, :p]
        out[name] = torch.from_numpy(np.ascontiguousarray(a)).to(device)
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
    to the host; `batch(index)` gathers a minibatch on the device and cuts `cand` / `prompt_options` to its needs."""

    def __init__(self, spec: ModelSpec, steps: int, num_envs: int, max_rows: int, dtypes: dict[str, np.dtype],
                 device: torch.device | str = "cpu"):
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

    def put(self, t: int, x: dict[str, torch.Tensor]) -> None:
        """Store step t from `obs_to_device` tensors (row-cut buffers are zero-padded to `max_rows`)."""
        for name in USED_BUFFERS:
            dst, src = self.obs[name][t], x[name]
            if name in ROW_TRUNCATED:
                w = min(src.shape[1], dst.shape[1])
                dst[:, :w].copy_(src[:, :w])
                dst[:, w:].zero_()
            else:
                dst.copy_(src)

    def put_obs(self, t: int, obs: dict[str, np.ndarray]) -> None:
        self.put(t, obs_to_device(obs, self.device, max_rows=self.max_rows))

    def batch(self, index: torch.Tensor | np.ndarray) -> dict[str, torch.Tensor]:
        """Model inputs of the flat (t * N + i) samples `index`, gathered on the device."""
        idx = torch.as_tensor(index, device=self.device, dtype=torch.long)
        x = {k: v.flatten(0, 1).index_select(0, idx) for k, v in self.obs.items()}
        c = int(x["cand_n"].max().clamp(1, self.obs["cand"].shape[2]).item()) if len(idx) else 1
        p = int(x["global_i"][:, self._prompt_col].max().clamp(1, self.obs["prompt_options"].shape[2]).item()) \
            if len(idx) else 1
        x["cand"] = x["cand"][:, :c]
        x["prompt_options"] = x["prompt_options"][:, :p]
        return x

    @property
    def nbytes(self) -> int:
        return sum(v.numel() * v.element_size() for v in self.obs.values())
