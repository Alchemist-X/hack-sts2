"""Task089 isolated prototype: public actual-action next-frame supervision.

Not wired to train/config/checkpoints. Labels never enter Policy/runner inputs.
Terminal HP alone uses the same-call public last_episode under strict ownership checks.
"""
from dataclasses import dataclass
import math
import numpy as np
import torch
from torch import nn
from torch.nn import functional as F
from sts2vec.schema import STATUS

CHANNELS = ("player_hp", "player_block", "player_energy", "sum_enemy_hp")
SCALES = np.array([100., 100., 10., 100.], dtype=np.float64)
COEFFICIENT = 0.1
BAD = sum(STATUS[n] for n in ("NOOP", "ILLEGAL_ACTION", "CONTENT_GAP", "SIM_ERROR",
                              "CANDIDATE_OVERFLOW", "TABLE_OVERFLOW", "RESET", "POOL_EMPTY", "FLOOR_STALL"))


@dataclass(frozen=True)
class PublicFrame:
    global_i: np.ndarray
    enemies: np.ndarray
    aux: np.ndarray
    cand: np.ndarray
    cand_n: np.ndarray
    episode_id: np.ndarray
    step_index: np.ndarray
    status: np.ndarray

    @classmethod
    def capture(cls, obs, episode_id, step_index, status):
        # Copy before env.step mutates views. No RNG/state hash/hidden buffers.
        return cls(*(np.array(a, copy=True) for a in (
            obs["global_i"], obs["enemies"], obs["aux"], obs["cand"], obs["cand_n"], episode_id, step_index, status)))


def targets(spec, before, after, actions, terminated, truncated, status, valid, *, max_rows=192, last_episode=None, terminal_end_kind=None):
    """Own actual transition labels; mask independent of PPO's valid/reward arrays."""
    gc, ec, cc = (spec.cols[k] for k in ("global_i", "enemies", "cand"))
    required = {"global_i": ("phase", "is_combat", "act_index", "total_floor", "map_row", "map_col",
                             "act_floor", "turn", "hp", "max_hp", "block", "energy", "enemy_n", "prompt_option_n"),
                "enemies": ("present", "uid", "hp"), "cand": ("kind",), "aux": ("combats_won",)}
    for table, fields in required.items():
        if any(f not in spec.cols[table] for f in fields):
            raise ValueError("required public transition field unavailable")
    n = len(actions)
    counts = {"terminal_hp_labeled": 0, "terminal_ownership_rejected": 0, "combat_identity_rejected": 0, "reasons": {}}
    def reject(reason):
        counts["reasons"][reason] = counts["reasons"].get(reason, 0) + 1
    y, mask = np.zeros((n, 4), np.float32), np.zeros((n, 4), bool)
    gi, gj = before.global_i, after.global_i
    action_names = spec.enum_names["RunActionKind"]
    allowed = {action_names.index(k) for k in ("PlayCard", "UsePotion", "EndTurn")}
    combat = spec.enum_names["RunPhase"].index("Combat")
    for i, action in enumerate(actions):
        if not valid[i] or not 0 <= action < min(int(before.cand_n[i]), max_rows):
            reject("invalid_sample_or_index")
            continue
        if before.cand[i, action, cc["kind"]] not in allowed:
            reject("noncombat_action_kind")
            continue
        if int(before.status[i]) & (BAD & ~STATUS["RESET"]):
            reject("pre_error_or_native_overflow")
            continue
        if before.cand_n[i] > max_rows or gi[i, gc["prompt_option_n"]] > max_rows:
            reject("pre_trainer_width_overflow")
            continue
        if terminated[i] and not truncated[i]:
            lc = spec.cols["last_episode"]
            owned = (last_episode is not None and terminal_end_kind is not None
                     and not (int(status[i]) & (BAD & ~STATUS["RESET"]))
                     and gi[i, gc["phase"]] == combat and gi[i, gc["is_combat"]] == 1
                     and after.episode_id[i] != before.episode_id[i] and after.step_index[i] == 0)
            if owned:
                le = last_episode[i]
                owned = (le[lc["end_kind"]] == terminal_end_kind
                         and le[lc["steps"]] == before.step_index[i] + 1
                         and le[lc["floor"]] == gi[i, gc["total_floor"]]
                         and le[lc["act"]] == gi[i, gc["act_index"]])
                hp, mx = le[lc["final_hp"]], le[lc["final_max_hp"]]
                pre, pmx = gi[i, gc["hp"]], gi[i, gc["max_hp"]]
                owned = owned and all(np.isfinite(v) for v in (hp, mx, pre, pmx)) \
                    and mx > 0 and 0 <= hp <= mx and pmx > 0 and 0 <= pre <= pmx
            if owned:
                delta = float(hp) - float(pre)
                y[i, 0] = np.sign(delta) * np.log1p(abs(delta)) / np.log1p(SCALES[0])
                mask[i, 0] = True
                counts["terminal_hp_labeled"] += 1
            else:
                counts["terminal_ownership_rejected"] += 1
                reject("terminal_ownership")
            continue
        if terminated[i] or truncated[i] or (int(status[i]) & BAD):
            reject("done_or_post_error_reset_overflow")
            continue
        if after.cand_n[i] > max_rows or gj[i, gc["prompt_option_n"]] > max_rows:
            reject("post_trainer_width_overflow")
            continue
        if (before.episode_id[i] != after.episode_id[i]
                or after.step_index[i] != before.step_index[i] + 1):
            reject("episode_or_step_discontinuity")
            continue
        if any(gi[i, gc[f]] != gj[i, gc[f]] for f in ("act_index", "total_floor", "act_floor", "map_row", "map_col")):
            reject("room_discontinuity")
            continue
        if (before.aux[i, spec.cols["aux"]["combats_won"]] != after.aux[i, spec.cols["aux"]["combats_won"]]
                or gj[i, gc["turn"]] < gi[i, gc["turn"]]):
            counts["combat_identity_rejected"] += 1
            reject("combat_identity_proxy")
            continue
        if not (gi[i, gc["phase"]] == gj[i, gc["phase"]] == combat
                and gi[i, gc["is_combat"]] == gj[i, gc["is_combat"]] == 1):
            reject("combat_phase")
            continue
        raw = np.zeros(4, np.float64)
        for j, field in enumerate(("hp", "block", "energy")):
            pre, post = gi[i, gc[field]], gj[i, gc[field]]
            ok = np.isfinite(pre) and np.isfinite(post) and pre >= 0 and post >= 0
            if field == "hp":
                a, b = gi[i, gc["max_hp"]], gj[i, gc["max_hp"]]
                ok = ok and a > 0 and b > 0 and pre <= a and post <= b
            if ok:
                raw[j], mask[i, j] = float(post) - float(pre), True
        pre = before.enemies[i][before.enemies[i, :, ec["present"]] != 0]
        post = after.enemies[i][after.enemies[i, :, ec["present"]] != 0]
        u, v = pre[:, ec["uid"]], post[:, ec["uid"]]
        hp0, hp1 = pre[:, ec["hp"]], post[:, ec["hp"]]
        if (0 < len(u) <= before.enemies.shape[1] and len(u) == len(set(u.tolist()))
                and len(v) == len(set(v.tolist())) and set(u.tolist()) == set(v.tolist())
                and np.all(u >= 0) and np.all(v >= 0)
                and np.isfinite(hp0).all() and np.isfinite(hp1).all()
                and np.all(hp0 >= 0) and np.all(hp1 >= 0)
                and 0 <= gi[i, gc["enemy_n"]] <= before.enemies.shape[1]
                and 0 <= gj[i, gc["enemy_n"]] <= after.enemies.shape[1]):
            raw[3], mask[i, 3] = float(hp1.sum(dtype=np.float64) - hp0.sum(dtype=np.float64)), True
        if not mask[i, 3]:reject("enemy_membership_or_invalid")
        if not mask[i, :3].all():reject("player_component_invalid")
        y[i] = (np.sign(raw) * np.log1p(np.abs(raw)) / np.log1p(SCALES)).astype(np.float32)
    counts["valid_per_channel"] = mask.sum(0).tolist()
    return y, mask, counts


class TransitionHead(nn.Module):
    def __init__(self, state_width, candidate_width, *, seed=89):
        super().__init__()
        # CPU-only construction after original Policy init; restore CPU RNG.
        # Does not touch any CUDA generator or consume sampling/shuffle streams.
        with torch.random.fork_rng(devices=[]):
            torch.random.default_generator.manual_seed(seed)
            self.layers = nn.Sequential(nn.Linear(state_width + candidate_width, 64, device="cpu"), nn.ReLU(), nn.Linear(64, 4, device="cpu"))

    def forward(self, pre_state, pre_candidates, actions):
        idx = actions[:, None, None].expand(-1, 1, pre_candidates.shape[-1])
        selected = pre_candidates.gather(1, idx).squeeze(1)
        return self.layers(torch.cat((pre_state, selected), dim=-1)).float()


def masked_loss(prediction, target, mask):
    target = target.detach()
    mask = mask.detach().bool()
    # Where before reduction avoids NaN labels in masked entries; valid values must be finite.
    if not torch.isfinite(prediction).all() or not torch.isfinite(target[mask]).all():
        raise FloatingPointError("nonfinite transition auxiliary value")
    safe_target = torch.where(mask, target, torch.zeros_like(target))
    per = F.smooth_l1_loss(prediction, safe_target, reduction="none", beta=1.0)
    return ((per * mask).sum(0) / mask.sum(0).clamp_min(1)).sum() / 4


def loss_with_transition(model, head, xb, actions, oldlogp, advantages, returns, oldvalues,
                         target, mask, *, transition_coef=COEFFICIENT, **ppo_kwargs):
    """Prototype second prestate forward; no trainer integration/performance claim."""
    if not math.isfinite(transition_coef) or transition_coef < 0:
        raise ValueError("invalid transition coefficient")
    from .ppo import ppo_loss
    base, stats = ppo_loss(model, xb, actions, oldlogp, advantages, returns, oldvalues, **ppo_kwargs)
    if transition_coef == 0:
        return base, stats, base.detach().new_zeros(())
    *_, (state, candidates) = model(xb, return_transition_features=True)
    extra = masked_loss(head(state, candidates, actions), target, mask)
    return base + transition_coef * extra, stats, extra.detach()
