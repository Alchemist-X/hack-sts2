import ctypes

import numpy as np
import pytest

from sts2vec import STATUS, Schema, Status, status_counts, status_names
from sts2vec.schema import SIGNAL_BUFFERS, slot_seed, xxh64

BUFFER_ORDER = [
    "global_i", "global_f", "hand", "hand_f", "hand_dmg", "deck", "draw_counts", "discard_counts", "exhaust_counts",
    "relics", "potions", "powers_player", "orbs", "enemies", "enemy_intents", "enemy_powers", "allies", "ally_powers",
    "map_nodes", "rewards", "card_reward", "card_reward_alts", "shop", "rest", "event_options", "prompt_options",
    "cand", "cand_n", "reward", "terminated", "truncated", "status", "episode", "aux", "last_episode",
    # appended (player-view schema append, docs/parity-audit-2026-11-02.md §5.2)
    "global_x", "hand_x", "draw_marked", "discard_order", "exhaust_order", "event_option_values",
    # appended with the rewards-screen macro candidates (2026-11-04)
    "reward_choices",
    # appended with the map-routing features (2026-11-04)
    "map_routes",
]

DTYPES = {  # §D3.1
    "draw_counts": np.int16, "discard_counts": np.int16, "exhaust_counts": np.int16,
    "global_f": np.float32, "hand_f": np.float32, "hand_dmg": np.float32, "reward": np.float32, "aux": np.float32,
    "terminated": np.uint8, "truncated": np.uint8, "episode": np.int64,
}


def test_xxh64_known_vectors():
    assert xxh64(b"") == 0xEF46DB3751D8E999
    assert xxh64(b"a") == 0xD24EC4F1A98C6E5B
    assert xxh64(b"abc") == 0x44BC2CF5AD770999
    # >= 32 bytes exercises the 4-lane path
    assert xxh64(b"Nobody inspects the spammish repetition") == 0xFBCEA83C8A378BF1


def test_encoder_version_matches_json(lib, schema):
    assert schema.verify_version()
    assert lib.version() == "sts2vec/" + schema.encoder_version
    tampered = Schema.from_json(schema.text.replace('"cards"', '"cards" ', 1))
    assert not tampered.verify_version()


def test_buffer_order_dtypes_and_fields(schema):
    assert schema.names == BUFFER_ORDER
    for b in schema.buffers:
        assert b.index == BUFFER_ORDER.index(b.name)
        assert b.dtype == np.dtype(DTYPES.get(b.name, np.int32)), b.name
        assert b.bytes_per_env > 0, b.name
        if b.fields is not None:
            assert len(b.fields) == b.shape[-1], b.name
            assert len(set(b.fields)) == len(b.fields), b.name
    for name in ("cand_n", "reward", "terminated", "truncated", "status"):
        assert schema[name].shape == () and schema[name].fields is None
    v_cards = len(schema.vocab.cards)
    for name in ("draw_counts", "discard_counts", "exhaust_counts"):
        assert schema[name].shape == (2 * v_cards,)
    assert schema["hand"].shape[1] == schema["card_reward"].shape[1] == len(schema["hand"].fields)
    assert schema["cand"].shape == (schema.caps["CANDIDATES"], 9)
    assert schema["prompt_options"].shape[0] == schema.caps["PROMPT_OPTIONS"] == 128
    assert schema["episode"].fields == ("episode_id", "step_index", "start_entry")
    # 21 RunSim action kinds, then the Vec-only rewards-screen macro kinds
    assert len(schema.vocab.enums["RunActionKind"]) == 23
    assert list(schema.vocab.enums["RunActionKind"])[21:] == ["TakeRewardCard", "TakeRewardAlternative"]
    assert schema["reward_choices"].shape == (schema.caps["REWARD_CHOICES"], 3 + len(schema["hand"].fields))
    assert schema["reward_choices"].fields == ("reward_index", "option", "alt_id") + tuple(schema["hand"].fields)
    assert schema["map_routes"].shape == schema["map_nodes"].shape[:2] + (5,)
    assert schema["map_routes"].fields == ("elites_min", "elites_max", "rests", "shops", "boss_dist")
    # the appended buffers (shapes from the caps; every pile table shares one column list)
    assert schema["global_x"].fields == ("ancient_id", "draw_marked_n")
    assert schema["hand_x"].shape == (schema.caps["HAND"], 2)
    assert schema["draw_marked"].shape == (schema.caps["DRAW_MARKED"], 6)
    assert schema["discard_order"].shape == schema["exhaust_order"].shape == (schema.caps["PILE_ORDER"], 6)
    assert schema["draw_marked"].fields == schema["discard_order"].fields == schema["exhaust_order"].fields
    assert schema["event_option_values"].shape[0] == schema["event_options"].shape[0]
    assert schema.vocab.enums["ItemKind"] == ["", "?", "affliction", "card", "enchantment", "potion", "relic"]


def test_columns_helpers(schema):
    H = schema.cols("hand")
    assert H.card_id == 0 and H["upgrade"] == 1
    assert schema.col("global_i", "hp") == schema["global_i"].fields.index("hp")
    assert schema.col("cand", "kind") == 0
    with pytest.raises(KeyError, match="no field"):
        schema.col("hand", "nope")
    with pytest.raises(AttributeError):
        _ = H.nope
    with pytest.raises(KeyError, match="has no fields"):
        schema.cols("draw_counts")
    with pytest.raises(KeyError, match="no buffer"):
        _ = schema["nope"]
    assert Schema.count_index(5, 0) == 10 and Schema.count_index(5, 3) == 11


def test_vocab_reverse_maps(schema):
    for kind, lst in schema.vocab.lists.items():
        assert lst[0] == "" and lst[1] == "?", kind
        assert len(set(lst)) == len(lst), kind
        for i in range(2, len(lst)):
            assert lst.index_of(lst[i]) == i
        assert lst.index_of("") == 0 and lst.index_of(None) == 0 and lst.index_of("NOT_AN_ID_XYZ") == 1
        assert lst.name(len(lst) + 5) == f"<{len(lst) + 5}>"
    assert schema.vocab.cards.index_of("NOT_A_CARD") == 1
    assert len(schema.vocab.characters) == 7  # "", "?" + 5 characters
    k = schema.vocab.enum("RunActionKind")
    assert k.index_of("PlayCard") == 0 and k.index_of("EndTurn") == 1 and k.name(1) == "EndTurn"
    with pytest.raises(KeyError):
        k.index_of("NotAKind")
    assert schema.vocab.enums["EndKind"] == ["None", "Terminal", "Truncated", "ContentGap", "SimError", "FloorStall"]
    assert "Prompt" in schema.vocab.enums["RunPhase"]
    assert len(schema.card_vars) == 133 and schema.card_vars[:2] == ["", "?"]
    assert schema.playable_reason_bits["None"] == 0
    bits = schema.playable_reason_bits
    two = [n for n, b in bits.items() if b][:2]
    assert schema.playable_reason_names(bits[two[0]] | bits[two[1]]) == two
    assert schema.playable_reason_names(0) == ["None"]


def test_status_flags():
    assert STATUS == {s.name: int(s) for s in Status}
    assert STATUS["RESET"] == 64 and STATUS["POOL_EMPTY"] == 128 and STATUS["FLOOR_STALL"] == 256
    assert status_names(0) == ["OK"]
    assert status_names(Status.NOOP | Status.RESET) == ["NOOP", "RESET"]
    c = status_counts(np.array([0, 1, 65, 8], dtype=np.int32))
    assert c["OK"] == 1 and c["NOOP"] == 2 and c["RESET"] == 1 and c["SIM_ERROR"] == 1 and c["ILLEGAL_ACTION"] == 0


def test_observation_partition(schema):
    obs = schema.observation_names
    assert set(obs) | set(SIGNAL_BUFFERS) == set(BUFFER_ORDER)
    assert not set(obs) & set(SIGNAL_BUFFERS)
    assert "cand" in obs and "cand_n" in obs and "aux" in obs
    assert schema.observation_bytes_per_env + sum(schema[n].bytes_per_env for n in SIGNAL_BUFFERS) == schema.bytes_per_env


def test_buffer_sizes_and_pointer_order(schema):
    n = 3
    bufs = schema.allocate(n)
    assert bufs.check() == []
    for b in schema.buffers:
        a = bufs[b.name]
        assert a.shape == (n, *b.shape), b.name
        assert a.dtype == b.dtype, b.name
        assert a.nbytes == n * b.bytes_per_env, b.name
        assert a.flags.c_contiguous
        assert bufs.table[b.index] == a.ctypes.data
    assert bufs.pointers() == [bufs[name].ctypes.data for name in BUFFER_ORDER]
    # the void** table is read as consecutive pointers starting at bufs.ptr
    raw = (ctypes.c_void_p * len(BUFFER_ORDER)).from_address(bufs.ptr)
    assert [raw[i] for i in range(len(BUFFER_ORDER))] == bufs.pointers()
    assert bufs.nbytes == n * schema.bytes_per_env
    # changes only with a schema change (26,710 + 4 aux columns in E3 train-smoke + 4,216 for the six appended buffers
    # + 1,600 for reward_choices + 2,520 for map_routes)
    assert schema.bytes_per_env == 35_062
    with pytest.raises(ValueError):
        schema.allocate(0)


def test_buffers_check_detects_problems(schema):
    bufs = schema.allocate(2)
    bufs.arrays["hand"] = np.zeros((2, 1), dtype=np.int32)
    problems = bufs.check()
    assert any(p.startswith("hand: shape") for p in problems)
    assert any("pointer table" in p for p in problems)


def test_slot_seed_matches_definition():
    # XXH64(UTF-8(seed_text) || int32 LE slot)
    assert slot_seed("VEC", 0) == xxh64(b"VEC\x00\x00\x00\x00")
    assert slot_seed("VEC", 1) == xxh64(b"VEC\x01\x00\x00\x00")
    assert slot_seed("VEC", 0) != slot_seed("VEC", 1)
