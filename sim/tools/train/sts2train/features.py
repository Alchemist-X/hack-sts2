"""Observation plumbing between sts2vec numpy buffers and the torch model.

`ModelSpec` captures everything the model needs from the published schema (vocab sizes, enum sizes, column indices,
capacities) so a checkpoint can rebuild its model without a live library, and records the schema's encoder_version.
Tables are always indexed by the schema's field names (never hard-coded offsets).
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
    """Preallocated [T, N, ...] numpy copies of the used buffers (cand / prompt_options cut to `max_rows` rows)."""

    def __init__(self, spec: ModelSpec, steps: int, num_envs: int, max_rows: int, dtypes: dict[str, np.dtype]):
        self.T, self.N, self.max_rows = steps, num_envs, max_rows
        self.obs = {}
        for name in USED_BUFFERS:
            shape = list(spec.shapes[name])
            if name in ROW_TRUNCATED:
                shape[0] = min(shape[0], max_rows)
            self.obs[name] = np.zeros((steps, num_envs, *shape), dtype=dtypes[name])
        self.actions = np.zeros((steps, num_envs), dtype=np.int64)
        self.logp = np.zeros((steps, num_envs), dtype=np.float32)
        self.values = np.zeros((steps, num_envs), dtype=np.float32)
        self.rewards = np.zeros((steps, num_envs), dtype=np.float32)
        self.dones = np.zeros((steps, num_envs), dtype=np.float32)
        self.valid = np.zeros((steps, num_envs), dtype=bool)

    def put_obs(self, t: int, obs: dict[str, np.ndarray]) -> None:
        for name in USED_BUFFERS:
            dst = self.obs[name][t]
            src = obs[name]
            if name in ROW_TRUNCATED:
                src = src[:, : dst.shape[1]]
            np.copyto(dst, src)

    def flat(self) -> dict[str, np.ndarray]:
        return {k: v.reshape(self.T * self.N, *v.shape[2:]) for k, v in self.obs.items()}

    @property
    def nbytes(self) -> int:
        return sum(v.nbytes for v in self.obs.values())
