import contextlib
import fcntl
import io
import os
from pathlib import Path
import tempfile
import time
import unittest

import live_bridge as bridge
from push_live import SiteClient, read_service_token


def payload(version, status="running"):
    return {
        "schema_version": 1, "version": version, "observed_at": str(version),
        "runs": [{"id": key, "status": status, "observed_at": str(version), "updates": version,
                  "decisions": version * 131072, "checkpoint_evidence": "writer_metadata"}
                 for key in ("M116", "S115-S2")],
    }


class FakeSite:
    def __init__(self):
        self.current = payload(1)
        self.writes = []
        self.reads = 0
        self.lose_ack = False
        self.failure = False

    def get(self, timeout):
        self.reads += 1
        if self.failure:
            raise OSError("fixture offline")
        return self.current

    def post(self, value, timeout):
        self.writes.append(value["version"])
        self.current = value
        if self.lose_ack:
            self.lose_ack = False
            raise TimeoutError("fixture lost receipt")


class Clock:
    def __init__(self, now):
        self.now = now
        self.sleeps = []

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.now += seconds


class Tests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.logs = io.StringIO()
        self.enterContext(contextlib.redirect_stdout(self.logs))

    def run_loop(self, client, load, clock):
        bridge.loop(self.directory, client, load=load, clock=clock, sleep=clock.sleep)
        return bridge.read_state(self.directory)

    def test_already_published_same_version_never_posts_and_persists(self):
        site = FakeSite()
        clock = Clock(bridge.HARD_STOP - 200)
        state = self.run_loop(site, lambda: payload(1), clock)
        self.assertEqual(state["last_good"], site.current)
        self.assertEqual(site.reads, 1)
        self.assertEqual(site.writes, [])
        self.assertEqual(clock.sleeps, [90, 90, 20])

    def test_lost_post_ack_reconciles_with_get_without_resending(self):
        site = FakeSite()
        site.lose_ack = True
        state = self.run_loop(site, lambda: payload(2), Clock(bridge.HARD_STOP - 200))
        self.assertEqual(site.writes, [2])
        self.assertEqual(site.reads, 2)
        self.assertEqual(state["last_good"], payload(2))

    def test_network_backoff_keeps_observer_cadence_and_old_state(self):
        site = FakeSite()
        site.failure = True
        bridge.persist(self.directory, {"schema_version": 1, "last_good": payload(1), "pending": None, "terminal_seen_at": None})
        clock = Clock(bridge.HARD_STOP - 650)
        ticks = []
        def load():
            ticks.append(clock())
            return payload(2)
        state = self.run_loop(site, load, clock)
        self.assertEqual(state["last_good"], payload(1))
        self.assertEqual([x - ticks[0] for x in ticks], list(range(0, 650, 90)))
        self.assertEqual(site.reads, 4)
        self.assertEqual(site.writes, [])

    def test_only_both_terminal_starts_persisted_margin_and_hard_stop_wins(self):
        site = FakeSite()
        clock = Clock(bridge.HARD_STOP - 600)
        start = clock()
        state = self.run_loop(site, lambda: payload(2, "terminal"), clock)
        self.assertEqual(state["terminal_seen_at"], start)
        self.assertEqual(state["last_good"], payload(2, "terminal"))
        self.assertEqual(clock(), start + bridge.TERMINAL_MARGIN)
        self.run_loop(site, lambda: self.fail("restart extended deadline"), clock)
        self.assertEqual(site.writes, [2])
        self.assertEqual(bridge.deadline({"terminal_seen_at": bridge.HARD_STOP - 1}), bridge.HARD_STOP)

    def test_equal_global_version_retains_terminal_peer_pending(self):
        site = FakeSite()
        confirmed = payload(1)
        confirmed["runs"][0]["status"] = "terminal"
        site.current = confirmed
        bridge.persist(self.directory, {"schema_version": 1, "last_good": confirmed,
                                       "pending": None, "terminal_seen_at": None})
        terminal = payload(1, "terminal")
        state = self.run_loop(site, lambda: terminal, Clock(bridge.HARD_STOP - 600))
        self.assertEqual(state["last_good"], confirmed)
        self.assertEqual(state["pending"], terminal)
        self.assertEqual(site.writes, [])
        self.assertIn("pending_observation_unpublished", self.logs.getvalue())
        self.assertEqual(self.logs.getvalue().count("peer_observation_pending_new_global_version"), 1)

    def test_missing_observation_never_posts_or_creates_terminal(self):
        site = FakeSite()
        def missing():
            raise FileNotFoundError()
        state = self.run_loop(site, missing, Clock(bridge.HARD_STOP - 100))
        self.assertIsNone(state["terminal_seen_at"])
        self.assertIsNone(state["last_good"])
        self.assertEqual(site.reads, 0)
        self.assertEqual(site.writes, [])

    def test_competing_flock_cannot_read_or_send(self):
        lock = self.directory / "writer.lock"
        with lock.open("w") as handle:
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.run_loop(FakeSite(), lambda: self.fail("second writer entered"), Clock(bridge.HARD_STOP - 100))
        self.assertIn("writer_already_running", self.logs.getvalue())

    def test_inherited_fd_and_wall_timeout(self):
        read, write = os.pipe()
        try:
            os.write(write, b"fixture-token\n")
            os.close(write)
            self.assertEqual(read_service_token(read), "fixture-token")
        finally:
            os.close(read)
        client = SiteClient("fixture-token")
        class BlockedOpener:
            def open(self, *_args, **_kwargs):
                time.sleep(1)
        client._opener = BlockedOpener()
        started = time.monotonic()
        with self.assertRaises(TimeoutError):
            client.get(.02)
        self.assertLess(time.monotonic() - started, .5)
        self.assertNotIn("fixture-token", self.logs.getvalue())


if __name__ == "__main__":
    unittest.main()
