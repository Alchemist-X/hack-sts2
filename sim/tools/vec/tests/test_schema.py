import numpy as np

from sts2vec import STATUS, PoolSpec, Schema, VecEnv, load
from sts2vec.schema import xxh64

BUFFER_ORDER = [
    "global_i", "global_f", "hand", "hand_f", "hand_dmg", "deck", "draw_counts", "discard_counts", "exhaust_counts",
    "relics", "potions", "powers_player", "orbs", "enemies", "enemy_intents", "enemy_powers", "allies", "ally_powers",
    "map_nodes", "rewards", "card_reward", "card_reward_alts", "shop", "rest", "event_options", "prompt_options",
    "cand", "cand_n", "reward", "terminated", "truncated", "status", "episode", "aux", "last_episode",
]


def schema() -> Schema:
    return Schema.from_json(load().schema_json())


def test_xxh64_known_vectors():
    assert xxh64(b"") == 0xEF46DB3751D8E999
    assert xxh64(b"a") == 0xD24EC4F1A98C6E5B
    assert xxh64(b"abc") == 0x44BC2CF5AD770999
    assert xxh64(bytes(range(100))) == xxh64(bytes(range(100)))


def test_encoder_version_matches_json():
    s = schema()
    assert s.verify_version()
    assert load().version() == "sts2vec/" + s.encoder_version


def test_buffer_order_and_fields():
    s = schema()
    assert [b.name for b in s.buffers] == BUFFER_ORDER
    for b in s.buffers:
        assert b.bytes_per_env > 0, b.name
        if b.fields is not None:
            assert len(b.fields) == b.shape[-1], b.name
    v_cards = len(s.vocab.cards)
    for name in ("draw_counts", "discard_counts", "exhaust_counts"):
        assert s[name].shape == (2 * v_cards,)
        assert s[name].dtype == np.int16
    assert s["hand"].shape[1] == s["card_reward"].shape[1] == len(s["hand"].fields)
    assert s.vocab.cards[0] == "" and s.vocab.cards[1] == "?"
    assert s.vocab.cards.index_of("NOT_A_CARD") == 1
    assert len(s.vocab.enums["RunActionKind"]) == 21
    assert s.caps["CANDIDATES"] == 192 and s.caps["PROMPT_OPTIONS"] == 128


def test_buffer_sizes_match_schema():
    s = schema()
    n = 3
    bufs = s.allocate(n)
    for b in s.buffers:
        a = bufs[b.name]
        assert a.shape == (n, *b.shape), b.name
        assert a.dtype == b.dtype, b.name
        assert a.nbytes == n * b.bytes_per_env, b.name
        assert a.flags.c_contiguous
        assert bufs.table[b.index] == a.ctypes.data
    assert bufs.nbytes == n * s.bytes_per_env


def test_env_reset_step_against_stubs():
    with VecEnv(4, threads=2, pool=PoolSpec(characters=("IRONCLAD", "SILENT"), count=2)) as env:
        obs = env.reset()
        status = env.buffers["status"]
        assert np.all(status & STATUS["RESET"])
        assert list(env.buffers["episode"][:, 0]) == [1, 2, 3, 4]
        assert set(obs) == set(BUFFER_ORDER) - {"reward", "terminated", "truncated", "status", "episode", "last_episode"}
        cand_n = obs["cand_n"].copy()
        acts = np.full(4, -1, dtype=np.int32)
        acts[1] = cand_n[1]  # always out of range
        obs, reward, term, trunc, status, info = env.step(acts)
        assert status[0] & STATUS["NOOP"]
        assert status[1] & STATUS["ILLEGAL_ACTION"]
        assert reward.shape == (4,) and term.dtype == np.uint8
        assert set(info) == {"episode_id", "step_index", "start_entry", "last_episode"}
        h = env.state_hash()
        assert h.dtype == np.uint64 and np.all(h != 0)
        entry = env.snapshot(0)
        assert entry == len(env.pool) - 1
        env.restore(3, entry)
        assert env.state_hash([3])[0] == h[0]
