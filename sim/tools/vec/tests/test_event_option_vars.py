"""sim/Sts2Sim.Vec/EventOptionVars.g.cs (the placeholders of every event option's text, which the encoder's
event_option_values table resolves) is up to date with the localisation export in knowledge/source."""

from __future__ import annotations

import importlib.util
from pathlib import Path

GEN = Path(__file__).resolve().parents[1] / "tools" / "gen_event_option_vars.py"


def _gen():
    spec = importlib.util.spec_from_file_location("_gen_event_option_vars", GEN)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def test_generated_table_is_up_to_date(capsys):
    gen = _gen()
    assert gen.main(["--check"]) == 0, "run python3 sim/tools/vec/tools/gen_event_option_vars.py"
    assert "up to date" in capsys.readouterr().out


def test_table_content():
    gen = _gen()
    table, files = gen.collect()
    assert files and all(len(h) == 64 for _, h in files)
    assert table["ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE"] == ["MaxHp", "Damage"]
    assert table["RELIC_TRADER.pages.INITIAL.options.TOP"] == ["TopRelicOwned", "TopRelicNew"]
    assert all(names and len(set(names)) == len(names) for names in table.values())
    assert len(table) >= 120


def test_appended_buffers_through_the_env(full):
    """End to end through the C ABI: random play fills the appended buffers consistently with the frozen ones —
    event_option_values only on option rows (and with valid item ids), the act's Ancient on every live slot, pile order
    tables with min(pile size, cap) cards, hand_x only on hand rows, draw_marked_n within the draw pile."""
    import numpy as np

    from sts2vec import PoolSpec, VecEnv, random_actions

    with VecEnv(64, threads=4, seed_text="APPEND", pool=PoolSpec(count=64, seed_prefix="APPEND")) as env:
        S = env.schema
        G, X, E = S.cols("global_i"), S.cols("global_x"), S.cols("event_option_values")
        P = S.cols("discard_order")
        kinds = S.vocab.enums["ItemKind"]
        vocab_of = {"card": "cards", "relic": "relics", "potion": "potions", "enchantment": "enchantments",
                    "affliction": "afflictions"}
        cap = S.caps["PILE_ORDER"]
        rng = np.random.default_rng(3)
        obs = env.reset()
        seen = {"event_values": 0, "event_items": 0, "discard_rows": 0, "combat": 0}
        for _ in range(400):
            gi, gx = obs["global_i"], obs["global_x"]
            assert (gx[:, X.ancient_id] > 1).all(), "every run shows its act's Ancient"
            for i in range(env.num_envs):
                ev = obs["event_option_values"][i]
                n_opt = int(gi[i, G.event_option_n])
                assert not ev[n_opt:].any(), "values only on option rows"
                for r in range(min(n_opt, ev.shape[0])):
                    seen["event_values"] += int(ev[r, E.value_n] > 0)
                    for k in range(min(int(ev[r, E.item_n]), S.caps["EVENT_ITEMS"])):
                        kind = kinds.name(ev[r, E[f"item_kind_{k}"]])
                        iid = int(ev[r, E[f"item_id_{k}"]])
                        assert kind in vocab_of and 2 <= iid < len(S.vocab.lists[vocab_of[kind]]), (kind, iid)
                        seen["event_items"] += 1
                if gi[i, G.is_combat]:
                    seen["combat"] += 1
                    for name, n_col in (("discard_order", G.discard_n), ("exhaust_order", G.exhaust_n)):
                        rows = int((obs[name][i][:, P.card_id] > 1).sum())
                        assert rows == min(int(gi[i, n_col]), cap), (name, rows, int(gi[i, n_col]))
                    seen["discard_rows"] += int(gi[i, G.discard_n] > 0)
                    assert 0 <= gx[i, X.draw_marked_n] <= gi[i, G.draw_n]
                    hand_n = int(gi[i, G.hand_n])
                    assert not obs["hand_x"][i][hand_n:].any(), "hand_x only on hand rows"
                else:
                    for name in ("draw_marked", "discard_order", "exhaust_order", "hand_x"):
                        assert not obs[name][i].any(), f"{name} is combat only"
                    assert gx[i, X.draw_marked_n] == 0
            obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
        assert seen["combat"] > 1000 and seen["discard_rows"] > 500, seen
        assert seen["event_values"] > 50 and seen["event_items"] > 20, seen
