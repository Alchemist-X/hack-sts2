"""map_routes (appended 2026-11-04): the routing features recomputed in plain Python from the published map_nodes
columns (point_type, child_mask) over random rollouts, and compared with the library's table (a drift test between
the encoder and the graph a policy sees). Mirrors VecEncoderTests.Test_VecEncoder_MapRoutesMatchTheDrawnGraph."""

from __future__ import annotations

import numpy as np

from sts2vec import PoolSpec, VecEnv, random_actions


def reference_routes(nodes: np.ndarray, schema) -> np.ndarray:
    """map_routes of one env from its map_nodes [rows, cols, fields] (semantics: sim/ENV_SPEC.md, buffer map_routes)."""
    nc = schema.cols("map_nodes")
    mt = schema.vocab.enums["MapPointType"]
    boss_t, elite_t, rest_t, shop_t = (mt.index_of(n) for n in ("Boss", "Elite", "RestSite", "Shop"))
    t = nodes[..., nc.point_type]
    mask = nodes[..., nc.child_mask]
    rows, cols = t.shape
    bosses = [(r, c) for r in range(rows) for c in range(cols) if t[r, c] == boss_t]
    boss = min(bosses)                                  # the act's boss: the first Boss row (a second boss follows it)
    out = np.zeros((rows, cols, 5), dtype=np.int32)
    reach: dict[tuple[int, int], set] = {}
    for r in range(rows - 1, -1, -1):
        for c in range(cols):
            if t[r, c] == 0:
                continue
            seen = {(r, c)}
            if (r, c) == boss:
                d, lo, hi = 0, 0, 0
            else:
                d, lo, hi = -1, 1 << 30, -1
                for cc in range(cols):
                    if r + 1 < rows and (int(mask[r, c]) >> cc) & 1 and t[r + 1, cc]:
                        seen |= reach[(r + 1, cc)]
                        cmin, cmax, _, _, cd = out[r + 1, cc]
                        if cd >= 0:
                            d = cd + 1 if d < 0 else min(d, cd + 1)
                            lo, hi = min(lo, cmin), max(hi, cmax)
                if d >= 0 and t[r, c] == elite_t:
                    lo, hi = lo + 1, hi + 1
            if d < 0:
                lo = hi = -1
            reach[(r, c)] = seen
            out[r, c] = (lo, hi, sum(t[x] == rest_t for x in seen), sum(t[x] == shop_t for x in seen), d)
    return out


def test_map_routes_match_the_drawn_graph(full):
    spec = PoolSpec(characters=("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER"), ascension=10, count=20,
                    seed_prefix="ROUTES")
    nc = None
    graphs: dict[bytes, np.ndarray] = {}
    checked = 0
    with VecEnv(20, threads=4, pool=spec, seed_text="ROUTES") as env:
        nc = env.schema.cols("map_nodes")
        obs = env.reset()
        rng = np.random.default_rng(5)
        for _ in range(600):
            for i in range(env.num_envs):
                nodes = obs["map_nodes"][i]
                if not nodes[..., nc.point_type].any():
                    continue
                key = nodes[..., [nc.point_type, nc.child_mask]].tobytes()
                if key not in graphs:
                    graphs[key] = reference_routes(nodes, env.schema)
                    checked += 1
                np.testing.assert_array_equal(obs["map_routes"][i], graphs[key])
            obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
    assert checked >= 20, checked
