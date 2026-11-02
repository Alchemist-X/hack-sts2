"""Template: write your own sts2vec policy. Copy this file, edit `act`, then run

    uv run --project sim/tools/vec sts2vec eval --policy path/to/my_policy.py:act --seedset std-a0-v1

The evaluator calls `act(obs, ctx)` once per batched step with every game of the seed set in one batch.
Return one candidate index per slot; read what each candidate does from obs["cand"] (sim/ENV_SPEC.md, "Action").
"""

import numpy as np


def act(obs: dict, ctx) -> np.ndarray:
    C = ctx.cols("cand")                     # column indices by name: C.kind, C.hand_slot, C.target_slot, ...
    G = ctx.cols("global_i")
    END_TURN, PLAY_CARD = ctx.kind("EndTurn"), ctx.kind("PlayCard")
    actions = np.zeros(ctx.num_envs, dtype=np.int32)
    for i in np.flatnonzero(ctx.live):       # slots that need an action now
        n = int(obs["cand_n"][i])            # legal candidates are rows 0..n-1 of obs["cand"][i]
        kinds = obs["cand"][i, :n, C.kind]
        plays = np.flatnonzero(kinds == PLAY_CARD)
        if len(plays) and obs["global_i"][i, G.energy] > 0:
            actions[i] = plays[0]            # play the first playable card
        elif (kinds == END_TURN).any():
            actions[i] = int(np.flatnonzero(kinds == END_TURN)[0])
        else:
            actions[i] = int(ctx.rngs[i].integers(0, n))   # anything else: a seeded random legal choice
    return actions
