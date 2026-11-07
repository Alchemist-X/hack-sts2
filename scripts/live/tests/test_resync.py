"""Resync rules: which game save may re-seed the twin, at which save point, and what happens when it does not match.

The save files are minimal current_run.save documents (the fields sts2rec/run_save.py parses); the twin is a fake that
records imports, so these tests need neither the game nor the simulator."""

from __future__ import annotations

import json
import os
import time

import pytest
from nn_twin.game import Game, Observation
from nn_twin.runner import Runner
from nn_twin.semantics import View

SEED = "TW1NA10S01"


def save_doc(*, history: int, visited: int, room_type: str | None = None, seed: str = SEED) -> dict:
    return {"schema_version": 20, "rng": {"seed": seed}, "ascension": 10, "game_mode": "standard",
            "players": [{"character_id": "CHARACTER.SILENT"}], "acts": [{"id": "ACT.UNDERDOCKS", "rooms": {}}],
            "current_act_index": 0, "map_point_history": [[{} for _ in range(history)]],
            "visited_map_coords": [{"col": 3, "row": r} for r in range(visited)],
            "pre_finished_room": {"room_type": room_type} if room_type else None}


class FakeGame(Game):
    name = "fake"
    supports_saves = True

    def __init__(self, path):
        self.path = path

    def latest_save(self):
        return self.path if self.path.exists() else None


class FakeEnv:
    def __init__(self, twin):
        self.twin = twin

    def snapshot(self, slot):
        self.twin.snapshots.append(self.twin.state)
        return len(self.twin.snapshots) - 1

    def restore(self, slot, entry):
        self.twin.state = self.twin.snapshots[entry]


class FakeTwin:
    def __init__(self, view: View):
        self.state = view
        self.snapshots: list = []
        self.env = FakeEnv(self)
        self.imported: list = []
        self.after_import: View | None = None

    def view(self):
        return self.state

    def acts(self):
        return []

    def start_save(self, path):
        self.imported.append(path)
        self.state = self.after_import

    def state_hash(self):
        return "0"


def view(screen="combat", floor=3, hp=50):
    return View(screen, act=1, floor=floor, hp=hp, max_hp=70, gold=99, potions=(), relics=("R",))


def make(tmp_path, doc, twin_view, *, mode="follow", anchor="desync"):
    path = tmp_path / "current_run.save"
    path.write_text(json.dumps(doc))
    twin = FakeTwin(twin_view)
    r = Runner(FakeGame(path), twin, None, tmp_path / "out", character="SILENT", ascension=10, seed=SEED,
               anchor=anchor, log=lambda *_: None)
    r.twin_ok, r.mode = True, mode
    return r, twin, path


def obs(v: View) -> Observation:
    return Observation(raw={}, view=v, acts=[])


def test_room_entry_save_is_one_floor_ahead_of_its_history(tmp_path):
    """A room-entry save lists the finished rooms only: history 2 + the room being entered = floor 3."""
    real = view(floor=3)
    r, twin, _ = make(tmp_path, save_doc(history=2, visited=3), view(floor=3, hp=40))
    twin.after_import = view(floor=3)
    r.since_boundary, r.boundary_kind = 0, "room"
    r.maybe_resync(obs(real))
    assert len(twin.imported) == 1 and r.mode == "lockstep" and r.resyncs[-1]["ok"] is True
    assert r.resyncs[-1]["floor"] == 3 and r.resyncs[-1]["entry_point"] == "entering_map_point"


def test_post_combat_save_only_on_the_rewards_screen(tmp_path):
    doc = save_doc(history=3, visited=3, room_type="monster")
    r, twin, _ = make(tmp_path, doc, view(floor=3, hp=40))
    twin.after_import = view("rewards", floor=3)
    assert r.import_save(obs(view("combat", floor=3)), reason="follow", kinds=("post_combat_rewards",)) is None
    assert r.import_save(obs(view("rewards", floor=3)), reason="follow", kinds=("post_combat_rewards",))["ok"]


def test_a_save_of_another_kind_or_floor_or_seed_is_ignored(tmp_path):
    r, twin, path = make(tmp_path, save_doc(history=3, visited=3, room_type="monster"), view(floor=3))
    assert r.import_save(obs(view("rewards", floor=3)), reason="follow", kinds=("entering_map_point",)) is None
    path.write_text(json.dumps(save_doc(history=2, visited=3)))           # room entry of floor 3
    assert r.import_save(obs(view(floor=4)), reason="follow", kinds=("entering_map_point",)) is None
    path.write_text(json.dumps(save_doc(history=2, visited=3, seed="OTHER0SEED")))
    assert r.import_save(obs(view(floor=3)), reason="follow", kinds=("entering_map_point",)) is None
    assert twin.imported == []


def test_a_save_is_imported_once(tmp_path):
    r, twin, path = make(tmp_path, save_doc(history=2, visited=3), view(floor=3, hp=40))
    twin.after_import = view(floor=3)
    assert r.import_save(obs(view(floor=3)), reason="follow", kinds=("entering_map_point",))["ok"]
    assert r.import_save(obs(view(floor=3)), reason="follow", kinds=("entering_map_point",)) is None
    t = time.time() + 5
    os.utime(path, (t, t))                                                 # the game rewrote it
    assert r.import_save(obs(view(floor=3)), reason="follow", kinds=("entering_map_point",)) is not None
    assert len(twin.imported) == 2


def test_proactive_import_that_would_desync_an_in_sync_twin_is_reverted(tmp_path):
    real = view(floor=3)
    r, twin, _ = make(tmp_path, save_doc(history=2, visited=3), view(floor=3), mode="lockstep", anchor="room")
    twin.after_import = view(floor=3, hp=1)
    res = r.import_save(obs(real), reason="anchor", kinds=("entering_map_point",))
    assert res["ok"] is False and r.resyncs[-1]["reverted"] is True
    assert twin.view() == real and r.mode == "lockstep"


def test_follow_mode_resync_keeps_the_closer_twin_and_stays_in_follow(tmp_path):
    real = view(floor=3)
    r, twin, _ = make(tmp_path, save_doc(history=2, visited=3), view(floor=3, hp=40), mode="follow")
    twin.after_import = view(floor=3, hp=49)
    res = r.import_save(obs(real), reason="follow", kinds=("entering_map_point",))
    assert res["ok"] is False and not r.resyncs[-1].get("reverted") and r.mode == "follow"
    assert twin.view().hp == 49


@pytest.mark.parametrize("prev,cur,kind", [
    (view("map", floor=2), view("combat", floor=3), "room"),
    (view("combat", floor=3), view("rewards", floor=3), "combat_end"),
    (view("event", floor=1), view("map", floor=1), "event_end"),
])
def test_boundaries(tmp_path, prev, cur, kind):
    r, _, _ = make(tmp_path, save_doc(history=1, visited=1), view())
    r.since_boundary = 5
    r.boundary(prev)
    r.boundary(cur)
    assert r.boundary_kind == kind and r.since_boundary == 0
