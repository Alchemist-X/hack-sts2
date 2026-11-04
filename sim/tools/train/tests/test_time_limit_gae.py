import numpy as np

from sts2train.gae import time_limit_advantages


def test_external_caps_bootstrap_final_state_and_cut_trace():
    values = np.array([[1, 1, 1, 1, 1], [100, 100, 100, 100, 2]], dtype=np.float32)
    rewards = np.array([[3, 3, 3, 3, 3], [1, 1, 1, 1, 4]], dtype=np.float32)
    done = np.array([[1, 1, 1, 1, 0], [0, 0, 0, 0, 1]], dtype=np.float32)
    valid = np.ones_like(done, dtype=bool)
    valid[0, 3] = False
    final = np.full_like(values, np.nan)
    final[0, 0] = 5
    final[1, 4] = 6
    result = time_limit_advantages(rewards, values, np.array([200] * 5, dtype=np.float32),
                                   done, valid, final, 0.5, 0.8)
    # Cap, true terminal, stall, invalid, ongoing. Last row also covers the rollout boundary.
    np.testing.assert_allclose(result, [[4.5, 2, 2, 0, 5], [1, 1, 1, 1, 5]])


def test_invalid_transition_does_not_backpropagate_to_prior_advantage():
    rewards = np.array([[2], [100]], dtype=np.float32)
    values = np.array([[1], [4]], dtype=np.float32)
    done = np.array([[False], [True]])
    valid = np.array([[True], [False]])
    advantages = time_limit_advantages(rewards, values, np.array([10], dtype=np.float32),
                                        done, valid, np.full_like(values, np.nan), 0.5, 0.8)
    np.testing.assert_allclose(advantages, [[3], [0]])


def test_stream_copies_only_valid_caps_before_shared_buffer_is_reset():
    from sts2train.seedstream import StreamEnv
    from sts2vec import STATUS

    class SharedBufferEnv:
        def __init__(self):
            self.obs = {"marker": np.arange(1, 7)}
            self.signals = (
                np.ones(6),
                np.array([0, 1, 0, 0, 0, 0], dtype=np.uint8),
                np.array([1, 0, 1, 1, 1, 0], dtype=np.uint8),
                np.array([0, 0, STATUS["FLOOR_STALL"], STATUS["SIM_ERROR"], STATUS["ILLEGAL_ACTION"], 0]),
            )

        def step(self, actions):
            return self.obs, *self.signals, {}

        def info(self, copy):
            return {}

        def slot_errors(self):
            return {3: "sim error"}

    env = SharedBufferEnv()
    stream = StreamEnv.__new__(StreamEnv)
    stream.env = env
    stream.slot_n = stream.slot_entry = np.arange(6)
    stream.slot_text = ["seed"] * 6
    stream.t_restart = 0
    stream.totals = {"t_restart": 0}
    stream._join = lambda: None

    def reset(slots):
        env.obs["marker"][slots] = 900
        for signals in env.signals:
            signals[slots] = 0

    stream._start = reset
    obs, _, term, trunc, _, info = stream.step(np.zeros(6), capture_final=True)
    slots, final = info["final_observation"]
    np.testing.assert_array_equal(slots, [0])
    np.testing.assert_array_equal(final["marker"], [1])
    np.testing.assert_array_equal(obs["marker"], [900, 900, 900, 900, 900, 6])
    np.testing.assert_array_equal(term, [0, 1, 0, 0, 0, 0])
    np.testing.assert_array_equal(trunc, [1, 0, 1, 1, 1, 0])
