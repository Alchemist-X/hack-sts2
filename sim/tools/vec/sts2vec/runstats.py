"""Run-state fields of finished episodes / games, for logs only (``sts2train`` episodes.jsonl, ``sts2vec eval`` game
summaries; 2026-11-05, wave 6: the death analysis could not tell HP / deck / relics / room / the killing encounter /
HP at a boss from the logs).

``RunStats(schema, num_envs, fights=False)`` keeps, per env slot, a few scalars of the **observation the slot last
decided at** (``observe(obs, live)``, called with the observation the actions are chosen for, i.e. before
``env.step``). With auto-reset, a slot whose episode ended in a step already shows the next episode's first
observation after that step, so a finished episode's fields come from the snapshot taken before the step that ended
it, never from ``obs`` after it: ``end(slot, last_episode_row)`` builds them from that snapshot plus the step's
``last_episode`` row (which the env writes for the dying episode), and ``reset(done)`` clears the slots' per-episode
state so the successor starts empty. Nothing here changes an action or an RNG: it only reads buffers.

Fields (``FIELDS``; ints unless noted; null when the library lacks the column or the slot was never observed):

- ``hp`` / ``max_hp``: ``last_episode.final_hp`` / ``final_max_hp`` (the end of the episode: 0 at a death).
- ``deck`` (deck size, ``global_i.deck_n``), ``upgraded`` (deck cards with ``upgrade`` > 0), ``relics``
  (``relic_n``), ``gold``, ``potions`` (filled potion slots): at the last decision of the episode (the effects of the
  action taken there are not included; a death leaves them unchanged).
- ``room``: the kind of the final room: the ``MapPointType`` name of the current map node (``map_nodes`` at
  ``global_i.map_row`` / ``map_col``): ``Monster``, ``Elite``, ``Boss``, ``RestSite``, ``Shop``, ``Treasure``,
  ``Ancient``; an ``Unknown`` node is resolved by what the episode saw on that floor (a fight -> ``Monster``, else the
  first ``Event`` / ``Shop`` / ``Treasure`` / ``RestSite`` phase; ``Unknown`` when none was seen); null before the
  first map move.
- ``killer``: the encounter id (encounters vocabulary name, ``last_episode.last_encounter_id``) of the fight a death
  happened in; null when the episode did not end in a death or the death was outside combat.
- ``boss_entry``: one ``{act, floor, hp, max_hp, boss}`` per act-boss room the episode entered (a Boss map node with
  a combat in progress; hp / max_hp at the fight's first decision; ``boss`` = the encounter id of ``global_i.boss_id``,
  or ``second_boss_id`` for the second boss of a double-boss act), in order; ``[]`` when none.
- ``fights`` (only with ``fights=True``; ``sts2vec eval``): one ``{floor, act, room, enemies, encounter, hp_before,
  max_hp_before, result, hp_after, max_hp}`` per combat: ``enemies`` = the monster ids present at the fight's first
  decision; ``encounter`` = the boss's encounter id for a boss fight, the killer for the lost fight, else null (the
  observation carries no encounter id during a fight); ``result`` ``won`` (the slot decided out of combat afterwards,
  or the run was won in it), ``lost`` (died in it) or ``unfinished`` (truncated / capped in it); ``hp_after`` /
  ``max_hp`` at the first decision after the fight (at the episode end: the final values).

Cost per ``observe``: a column gather of global_i, a count over the deck's upgrade column (rows up to the largest
deck), one over the potion slots and a gather of the current map node's type, all numpy over the slots; Python work
only for the slots entering a boss fight (and, with ``fights``, starting or ending a fight).
"""

from __future__ import annotations

import numpy as np

FIELDS = ("hp", "max_hp", "deck", "upgraded", "relics", "gold", "potions", "killer", "boss_entry", "room")
FIGHT_KEYS = ("floor", "act", "room", "enemies", "encounter", "hp_before", "max_hp_before", "result", "hp_after",
              "max_hp")
# snapshot columns (RunStats.last)
_HP, _MAXHP, _GOLD, _DECK, _RELICS, _COMBAT, _ACT, _FLOOR, _UPG, _POT, _PT, _RES = range(12)
_G_NAMES = ("hp", "max_hp", "gold", "deck_n", "relic_n", "is_combat", "act_index", "total_floor")
RES_COMBAT = 1          # RunStats.res: 0 = nothing seen on this floor, 1 = a fight, 2 + RunPhase id = that room phase
ROOM_PHASES = ("Event", "Shop", "Treasure", "RestSite")


class RunStats:
    """Per-slot run-state snapshot and per-episode boss / fight records (module docstring)."""

    def __init__(self, schema, num_envs: int, *, fights: bool = False):
        self.n = int(num_envs)
        self.fights_on = bool(fights)
        G = schema.cols("global_i")
        self._g = [G.get(k, -1) for k in _G_NAMES]
        self._row, self._col = G.get("map_row", -1), G.get("map_col", -1)
        self._phase = G.get("phase", -1)
        self._boss_id, self._boss2_id = G.get("boss_id", -1), G.get("second_boss_id", -1)
        has = schema.names
        self._up = schema.cols("deck").get("upgrade", -1) if "deck" in has else -1
        self._deck_rows = schema["deck"].shape[0] if "deck" in has else 0
        self._pot = schema.cols("potions").get("potion_id", -1) if "potions" in has else -1
        self._pt = schema.cols("map_nodes").get("point_type", -1) if "map_nodes" in has else -1
        self._map_shape = tuple(schema["map_nodes"].shape[:2]) if "map_nodes" in has else (0, 0)
        self._bd = schema.cols("map_routes").get("boss_dist", -1) if "map_routes" in has else -1
        E = schema.cols("enemies") if "enemies" in has else {}
        self._e_present, self._e_monster = E.get("present", -1), E.get("monster_id", -1)
        self._le = {f: i for i, f in enumerate(schema["last_episode"].fields)}
        enums, lists = schema.vocab.enums, schema.vocab.lists
        self.pt_names = list(enums.get("MapPointType") or [])
        self.phase_names = list(enums.get("RunPhase") or [])
        self.enc_name = lists["encounters"].name if "encounters" in lists else (lambda i: f"<{int(i)}>")
        self.mon_name = lists["monsters"].name if "monsters" in lists else (lambda i: f"<{int(i)}>")
        self._boss_pt = self.pt_names.index("Boss") if "Boss" in self.pt_names else -99
        self._unknown_pt = self.pt_names.index("Unknown") if "Unknown" in self.pt_names else -99
        self._room_phase_ids = np.array([self.phase_names.index(p) for p in ROOM_PHASES if p in self.phase_names],
                                        dtype=np.int64)
        self.last = np.full((self.n, 12), -1, dtype=np.int64)
        self.floor_prev = np.full(self.n, -1, dtype=np.int64)
        self.res = np.zeros(self.n, dtype=np.int64)
        self.in_boss = np.zeros(self.n, dtype=bool)
        self.in_combat = np.zeros(self.n, dtype=bool)
        self.bosses: list[list[dict]] = [[] for _ in range(self.n)]
        self.fights: list[list[dict]] = [[] for _ in range(self.n)]

    # ---------------------------------------------------------------------------------------------- per step
    def observe(self, obs: dict, live=None) -> None:
        """Snapshot the observation the slots decide at (before ``env.step``); `live` = bool [N] of the slots that
        decide (None = all)."""
        gi = obs["global_i"]
        rows = np.arange(self.n) if live is None else np.flatnonzero(np.asarray(live, dtype=bool))
        m = len(rows)
        if m == 0:
            return
        sel = slice(None) if live is None else rows         # basic slicing (no copy) when every slot decides
        snap = np.full((m, 12), -1, dtype=np.int64)
        for j, c in enumerate(self._g):
            if c >= 0:
                snap[:, j] = gi[sel, c]
        if self._up >= 0 and self._g[3] >= 0:
            d = int(min(self._deck_rows, max(0, int(snap[:, _DECK].max()))))
            snap[:, _UPG] = np.count_nonzero(obs["deck"][sel, :d, self._up] > 0, axis=1) if d else 0
        if self._pot >= 0:
            snap[:, _POT] = np.count_nonzero(obs["potions"][sel, :, self._pot] > 0, axis=1)
        pt = np.zeros(m, dtype=np.int64)
        if self._pt >= 0 and self._row >= 0 and self._col >= 0 and "map_nodes" in obs:
            r, c = gi[sel, self._row].astype(np.int64), gi[sel, self._col].astype(np.int64)
            R, C = self._map_shape
            ok = (r >= 0) & (r < R) & (c >= 0) & (c < C)
            pt = np.where(ok, obs["map_nodes"][rows, np.clip(r, 0, R - 1), np.clip(c, 0, C - 1), self._pt], 0)
        snap[:, _PT] = pt
        floor = snap[:, _FLOOR]
        newf = floor != self.floor_prev[rows]
        res = np.where(newf, 0, self.res[rows])
        combat = snap[:, _COMBAT] > 0
        res[combat] = RES_COMBAT
        if self._phase >= 0 and len(self._room_phase_ids):
            ph = gi[sel, self._phase].astype(np.int64)
            rp = (res == 0) & np.isin(ph, self._room_phase_ids)
            res[rp] = 2 + ph[rp]
        snap[:, _RES] = res
        boss_now = combat & (pt == self._boss_pt)
        enter = boss_now & (~self.in_boss[rows] | newf)
        for k in np.flatnonzero(enter).tolist():
            s = int(rows[k])
            self.bosses[s].append(self._boss_row(obs, s, snap[k]))
        if self.fights_on:
            was = self.in_combat[rows]
            for k in np.flatnonzero(was & ~combat).tolist():
                f = self.fights[int(rows[k])]
                if f and f[-1]["result"] is None:
                    f[-1].update(result="won", hp_after=int(snap[k, _HP]), max_hp=int(snap[k, _MAXHP]))
            for k in np.flatnonzero(combat & (~was | newf)).tolist():
                s = int(rows[k])
                self.fights[s].append(self._fight_row(obs, s, snap[k]))
            self.in_combat[rows] = combat
        self.in_boss[rows] = boss_now
        self.floor_prev[rows] = floor
        self.res[rows] = res
        self.last[rows] = snap

    def _boss_name(self, obs: dict, s: int, snap) -> str | None:
        gi = obs["global_i"][s]
        if self._bd >= 0 and "map_routes" in obs:
            r, c = int(gi[self._row]), int(gi[self._col])
            second = int(obs["map_routes"][s, r, c, self._bd]) != 0      # -1 at the second boss (boss not reachable)
        else:
            second = any(b["act"] == int(snap[_ACT]) + 1 for b in self.bosses[s])
        col = self._boss2_id if second else self._boss_id
        if col < 0 or int(gi[col]) <= 0:
            return None
        return self.enc_name(int(gi[col]))

    def _boss_row(self, obs: dict, s: int, snap) -> dict:
        return {"act": int(snap[_ACT]) + 1, "floor": int(snap[_FLOOR]), "hp": int(snap[_HP]),
                "max_hp": int(snap[_MAXHP]), "boss": self._boss_name(obs, s, snap)}

    def _fight_row(self, obs: dict, s: int, snap) -> dict:
        enemies = []
        if self._e_present >= 0 and self._e_monster >= 0 and "enemies" in obs:
            enemies = [self.mon_name(int(x[self._e_monster])) for x in obs["enemies"][s] if int(x[self._e_present]) > 0]
        boss = int(snap[_PT]) == self._boss_pt
        return {"floor": int(snap[_FLOOR]), "act": int(snap[_ACT]) + 1, "room": self._room(snap),
                "enemies": enemies, "encounter": self._boss_name(obs, s, snap) if boss else None,
                "hp_before": int(snap[_HP]), "max_hp_before": int(snap[_MAXHP]), "result": None, "hp_after": None,
                "max_hp": None}

    # ---------------------------------------------------------------------------------------------- per episode
    def _room(self, snap) -> str | None:
        pt = int(snap[_PT])
        if pt <= 0 or pt >= len(self.pt_names):
            return None
        if pt != self._unknown_pt:
            return self.pt_names[pt]
        res = int(snap[_RES])
        if res == RES_COMBAT:
            return "Monster"
        if res >= 2 and res - 2 < len(self.phase_names):
            return self.phase_names[res - 2]
        return "Unknown"

    def end(self, slot: int, last_episode_row, *, win: bool | None = None) -> dict:
        """The fields (``FIELDS``, + ``fights``) of the episode that just ended on `slot`, from its snapshot and its
        ``last_episode`` row of this step (`win` overrides the row's win column). Call before ``reset``."""
        s = int(slot)
        le = self._le
        row = [int(x) for x in last_episode_row]
        won = bool(win) if win is not None else bool(row[le["win"]]) if "win" in le else False
        end_kind = row[le["end_kind"]] if "end_kind" in le else 1
        died = end_kind == 1 and not won
        snap = self.last[s]
        known = bool(snap[_FLOOR] >= 0)                       # observed at least once this episode

        def v(j):
            return int(snap[j]) if known and snap[j] >= 0 else None

        hp = row[le["final_hp"]] if "final_hp" in le else v(_HP)
        max_hp = row[le["final_max_hp"]] if "final_max_hp" in le else v(_MAXHP)
        enc = row[le["last_encounter_id"]] if "last_encounter_id" in le else 0
        in_fight = known and snap[_COMBAT] > 0
        killer = self.enc_name(enc) if died and in_fight and enc > 0 else None
        out = {"hp": hp, "max_hp": max_hp, "deck": v(_DECK), "upgraded": v(_UPG), "relics": v(_RELICS),
               "gold": v(_GOLD), "potions": v(_POT), "killer": killer, "boss_entry": list(self.bosses[s]),
               "room": self._room(snap) if known else None}
        if self.fights_on:
            f = [dict(x) for x in self.fights[s]]
            if f and f[-1]["result"] is None:
                f[-1].update(result="lost" if died else "won" if won else "unfinished", hp_after=hp, max_hp=max_hp)
                if died and killer is not None:
                    f[-1]["encounter"] = killer
            out["fights"] = f
        return out

    def reset(self, done) -> None:
        """Clear the per-episode state of the slots in `done` (bool [N] or slot ids): the successors start empty."""
        d = np.asarray(done)
        idx = np.flatnonzero(d) if d.dtype == bool else d.astype(np.int64).ravel()
        for s in idx.tolist():
            self.bosses[s] = []
            self.fights[s] = []
        self.floor_prev[idx] = -1
        self.res[idx] = 0
        self.in_boss[idx] = False
        self.in_combat[idx] = False
        self.last[idx] = -1

    def attach(self, finished: list[dict], last_episode, *, offset: int = 0) -> None:
        """Trainer hook: merge each finished episode's fields into its dict's ``fields`` (the episodes.jsonl row;
        fields of ``episodes.EPISODE_HOOKS`` win on a name clash); `offset` = the global slot of this tracker's slot 0
        (``--overlap double`` lanes; the dicts carry global slots). Then clears those slots."""
        for e in finished:
            s = int(e["slot"]) - offset
            f = self.end(s, last_episode[s], win=bool(e.get("win")))
            e["fields"] = {**f, **(e.get("fields") or {})}
        if finished:
            self.reset(np.array([int(e["slot"]) - offset for e in finished], dtype=np.int64))
