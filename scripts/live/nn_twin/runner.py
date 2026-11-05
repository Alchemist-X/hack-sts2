"""Twin lockstep: a trained checkpoint plays the game through a simulator twin.

Per decision:
  1. observe the game (settled frame, its candidates) and the twin;
  2. UI glue -- steps that exist on one side only (event dialogue, the game's confirm button after a selection the
     sim settles on its last pick, leaving a treasure room, the sim's explicit OpenChest, ...);
  3. resync -- in follow mode (or with ``anchor="room"`` always) re-seed the twin from the game's own
     ``current_run.save`` when the game stands exactly at a save point (room entry, post-combat rewards, ancient done);
  4. diff the twin's view against the game's (floor, act, HP, gold, energy, block, hand, enemies, potions, relics and
     the screen's content); the first hard difference switches to follow mode;
  5. the network scores the twin's candidates; in lockstep the greedy row must map exactly onto a game candidate, in
     follow mode the best-ranked row that maps is taken (exact, then ignoring upgrades), and when nothing maps a
     deterministic real-side fallback acts (logged as such, never as the network's);
  6. execute in the game, step the twin with the same row, log; with prompt macros (``--macros all``) a select whose
     twin step committed the prompt is followed by the game's confirm (macros.py "Prompt macros").
"""

from __future__ import annotations

import json
import signal
import time
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from .game import Game, GameError, Observation
from .macros import (MACRO_FALLBACK, is_true_cancel, map_open, pending_selection, play_plan, play_prompt_commit,
                     prompt_committed)
from .semantics import SELECTION_SCREENS, Act, View, compare_views, match_act, screens_compatible
from .twin import Policy, Twin, TwinError

SEED_CHARS = set("0123456789ABCDEFGHJKLMNPQRSTUVWXYZ")


def canonical_seed(seed: str) -> str:
    """The game canonicalizes seeds (upper case, O->0, I->1; scripts/bench/run_bench.py); the sim hashes the text
    as given, so both sides must get the canonical form."""
    s = str(seed).strip().upper().replace("O", "0").replace("I", "1")
    if not s or set(s) - SEED_CHARS:
        raise ValueError(f"invalid seed {seed!r}")
    return s


def _now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


class Abort(RuntimeError):
    pass


class Runner:
    def __init__(self, game: Game, twin: Twin, policy: Policy, out: Path, *, character: str, ascension: int,
                 seed: str | None, anchor: str = "desync", max_decisions: int = 4000, stop_floor: int | None = None,
                 log=print, audit_tokens: bool = False, inject_desync_at: int | None = None):
        self.game, self.twin, self.policy = game, twin, policy
        self.out = Path(out)
        self.out.mkdir(parents=True, exist_ok=True)
        (self.out / "saves").mkdir(exist_ok=True)
        self.character, self.ascension, self.seed = character.upper(), int(ascension), seed
        self.anchor = anchor
        self.max_decisions = max_decisions
        self.stop_floor = stop_floor
        self.log = log
        self.audit_tokens = audit_tokens
        self.inject_at = inject_desync_at       # testing only: step the twin with another row at this decision
        self.mode = "lockstep"
        self.twin_ok = False
        self.stats = Counter()
        self.first_desyncs: list[dict] = []
        self.resyncs: list[dict] = []
        self.win_probs: list[float] = []
        self.audit: list[dict] = []
        self.executed_tokens: list = []
        self.i = 0
        self.records = 0
        self.t0 = time.monotonic()
        self.since_boundary = 0            # real actions since the last save point (room entry / combat end / ...)
        self.boundary_kind: str | None = None   # the save point just crossed: room / combat_end / event_end
        self.combat_floor: int | None = None
        self.last_twin_kind: str | None = None  # kind of the twin's last decided step (the cancel glue needs it)
        self.prev_real: View | None = None
        self.last_import_mtime = 0.0
        self.stop = False
        self.result: dict = {}
        self.error: str | None = None
        self.traj = open(self.out / "trajectory.jsonl", "a", encoding="utf-8")
        self.status_log = open(self.out / "status.log", "a", encoding="utf-8")

    # ------------------------------------------------------------------------------------------------ io

    def write(self, rec: dict) -> None:
        self.traj.write(json.dumps(rec, ensure_ascii=False, default=str) + "\n")
        self.traj.flush()
        self.records += 1

    def say(self, line: str) -> None:
        self.log(line)
        self.status_log.write(line + "\n")
        self.status_log.flush()

    # ------------------------------------------------------------------------------------------------ start

    def start(self, first: Observation) -> None:
        """Seed the twin: fresh from the run's seed at the start of the run, else from the game's save."""
        real = first.view
        info = self.game.run_info()
        seed = self.seed or info.get("seed")
        if not seed:
            raise Abort("no seed: pass --seed or let the game write current_run.save first")
        seed = canonical_seed(seed)
        if info.get("seed") and canonical_seed(info["seed"]) != seed:
            raise Abort(f"the game runs seed {info['seed']}, not {seed}")
        if info.get("character") and str(info["character"]).upper() != self.character:
            raise Abort(f"the game runs {info['character']}, not {self.character}")
        if info.get("ascension") is not None and int(info["ascension"]) != self.ascension:
            raise Abort(f"the game runs ascension {info['ascension']}, not {self.ascension}")
        self.seed = seed
        at_start = (real.floor or 0) <= 1 and real.screen == "event" and real.detail.get("event") in (None, "NEOW")
        how = None
        if at_start or not self.game.supports_saves:
            self.twin.start_fresh(self.character, seed, self.ascension)
            how = {"kind": "fresh", "seed": seed}
        else:
            how = self.import_save(first, reason="start", kinds=self.SAVE_KIND["start"], force=True)
            if how is None or not how.get("path"):
                raise Abort("the run is past its start and the game's save could not be imported; start the runner "
                            "at the beginning of a run or at a room entry")
        self.twin_ok = True
        hard, soft = compare_views(self.twin.view(), real)
        self.write({"type": "start", "t": _now(), "seed": seed, "character": self.character,
                    "ascension": self.ascension, "twin_origin": how, "game": self.game.name, "run_info": info,
                    "twin_view": self.twin.view().to_json(), "real_view": real.to_json(), "desync": hard,
                    "soft": soft, "twin_hash": self.twin.state_hash()})
        if hard:
            self.enter_follow(hard, real, "start")
        self.say(f"twin started ({how['kind']}) seed {seed} {self.character} A{self.ascension}; "
                 f"{'in sync' if not hard else 'DESYNC ' + ', '.join(d['field'] for d in hard)}")

    # ------------------------------------------------------------------------------------------------ resync

    # Which save the game writes at which boundary (sts2rec/run_save.py: entering a map point, after a won combat,
    # after an ancient event); a save of another kind on the same floor describes an earlier point of the room.
    SAVE_KIND = {"room": ("entering_map_point",), "combat_end": ("post_combat_rewards",),
                 "event_end": ("ancient_finished",),
                 "start": ("entering_map_point", "post_combat_rewards", "ancient_finished")}

    def import_save(self, obs: Observation, *, reason: str, kinds: tuple, force: bool = False) -> dict | None:
        """Re-seed the twin from a copy of the game's current_run.save if it describes the frame in view: same seed,
        the floor in view, the save kind of the boundary just crossed, not imported before.

        The imported twin is first walked through twin-only steps (a resumed ancient event still has to be left, the
        sim opens chests explicitly) and then diffed against the game. A proactive import (``anchor="room"``) that
        would make an in-sync twin differ is reverted (the twin's previous state was snapshotted)."""
        from sts2rec.run_save import RunSaveError, load_run_save

        real = obs.view
        path = self.game.latest_save()
        if path is None:
            return None
        mtime = path.stat().st_mtime
        if not force and mtime <= self.last_import_mtime:
            return None
        try:
            save = load_run_save(path)
            entry = save.entry_point
        except RunSaveError as e:
            self.stats["save_unreadable"] += 1
            return {"ok": False, "error": str(e)} if force else None
        # A room-entry save is written before the room joins map_point_history (the importer appends it again:
        # RunLoop.LoadIntoLatestMapCoord), so its floor is one more than the history count; after a won combat or a
        # finished ancient the room is already in the history.
        save_floor = save.floors + (1 if entry == "entering_map_point" else 0)
        if canonical_seed(save.seed) != self.seed or save_floor != real.floor or entry not in kinds:
            return None
        if entry == "post_combat_rewards" and real.screen != "rewards":
            return None
        n = len(self.resyncs) + 1
        copy = self.out / "saves" / f"{n:03d}-f{save_floor:02d}-{entry}.save"
        copy.write_bytes(path.read_bytes())
        self.last_import_mtime = mtime
        before = self.twin.view() if self.twin_ok else None
        before_hard = compare_views(before, real)[0] if before is not None else None
        prev_entry = self.twin.env.snapshot(0) if self.twin_ok else None
        rec = {"type": "resync", "t": _now(), "i": self.i, "reason": reason, "save": str(copy),
               "entry_point": entry, "floor": save_floor, "mode_before": self.mode}
        try:
            self.twin.start_save(str(copy))
        except Exception as e:  # noqa: BLE001  (Sts2VecError: unsupported save)
            self.stats["resync_failed"] += 1
            rec.update(ok=False, error=str(e)[:500])
            if prev_entry is not None:
                self.twin.env.restore(0, prev_entry)
                rec["reverted"] = True
            self.write(rec)
            self.resyncs.append(rec)
            self.say(f"      resync FAILED at floor {save_floor} ({entry}): {str(e)[:160]}")
            return None
        imported = self.twin.view()        # the save's own point: comparable with the chained twin's `before`
        settled = self.settle_twin(obs)
        after = self.twin.view()
        hard, soft = compare_views(after, real)
        rec.update(ok=not hard, desync_after=hard, twin_only_steps=settled, twin_hash=self.twin.state_hash(),
                   chained_vs_save=compare_views(before, imported)[0] if before is not None else None)
        reverted = bool(hard and before_hard == [] and prev_entry is not None and reason != "start")
        if reverted:
            self.twin.env.restore(0, prev_entry)
            rec["reverted"] = True
            self.stats["resync_reverted"] += 1
        self.twin_ok = True
        self.write(rec)
        self.resyncs.append(rec)
        self.stats["resyncs"] += 1
        if not hard:
            if self.mode != "lockstep":
                self.say(f"      resync OK at floor {save_floor} ({entry}): back in lockstep")
            elif reason == "anchor":
                self.say(f"      · anchor: twin re-seeded from the game's save at floor {save_floor} ({entry}), in sync")
            self.mode = "lockstep"
        elif reverted:
            self.say(f"      anchor import at floor {save_floor} ({entry}) would differ "
                     f"({', '.join(d['field'] for d in hard)}): kept the in-sync twin")
        else:
            self.say(f"      resync at floor {save_floor} ({entry}) still differs: "
                     f"{', '.join(d['field'] for d in hard)}")
        return {"kind": "save", "path": str(copy), "entry_point": entry, "floor": save_floor, "ok": not hard}

    def settle_twin(self, obs: Observation, limit: int = 4) -> list[str]:
        """Twin-only steps right after an import, until the twin stands where the game does."""
        done = []
        for _ in range(limit):
            tv = self.twin.view()
            tg = self.twin_glue(obs, tv, self.twin.acts())
            if tg is None or tg[1] is None:
                break
            self.twin.step(tg[1].native)
            done.append(tg[1].describe())
        return done

    def maybe_resync(self, obs: Observation) -> None:
        """At a save point with nothing done since it: re-seed the twin from the game's save when the twin no longer
        mirrors the game (follow mode / twin unusable), or always with ``anchor="room"``."""
        if not self.game.supports_saves or self.since_boundary != 0 or self.boundary_kind is None:
            return
        needed = self.mode != "lockstep" or not self.twin_ok
        if not needed and self.anchor != "room":
            return
        kinds = self.SAVE_KIND[self.boundary_kind]
        # The game writes the save while the room loads (the eval API reports it as busy); give a lagging write time.
        # Only ancient events write a save when they end, before the map frame settles: no wait there.
        wait = 0.0 if self.boundary_kind == "event_end" else (3.0 if needed else 1.0)
        deadline = time.monotonic() + wait
        while True:
            if self.import_save(obs, reason="follow" if needed else "anchor", kinds=kinds) is not None:
                self.boundary_kind = None
                return
            if time.monotonic() >= deadline:
                self.boundary_kind = None
                return
            time.sleep(0.25)

    def boundary(self, real: View) -> None:
        """Track save points: a new room, the end of a won combat (rewards), the end of an ancient event."""
        prev = self.prev_real
        self.prev_real = real
        if real.screen == "combat":
            self.combat_floor = real.floor
        if prev is None:
            return
        kind = None
        if (real.act, real.floor) != (prev.act, prev.floor):
            kind = "room"
        elif real.screen == "rewards" and prev.screen != "rewards" and self.combat_floor == real.floor:
            kind = "combat_end"
            self.combat_floor = None
        elif prev.screen == "event" and real.screen == "map":
            kind = "event_end"
        if kind is not None:
            self.since_boundary = 0
            self.boundary_kind = kind

    def enter_follow(self, hard: list[dict], real: View, where: str) -> None:
        if self.mode == "lockstep":
            self.stats["desync_events"] += 1
            if len(self.first_desyncs) < 8:
                self.first_desyncs.append({"i": self.i, "act": real.act, "floor": real.floor,
                                           "screen": real.screen, "where": where, "fields": hard})
        self.mode = "follow"

    # ------------------------------------------------------------------------------------------------ glue

    def real_glue(self, obs: Observation, tv: View | None) -> tuple[str, Act] | None:
        acts, real = obs.acts, obs.view
        adv = [a for a in acts if a.kind == "advance"]
        if adv:
            return "dialogue", adv[0]
        if tv is None:
            return None
        if real.screen == "prompt" and tv.screen not in SELECTION_SCREENS:
            if getattr(self, "last_twin_kind", None) == "cancel":
                # the sim's PromptCancel closes the whole selection; in the game a cancel on a preview (upgrade,
                # transform, enchant) only goes back to the card grid: cancel again, never confirm
                cancel = [a for a in acts if a.kind == "cancel"]
                return ("cancel", cancel[0]) if cancel else None
            conf = [a for a in acts if a.kind == "confirm"]
            if conf:
                return "confirm", conf[0]
        if tv.screen == "map" and real.screen in ("treasure", "rest", "rewards", "event", "shop"):
            leave = [a for a in acts if a.kind == "leave"]
            proceed_idx = {a.pos for a in leave}
            pending = [a for a in acts if a.kind in ("take_relic", "open_chest", "rest", "claim_reward")
                       or (a.kind == "event_option" and a.pos not in proceed_idx)]
            if leave and not pending:
                return "proceed", leave[0]
        if real.screen == "card_reward" and tv.screen in ("map", "rewards"):
            skip = [a for a in acts if a.kind == "card_alt" and a.key == ("skip",)]
            if skip:
                return "skip_reopened_card_reward", skip[0]
        return None

    def twin_glue(self, obs: Observation, tv: View, tacts: list[Act]) -> tuple[str, Act | None] | None:
        real = obs.view
        if tv.screen == "treasure" and real.screen in ("treasure", "map"):
            oc = [a for a in tacts if a.kind == "open_chest"]
            if oc and not any(a.kind == "open_chest" for a in obs.acts):
                return "open_chest", oc[0]
        if tv.screen == "prompt" and real.screen not in SELECTION_SCREENS:
            conf = [a for a in tacts if a.kind == "confirm" and a.key == ()]   # picks made, only confirm left
            if conf:
                return "confirm", conf[0]
            return "twin_only_prompt", None
        if tv.screen == "rewards" and real.screen in ("event", "map"):
            # the game closes an event's rewards screen by itself once everything is claimed; the sim waits for
            # ProceedRewards
            rest = [a for a in tacts if a.kind not in ("potion", "discard_potion")]
            if rest and all(a.kind == "leave" for a in rest):
                return "leave_rewards", rest[0]
        if tv.screen == "event" and real.screen == "map":
            # the game has already left a finished event (an ancient's save resumes the sim inside it)
            rest = [a for a in tacts if a.kind not in ("potion", "discard_potion")]
            if rest and all(a.kind == "leave" for a in rest):
                return "leave_event", rest[0]
        return None

    # ------------------------------------------------------------------------------------------------ choice

    def fallback(self, obs: Observation) -> Act | None:
        """Deterministic real-side choice when the twin cannot provide one (logged as policy "fallback")."""
        acts, v = obs.acts, obs.view
        by = lambda *kinds: [a for a in acts if a.kind in kinds]  # noqa: E731
        if v.screen == "combat":
            hp = {e[0]: e[2] for e in v.enemies or ()}
            plays = by("play")
            if plays:
                return min(plays, key=lambda a: (hp.get(a.key[2], 0) if a.key[2] is not None else 0, a.pos or 0))
            return (by("end_turn") or acts or [None])[0]
        order = {"prompt": ("confirm", "select", "cancel"), "card_reward": ("card_alt", "pick_card", "cancel"),
                 "rewards": ("claim_reward", "leave"), "map": ("travel",), "event": ("event_option", "leave"),
                 "shop": ("leave",), "rest": ("rest", "leave"), "treasure": ("open_chest", "take_relic", "leave")}
        for kind in order.get(v.screen, ()):
            cands = by(kind)
            if v.screen == "rewards" and kind == "claim_reward":
                cands = [a for a in cands if a.key[:1] != ("card",)]
            if v.screen == "rest" and kind == "rest":
                cands = sorted(cands, key=lambda a: a.key != ("HEAL",))
            if cands:
                return cands[0]
        others = [a for a in acts if a.kind not in ("potion", "discard_potion")]
        return (others or acts or [None])[0]

    @property
    def prompt_macros(self) -> bool:
        return bool(getattr(getattr(self, "twin", None), "prompt_macros", False))

    def map_row(self, tact: Act, tacts: list[Act], racts: list[Act], relaxed: bool) -> Act | None:
        if tact.kind == "macro":
            # the macro's first act (macros.SPECS: a card reward pairs by rank, SMITH / the removal by meaning); the
            # rest of its plan is played and checked step by step in decide()
            return map_open(tact, tacts, racts, relaxed)
        if self.prompt_macros and tact.kind == "select" and not relaxed:
            m = self.later_pick(tact, racts)
            if m is not None:
                return m
        m = match_act(tact, racts, tacts, relaxed=relaxed)
        if (m is not None and self.prompt_macros and tact.kind == "confirm" and tact.key == ("empty",)
                and is_true_cancel(m)):
            # prompt macros: DECLINE exists only where the game's own prompt allows declining (its confirm / skip
            # button); the cancel of a cancelable site goes back to the screen that opened the prompt instead. A
            # skippable choose-a-card screen publishes its cancel and its skip together: DECLINE is the skip.
            skips = [r for r in racts if (r.kind == "cancel" and not is_true_cancel(r))
                     or (r.kind == "card_alt" and r.key == ("skip",))]
            return skips[0] if skips else None
        return m

    def later_pick(self, tact: Act, racts: list[Act]) -> Act | None:
        """A k-pick's later pick of a card with same-key copies: the sim lists only the options above its last pick,
        STS2_MCP lists every unselected copy. Pair by the copy's rank among the twin's unpicked options of that key
        (``Twin.prompt_copy_rank``) when the game lists exactly those copies; None = the plain rank pairing."""
        rank_of = getattr(self.twin, "prompt_copy_rank", None)
        cr = rank_of(tact) if rank_of is not None else None
        if cr is None:
            return None
        rank, n = cr
        same = sorted((r for r in racts if r.kind in ("select", "pick_card") and r.key == tact.key),
                      key=lambda a: (a.pos is None, a.pos if a.pos is not None else 0))
        return same[rank] if len(same) == n and rank < n else None

    def choose(self, obs: Observation, tacts: list[Act], dec, in_sync: bool = False,
               tv_screen: str | None = None) -> tuple[int | None, Act | None, str]:
        racts = obs.acts
        if dec is not None and self.mode == "lockstep":
            m = self.map_row(tacts[dec.row], tacts, racts, relaxed=False)
            if m is not None:
                return dec.row, m, "greedy"
            if in_sync:
                # The states match but the game's interface does not offer this choice (e.g. drinking a potion outside
                # combat through STS2_MCP): take the best-ranked choice it does offer; the twin takes the same one, so
                # the two stay in lockstep.
                for r in self.second_choices(dec, tacts):
                    m = self.map_row(tacts[r], tacts, racts, relaxed=False)
                    if m is not None:
                        return r, m, "substitute"
            self.enter_follow([{"field": "unmappable", "twin": tacts[dec.row].describe(),
                                "real": [a.describe() for a in racts][:12]}], obs.view, "choose")
        # A twin on another screen than the game describes nothing the game can use (screen-independent rows such
        # as a potion discard would still "map"): the real-side fallback acts until the next save point.
        if dec is not None and (tv_screen is None or screens_compatible(tv_screen, obs.view.screen)):
            ranked = self.second_choices(dec, tacts)
            for relaxed in (False, True):
                for r in ranked:
                    m = self.map_row(tacts[r], tacts, racts, relaxed=relaxed)
                    if m is not None:
                        return r, m, "follow" if not relaxed else "follow_relaxed"
        return None, self.fallback(obs), "fallback"

    @staticmethod
    def second_choices(dec, tacts: list[Act]) -> list[int]:
        """The rows in choice order for a substitute / follow choice: a potion discard only when it is the model's
        own first choice (as a stand-in for another action it would throw a potion away)."""
        return [r for r in dec.ranked() if r == dec.row or tacts[r].kind != "discard_potion"]

    # ------------------------------------------------------------------------------------------------ main loop

    def run(self) -> dict:
        def on_signal(signum, _frame):
            self.stop = True
            self.say("stop requested: finishing the current step")
        old = signal.signal(signal.SIGINT, on_signal)
        try:
            self._run()
        except Abort as e:
            self.error = str(e)
            self.say(f"ABORT: {e}")
        except Exception as e:  # noqa: BLE001
            import traceback

            self.error = f"{type(e).__name__}: {e}"
            self.say(f"ERROR: {self.error}")
            self.write({"type": "error", "t": _now(), "i": self.i, "error": self.error,
                        "traceback": traceback.format_exc()[-4000:]})
        finally:
            signal.signal(signal.SIGINT, old)
        return self.finish()

    def observe(self) -> Observation:
        for attempt in range(6):
            try:
                obs = self.game.observe()
            except GameError as e:
                self.stats["observe_errors"] += 1
                if attempt == 5:
                    raise Abort(f"the game does not answer: {e}") from None
                time.sleep(2 + 2 * attempt)
                continue
            if obs.settled or obs.view.screen == "end":
                return obs
            self.stats["unsettled_observations"] += 1
            if attempt == 5:
                return obs
        raise Abort("unreachable")

    def _run(self) -> None:
        obs = self.observe()
        if obs.view.screen == "menu":
            raise Abort("the game is at the main menu: start a run first (or pass --start-run)")
        self.start(obs)
        self.prev_real = obs.view
        idle = 0
        while not self.stop:
            if (self.out / "PAUSE").exists():
                time.sleep(1.0)
                continue
            obs = self.observe()
            real = obs.view
            self.boundary(real)
            if real.screen == "end":
                self.say(f"run over: {real.terminal} at act {real.act} floor {real.floor}")
                break
            if real.screen == "menu":
                raise Abort("the game left the run (main menu)")
            if self.i >= self.max_decisions:
                raise Abort(f"--max-decisions {self.max_decisions} reached")
            if self.stop_floor and (real.floor or 0) >= self.stop_floor:
                self.say(f"--stop-floor {self.stop_floor} reached")
                break
            self.maybe_resync(obs)
            tv = self.twin.view() if self.twin_ok else None
            if tv is not None and tv.screen == "end" and real.screen != "end":
                self.twin_ok = False
                self.enter_follow([{"field": "terminal", "twin": tv.terminal, "real": None}], real, "twin_ended")
                tv = None
            # (1) steps that exist in the game only
            g = self.real_glue(obs, tv)
            if g is not None:
                idle += 1
                if idle > 40:
                    raise Abort("40 glue steps without a decision: the two sides do not converge")
                self.exec_glue(obs, g)
                continue
            # (2) steps that exist in the twin only
            tacts = self.twin.acts() if self.twin_ok else []
            if tv is not None:
                tg = self.twin_glue(obs, tv, tacts)
                if tg is not None:
                    idle += 1
                    if idle > 40:
                        raise Abort("40 twin-only steps without a decision")
                    self.twin_only(obs, tv, tacts, tg)
                    continue
            idle = 0
            self.decide(obs, tv, tacts)
        if self.stop:
            self.say("stopped by the user")

    def exec_glue(self, obs: Observation, g: tuple[str, Act]) -> None:
        why, act = g
        res = self.game.execute(act)
        self.since_boundary += 1 if res.ok else 0
        self.executed_tokens.append(res.wire)
        self.stats["glue_" + why] += 1
        self.write({"type": "glue", "t": _now(), "i": self.i, "why": why, "where": real_floor(obs.view),
                    "real_action": act.to_json(), "ok": res.ok, "error": res.error, "ms": round(res.ms),
                    "notes": res.notes})
        self.say(f"      · glue {why}: {act.describe()}{'' if res.ok else ' FAILED ' + str(res.error)}")
        if not res.ok:
            self.stats["exec_failures"] += 1

    def twin_only(self, obs: Observation, tv: View, tacts: list[Act], tg) -> None:
        why, act = tg
        dec = None
        if act is None:
            dec = self.policy.decide(self.twin.obs)
            act = tacts[dec.row]
        row = act.native
        token = self.twin.row_to_token(row) if self.audit_tokens else None
        try:
            self.count_status(self.twin.step(row))
        except TwinError as e:
            row = None
            self.twin_ok = False
            self.enter_follow([{"field": "twin_error", "twin": str(e)[:200], "real": None}], obs.view, "twin_only")
        if dec is not None:
            self.policy.commit(row)
        self.stats["twin_only_" + why] += 1
        self.write({"type": "twin_only", "t": _now(), "i": self.i, "why": why, "twin_row": row,
                    "twin_action": act.describe(), "token": token,
                    "p": round(float(dec.probs[dec.row]), 4) if dec is not None else None,
                    "real_screen": obs.view.screen})
        self.say(f"      · twin only ({why}): {act.describe()}")

    def count_status(self, st) -> None:
        if isinstance(st, int) and st & MACRO_FALLBACK:
            self.stats["twin_macro_fallback"] += 1

    def decide(self, obs: Observation, tv: View | None, tacts: list[Act]) -> None:
        real = obs.view
        if self.prompt_macros and self.twin_ok and any(a.kind == "cancel" for a in tacts):
            # prompt macros never list a PromptCancel row (PromptMacros.Apply); a table that shows one is not the env
            # the model was trained on: fail closed (fallback until a save point re-seeds the twin)
            self.stats["prompt_cancel_rows"] += 1
            self.enter_follow([{"field": "prompt_cancel_row", "twin": [a.describe() for a in tacts][:12],
                                "real": real.screen}], real, "prompt_macro")
            self.twin_ok = False
            tv, tacts = None, []
        hard, soft = compare_views(tv, real) if tv is not None else ([{"field": "twin", "twin": None,
                                                                         "real": "unavailable"}], [])
        if hard:
            self.enter_follow(hard, real, "decision")
            self.stats["desync_decisions"] += 1
        t_pol = time.perf_counter()
        dec = self.policy.decide(self.twin.obs) if self.twin_ok and tacts else None
        pol_ms = (time.perf_counter() - t_pol) * 1000
        row, ract, how = self.choose(obs, tacts, dec, in_sync=not hard, tv_screen=tv.screen if tv else None)
        if ract is None:
            raise Abort(f"no action available on screen {real.screen}")
        twin_act = tacts[row] if row is not None else None
        twin_token = self.twin.row_to_token(row) if (self.audit_tokens and row is not None) else None
        # execute in the game (a macro row is a plan: its later acts are matched after each step)
        res = self.game.execute(ract)
        executed = [ract.to_json()]
        wires = [res.wire]
        self.executed_tokens.append(res.wire)
        if res.ok:
            self.since_boundary += 1      # counted here: a macro whose later step fails has still moved the game
        macro_rec = None
        skip_twin_step = False
        if res.ok and twin_act is not None and twin_act.kind == "macro":
            # the rest of the plan, each act checked against the screen the game reached (macros.play_plan)
            mr = play_plan(twin_act, self.observe, self.game.execute, relaxed=self.mode == "follow",
                           first_acts=obs.acts)
            executed += mr.executed
            wires += mr.wires
            self.executed_tokens += mr.wires
            macro_rec = {"name": twin_act.key[0] if twin_act.key else None, "steps": mr.steps,
                         **({"failure": mr.failure} if mr.failure else {}), **({"aborted": True} if mr.aborted else {})}
            self.stats["macros"] += 1
            if not mr.ok:
                res.ok, res.error = False, mr.error
                self.stats["macro_failures"] += 1
            if mr.failure is not None:
                where = self.observe().view if not mr.aborted else real
                self.enter_follow([mr.failure], where, "macro")
                if mr.aborted:
                    # backed out of the opened screen with the game's cancel: the game stands where the twin does
                    # (nothing chosen), but the screen it opened was not the twin's -- follow, twin unstepped
                    skip_twin_step = True
                    self.stats["macro_aborts"] += 1
                else:
                    # The opened screen is not the twin's (hidden state the views cannot compare before the open, or a
                    # grid the twin does not have): the twin cannot follow it. Fallback play until the next save point
                    # re-seeds it.
                    self.twin_ok = False
        # step the twin with the same row (--inject-desync: deliberately with another one, to exercise follow + resync)
        twin_ms = 0.0
        injected = None
        if row is not None and self.twin_ok and res.ok and self.i == self.inject_at:
            alt = next((r for r in (dec.ranked() if dec is not None else range(len(tacts)))
                        if r != row and tacts[r].kind == tacts[row].kind and tacts[r].key != tacts[row].key), None)
            if alt is None:
                alt = next((r for r in range(len(tacts)) if r != row), None)
            if alt is not None:
                injected = {"executed": tacts[row].describe(), "twin_stepped_with": tacts[alt].describe()}
                self.say(f"      ! injected desync: the game did '{tacts[row].describe()}', "
                         f"the twin does '{tacts[alt].describe()}'")
                self.stats["injected_desyncs"] += 1
                row_for_twin = alt
        stepped = None                     # the row the twin took (the loop breaker records it)
        # prompt macros: a select is (select, confirm?) -- the confirm is due when the twin's env commits the prompt
        prompt_pre = (self.twin.prompt_marker() if (self.prompt_macros and self.twin_ok and row is not None
                                                     and not injected and twin_act is not None
                                                     and twin_act.kind == "select") else None)
        prompt_rec = None
        if row is not None and self.twin_ok and not skip_twin_step:
            t_tw = time.perf_counter()
            try:
                if res.ok:
                    stepped = row_for_twin if injected else row
                    self.count_status(self.twin.step(stepped))
                    if prompt_pre is not None and self.twin_ok:
                        prompt_rec = self.prompt_commit(prompt_pre, executed, wires)
                else:
                    self.stats["exec_failures"] += 1
                    if prompt_pre is not None:
                        stepped, prompt_rec = self.prompt_select_after_failure(row, prompt_pre, executed, wires)
                    else:
                        stepped = row if self.step_twin_if_game_moved(row) else None
            except TwinError as e:
                stepped = None
                self.twin_ok = False
                self.enter_follow([{"field": "twin_error", "twin": str(e)[:200], "real": None}], real, "step")
            twin_ms = (time.perf_counter() - t_tw) * 1000
            if prompt_rec is not None:
                if prompt_rec.get("committed") and prompt_pre.needs_confirm and twin_token is not None:
                    twin_token += "; confirm"          # the sim's own confirm click inside the macro step
                if prompt_rec.get("failure"):
                    res.ok, res.error = False, prompt_rec["error"]
        elif not res.ok:
            self.stats["exec_failures"] += 1
        real_wire = wires[0] if len(wires) == 1 else "; ".join(str(w) for w in wires)   # a macro: its plan's wires
        if dec is not None:
            self.policy.commit(stepped)
            if dec.loop_break and stepped == dec.row:
                self.stats["loop_breaks"] += 1
        self.last_twin_kind = tacts[stepped].kind if stepped is not None and stepped < len(tacts) else None
        if how != "greedy":
            self.stats["decisions_" + how] += 1
        self.stats["decisions"] += 1
        if self.audit_tokens and row is not None:
            self.audit.append({"i": self.i, "twin_token": twin_token, "standin_token": real_wire,
                               "equal": twin_token == real_wire})
        top = dec.top(5) if dec is not None else []
        rec = {"type": "decision", "t": _now(), "i": self.i, "act": real.act, "floor": real.floor,
               "screen": real.screen, "twin_screen": tv.screen if tv else None,
               "hp": [real.hp, real.max_hp], "gold": real.gold, "energy": real.energy, "mode": self.mode,
               "policy": how, **({"substituted_for": tacts[dec.row].describe()} if how == "substitute" else {}),
               **({"loop_break": {"model_argmax": tacts[dec.plain].describe(), "p_argmax": round(float(
                   dec.probs[dec.plain]), 4)}} if (dec is not None and dec.loop_break and row == dec.row) else {}),
               "twin_action": ({"row": row, **twin_act.to_json(), "token": twin_token}
                                              if twin_act is not None else None),
               "real_action": executed, "ok": res.ok, "error": res.error, "notes": res.notes,
               **({"macro": macro_rec} if macro_rec is not None else {}),
               **({"prompt_macro": prompt_rec} if prompt_rec is not None else {}),
               "p": round(float(dec.probs[row]), 4) if (dec is not None and row is not None) else None,
               "win_prob": round(dec.win_prob, 4) if (dec is not None and dec.win_prob is not None) else None,
               "combat_hp_pred": round(dec.combat_hp, 4) if (dec is not None and dec.combat_hp is not None) else None,
               "value": round(dec.value, 4) if dec is not None else None,
               "top5": [{"row": r, "p": round(p, 4), "action": tacts[r].describe()} for r, p in top],
               "desync": hard, "soft": soft, **({"injected_desync": injected} if injected else {}),
               "ms": {"observe": round(obs.ms), "policy": round(pol_ms, 1), "exec": round(res.ms),
                      "twin": round(twin_ms, 2)},
               "twin_hash": self.twin.state_hash() if self.twin_ok else None}
        self.write(rec)
        if rec["win_prob"] is not None:
            self.win_probs.append(rec["win_prob"])
        self.status(rec, twin_act, ract, dec, row)
        self.i += 1

    def prompt_commit(self, pre, executed: list, wires: list) -> dict:
        """After a prompt-macro select (game and twin both picked): when the twin's env committed the prompt, play the
        game's confirm (``macros.play_prompt_commit``); a failure fails closed (twin disabled, fallback until a save
        point). -> the decision record's ``prompt_macro`` field."""
        after = self.twin.prompt_marker()
        committed = prompt_committed(pre, after)
        rec = {"committed": committed, "picked": pre.picked, "needs_confirm": pre.needs_confirm,
               "bounds": [pre.lo, pre.hi]}
        self.stats["prompt_selects"] += 1
        if not committed:
            return rec
        self.stats["prompt_commits"] += 1
        pr = play_prompt_commit(self.observe, self.game.execute, twin_in_prompt=after is not None,
                                twin_picked=pre.picked + 1)
        executed += pr.executed
        wires += pr.wires
        self.executed_tokens += pr.wires
        self.since_boundary += len(pr.wires)
        rec.update(status=pr.status, steps=pr.steps)
        self.stats["prompt_commit_" + pr.status] += 1
        if pr.failure is not None:
            rec.update(failure=pr.failure, error=pr.error)
            self.stats["prompt_commit_failures"] += 1
            self.enter_follow([pr.failure], self.observe().view, "prompt_macro")
            self.twin_ok = False
        return rec

    def prompt_select_after_failure(self, row: int, pre, executed: list, wires: list) -> tuple[int | None, dict | None]:
        """A prompt-macro select the game reported as failed / unconfirmed. The twin's step may commit (and confirm)
        the prompt, so the generic tentative step (``step_twin_if_game_moved``) would compare a twin that left the
        prompt with a game that shows the pick unconfirmed, revert, and leave the game one pick ahead unseen (both
        sides publish their options only before the first pick). Read the game's pick count instead:
        - still ``pre.picked`` (no count published and no picks shown = 0): the select did not happen, the twin
          stays unstepped;
        - ``pre.picked + 1``: it did; step the twin and play the commit (``prompt_commit``: the game's confirm when
          the twin's env committed);
        - the game left the selection screen: the generic tentative step decides;
        - anything else (another count, or none published): fail closed (fallback until a save point).
        -> (the row the twin took or None, the decision record's ``prompt_macro`` field or None)"""
        try:
            now = self.game.observe().view
        except GameError:
            return None, None
        if now.screen != "prompt":
            if not self.step_twin_if_game_moved(row):
                return None, None
            return row, self.prompt_commit(pre, executed, wires)
        game_picked = now.picked if now.picked is not None else (None if pending_selection(now) else 0)
        if game_picked == pre.picked:
            return None, None
        if game_picked == pre.picked + 1:
            self.count_status(self.twin.step(row))
            self.stats["twin_step_kept_after_failure"] += 1
            self.since_boundary += 1
            return row, self.prompt_commit(pre, executed, wires)
        self.stats["prompt_select_failures"] += 1
        self.enter_follow([{"field": "prompt_picked", "twin": pre.picked + 1, "real": game_picked}], now, "prompt_macro")
        self.twin_ok = False
        return None, None

    def step_twin_if_game_moved(self, row: int) -> bool:
        """After a failed / unconfirmed execution: step the twin tentatively and keep the step only if the game shows
        the same state now (the action did happen, its confirmation timed out)."""
        try:
            now = self.game.observe()
        except GameError:
            return False
        entry = self.twin.env.snapshot(0)
        before = compare_views(self.twin.view(), now.view)[0]
        self.twin.step(row)
        after = compare_views(self.twin.view(), now.view)[0]
        if len(after) > len(before) or (after and not before):
            self.twin.env.restore(0, entry)
            self.stats["twin_step_reverted"] += 1
            return False
        self.stats["twin_step_kept_after_failure"] += 1
        self.since_boundary += 1
        return True

    def status(self, rec: dict, twin_act: Act | None, ract: Act, dec, row) -> None:
        hp = rec["hp"]
        what = (twin_act.describe() if twin_act is not None else f"[fallback] {ract.describe()}")
        if rec.get("substituted_for"):
            what += f" [instead of {rec['substituted_for']}: not offered by the game]"
        if rec.get("loop_break"):
            what += f" [loop breaker: instead of {rec['loop_break']['model_argmax']}]"
        p = f" p={rec['p']:.2f}" if rec.get("p") is not None else ""
        if rec.get("win_prob") is not None:
            conf = f"通关把握 {rec['win_prob'] * 100:.0f}%"
        elif rec.get("value") is not None:
            conf = f"V {rec['value']:+.2f}"
        else:
            conf = "-"
        sync = "sync" if not rec["desync"] else "DESYNC " + ",".join(sorted({d["field"] for d in rec["desync"]}))
        if self.mode == "follow" and not rec["desync"]:
            sync = "follow"
        energy = f" E{rec['energy']}" if rec.get("energy") is not None and rec["screen"] == "combat" else ""
        ok = "" if rec["ok"] else f" EXEC-FAIL({rec['error']})"
        self.say(f"#{rec['i']:04d} A{rec['act']} F{rec['floor'] or 0:02d} {rec['screen']:<11} HP {hp[0]}/{hp[1]}"
                 f"{energy} | {what}{p} | {conf} | {sync}{ok}")

    # ------------------------------------------------------------------------------------------------ end

    def finish(self) -> dict:
        wall = time.monotonic() - self.t0
        try:
            last = self.game.observe() if self.error is None else None
        except Exception:  # noqa: BLE001
            last = None
        try:
            result = self.game.result()
        except Exception as e:  # noqa: BLE001
            result = {"error": str(e)}
        decisions = self.stats["decisions"]
        desync_dec = self.stats["desync_decisions"]
        s = {"schema": "nn-twin-live/v1", "finished_at": _now(), "game": self.game.name,
             "seed": self.seed, "character": self.character, "ascension": self.ascension,
             "checkpoint": self.policy.meta, "twin": {"content": self.twin.content,
                                                       "encoder_version": self.twin.schema.encoder_version},
             "anchor": self.anchor, "decisions": decisions, "records": self.records,
             "desync_decisions": desync_dec, "desync_events": self.stats["desync_events"],
             "first_desyncs": self.first_desyncs,
             "resyncs": {"count": len(self.resyncs), "ok": sum(1 for r in self.resyncs if r.get("ok")),
                         "events": [{k: r.get(k) for k in ("i", "floor", "entry_point", "ok", "reason", "error")}
                                    for r in self.resyncs][:50]},
             "policy_sources": {"greedy": decisions - sum(self.stats[k] for k in self.stats
                                                          if k.startswith("decisions_")),
                                **{k[len("decisions_"):]: v for k, v in self.stats.items()
                                   if k.startswith("decisions_")}},
             "counters": dict(self.stats), "final_mode": self.mode,
             "final_view": last.view.to_json() if last is not None else None,
             "twin_final_view": self.twin.view().to_json() if self.twin_ok else None,
             "result": result, "error": self.error, "wall_s": round(wall, 1),
             "s_per_decision": round(wall / decisions, 2) if decisions else None,
             "loop_breaker": self.policy.loop_totals() if hasattr(self.policy, "loop_totals") else None,
             "win_prob": ({"first": self.win_probs[0], "last": self.win_probs[-1], "min": min(self.win_probs),
                           "max": max(self.win_probs)} if self.win_probs else None)}
        if self.audit_tokens:
            hist = getattr(self.game, "history", lambda: [])()
            mism = [a for a in self.audit if not a["equal"]]
            s["dry_run_audit"] = {"lockstep_steps_compared": len(self.audit), "token_mismatches": len(mism),
                                  "first_mismatches": mism[:5], "standin_history_len": len(hist),
                                  "executed_len": len(self.executed_tokens),
                                  "history_equal": hist == [t for t in self.executed_tokens]}
        with open(self.out / "summary.json", "w", encoding="utf-8") as f:
            json.dump(s, f, ensure_ascii=False, indent=1, default=str)
        self.traj.close()
        self.status_log.close()
        return s


def real_floor(v: View) -> str:
    return f"A{v.act}F{v.floor} {v.screen}"
