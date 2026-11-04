"""Auxiliary heads (``--aux-heads``): win probability and the hp left at the end of the current combat.

Two small MLPs on the policy's state embedding (``model.Policy`` with ``aux_heads=True``; the value head is unchanged):

- ``win_prob`` = sigmoid(win logit): the probability that the run is won from this state under the policy. Loss:
  binary cross-entropy against the episode's outcome (1 = Victory, 0 = Defeat).
- ``combat_hp_pred`` = sigmoid(combat logit): the fraction hp / max_hp the player has when the current combat ends
  (0 when the player dies in it). Defined for states in combat (``aux.is_combat``); loss: squared error.

Both losses are masked means over the minibatch, added to the PPO loss with weight ``aux_coef``, and train the shared
encoder too (that is their purpose: make the state embedding carry how the run / the fight is going).

Targets (``AuxTargets``, one rollout window of T steps x N slots, auto-reset env):

- A step of an episode that ends inside the window by a terminal (Victory / Defeat) gets that episode's outcome; an
  in-combat step whose combat ends inside the window gets the hp fraction at that end: of the first observation
  after the combat (``aux.hp / aux.max_hp``; a combat ends when ``is_combat`` drops, ``combats_won`` grows or the
  episode ends) or, when the episode ends there, ``last_episode.final_hp / final_max_hp``.
- Truncated episodes (step cap, floor step cap, SIM_ERROR / CONTENT_GAP: ``truncated`` without ``terminated``) have
  no outcome: their steps (and the combat steps whose combat the truncation cut) are masked.
- Steps of episodes / combats still running at the end of the window: with ``aux_bootstrap`` (default) their target
  is the head's own prediction (detached, pre-update weights) at the observation after the window, the n-step
  bootstrap the value head also uses (gamma 1, no discount); without it they are masked, so only outcomes observed
  inside the window train the heads (with 32-step windows that is mostly the last steps before a death).
"""

from __future__ import annotations

import numpy as np
import torch
import torch.nn.functional as F

# What a checkpoint with aux heads records under "heads" (and `sts2train eval` writes per step into the game logs).
HEAD_OUTPUTS = {
    "win_prob": "sigmoid(win_head(state)): probability that the run is won from this state under the policy "
                "(binary cross-entropy to the episode outcome; truncated episodes masked)",
    "combat_hp_pred": "sigmoid(combat_head(state)): predicted hp / max_hp when the current combat ends (0 = dies in "
                      "it); states in combat only (squared error; combats cut by a truncation masked)",
}
# Per-minibatch statistics appended after STAT_NAMES (ppo.stat_names) when the heads are on.
AUX_STAT_NAMES = ("aux_win_loss", "aux_combat_loss", "aux_win_frac", "aux_combat_frac")
AUX_FIELDS = ("hp", "max_hp", "is_combat", "combats_won")
LAST_EPISODE_FIELDS = ("win", "final_hp", "final_max_hp")


def aux_losses(heads: dict[str, torch.Tensor], win_y: torch.Tensor, win_m: torch.Tensor, combat_y: torch.Tensor,
               combat_m: torch.Tensor) -> tuple[torch.Tensor, ...]:
    """(win BCE, combat squared error, share of samples with a win target, share with a combat target): masked
    means over the minibatch (0 when no sample has a target; no host sync)."""
    wl = (F.binary_cross_entropy_with_logits(heads["win_logit"], win_y, reduction="none") * win_m).sum() \
        / win_m.sum().clamp_min(1.0)
    cl = ((torch.sigmoid(heads["combat_logit"]) - combat_y) ** 2 * combat_m).sum() / combat_m.sum().clamp_min(1.0)
    return wl, cl, win_m.mean(), combat_m.mean()


class AuxTargets:
    """Targets and masks of the auxiliary heads for one rollout window ([T, N], see the module docstring).

    Per step t: ``before(t, aux)`` with the aux block of the observation the policy acts on (s_t), then, after
    ``env.step``, ``after(t, terminated, truncated, last_episode, aux)`` with the new observation's aux block (s_{t+1};
    the next episode's first observation where the step ended an episode). At the end of the window
    ``targets(win_boot, combat_boot)`` -> (win_y, win_m, combat_y, combat_m) float32 [T, N] and a stats dict."""

    def __init__(self, aux_fields, last_episode_fields, steps: int, num_envs: int, bootstrap: bool = True):
        a = {f: i for i, f in enumerate(aux_fields)}
        l_ = {f: i for i, f in enumerate(last_episode_fields)}
        missing = [f for f in AUX_FIELDS if f not in a] + [f for f in LAST_EPISODE_FIELDS if f not in l_]
        if missing:
            raise ValueError(f"the library's aux / last_episode blocks lack {missing} (needed by --aux-heads)")
        self._a, self._l = a, l_
        self.bootstrap = bool(bootstrap)
        T, N = steps, num_envs
        self.in_combat = np.zeros((T, N), dtype=bool)       # s_t is in combat
        self._won = np.zeros((T, N), dtype=np.int64)        # combats_won at s_t
        self.ends = np.zeros((T, N), dtype=bool)            # step t ends the combat s_t is in
        self.end_hp = np.zeros((T, N), dtype=np.float32)    # hp fraction after step t (final_hp where it ended the run)
        self.term = np.zeros((T, N), dtype=bool)
        self.trunc = np.zeros((T, N), dtype=bool)           # truncated without a terminal
        self.win = np.zeros((T, N), dtype=bool)

    def before(self, t: int, aux: np.ndarray) -> None:
        a = self._a
        self.in_combat[t] = aux[:, a["is_combat"]] != 0
        self._won[t] = aux[:, a["combats_won"]]

    def after(self, t: int, terminated: np.ndarray, truncated: np.ndarray, last_episode: np.ndarray,
              aux: np.ndarray) -> None:
        a, l_ = self._a, self._l
        term = np.asarray(terminated).astype(bool)
        trunc = np.asarray(truncated).astype(bool) & ~term
        done = term | trunc
        self.term[t], self.trunc[t] = term, trunc
        self.win[t] = term & (last_episode[:, l_["win"]] != 0)
        hp = np.where(done, last_episode[:, l_["final_hp"]], aux[:, a["hp"]]).astype(np.float64)
        mx = np.where(done, last_episode[:, l_["final_max_hp"]], aux[:, a["max_hp"]]).astype(np.float64)
        self.end_hp[t] = np.clip(hp / np.maximum(mx, 1.0), 0.0, 1.0)
        left = (aux[:, a["is_combat"]] == 0) | (aux[:, a["combats_won"]] > self._won[t])
        self.ends[t] = self.in_combat[t] & (done | left)

    def targets(self, win_boot: np.ndarray | None = None, combat_boot: np.ndarray | None = None):
        """-> (win_y, win_m, combat_y, combat_m) float32 [T, N] (targets 0 where masked) and statistics. `win_boot` /
        `combat_boot` [N]: the heads' predictions (probabilities) at the observation after the window (needed with
        bootstrap)."""
        T, N = self.in_combat.shape
        boot = self.bootstrap
        if boot and (win_boot is None or combat_boot is None):
            raise ValueError("bootstrap targets need the heads' predictions after the window")
        wy, wm = np.zeros((T, N), np.float32), np.zeros((T, N), bool)
        cy, cm = np.zeros((T, N), np.float32), np.zeros((T, N), bool)
        w_exact, c_exact = np.zeros((T, N), bool), np.zeros((T, N), bool)
        w_y = np.asarray(win_boot, np.float32) if boot else np.zeros(N, np.float32)
        c_y = np.asarray(combat_boot, np.float32) if boot else np.zeros(N, np.float32)
        w_m, c_m = np.full(N, boot), np.full(N, boot)
        w_x, c_x = np.zeros(N, bool), np.zeros(N, bool)
        for t in reversed(range(T)):
            done = self.term[t] | self.trunc[t]
            w_y = np.where(done, self.win[t].astype(np.float32), w_y)
            w_m = np.where(done, self.term[t], w_m)
            w_x = done | w_x
            wy[t], wm[t], w_exact[t] = w_y, w_m, w_x & w_m
            e = self.ends[t]
            c_y = np.where(e, self.end_hp[t], c_y)
            c_m = np.where(e, ~self.trunc[t], c_m)
            c_x = e | c_x
            cm[t] = c_m & self.in_combat[t]
            cy[t], c_exact[t] = c_y, c_x & cm[t]
        wy[~wm] = 0.0
        cy[~cm] = 0.0
        stats = {"aux_win_targets": int(wm.sum()), "aux_win_exact": int(w_exact.sum()),
                 "aux_win_target_mean": float(wy[wm].mean()) if wm.any() else None,
                 "aux_combat_targets": int(cm.sum()), "aux_combat_exact": int(c_exact.sum()),
                 "aux_combat_target_mean": float(cy[cm].mean()) if cm.any() else None}
        return wy, wm.astype(np.float32), cy, cm.astype(np.float32), stats
