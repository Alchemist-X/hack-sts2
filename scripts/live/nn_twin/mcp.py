"""The real game through STS2_MCP: observe, decode, execute -- on top of the live harness's own layer.

Reused, not re-implemented:
- ``scripts/sts2_action_executor.py``: ``request`` (GET/POST /api/v1/singleplayer), ``wait_settled`` (actionable +
  quiet frame, then the eval API's ``busy`` gate when the mod has it), ``settled_state`` (per-action commitment
  witnesses: the played card left the hand, end_turn reached a new round, a map move left the map, ...);
- ``sts2rec/legal_actions.py``: ``derive_legal_actions`` -- the candidate set of the current MCP state (the same one
  the LLM harness and the E3 farm act on);
- ``sts2rec/run_save.py``: ``load_run_save`` to read the game's ``current_run.save`` (copies only).

Targets go on the wire as the enemy's ``combat_id`` (``scripts/sts2_action_refs.py``: entity ids renumber after a
kill, combat ids never do). The two retry rules of ``sts2_live.Session.settle`` are kept: an ``end_turn`` that
provably did nothing is re-sent once, a treasure claim swallowed by the chest animation is re-clicked once.

``/api/v1/eval`` (``patches/sts2mcp-eval-api.patch``) is optional: with it the runner reads the run's seed /
character / ascension, can start a seeded run (``--start-run``) and reads the final ``run_result``; without it
(the stock MCP build) the seed comes from ``current_run.save``.
"""

from __future__ import annotations

import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
for _p in (ROOT / "scripts", ROOT / "sts2rec"):
    if str(_p) not in sys.path:
        sys.path.insert(0, str(_p))

import sts2_action_executor as executor  # noqa: E402
from sts2rec.legal_actions import derive_legal_actions  # noqa: E402
from sts2rec.run_save import RunSaveError, load_run_save  # noqa: E402

from .game import ExecResult, Game, GameError, Observation  # noqa: E402
from .semantics import Act, View, norm_alt, norm_reward_type, norm_shop_category, norm_upgrade  # noqa: E402

COMBAT_TYPES = ("monster", "elite", "boss")
SCREEN = {"monster": "combat", "elite": "combat", "boss": "combat", "hand_select": "prompt",
          "card_select": "prompt", "bundle_select": "prompt", "relic_select": "prompt", "card_reward": "card_reward",
          "rewards": "rewards", "map": "map", "event": "event", "rest_site": "rest", "shop": "shop",
          "fake_merchant": "shop", "treasure": "treasure", "game_over": "end", "menu": "menu"}
LEAVE_OF = {"rewards": "rewards", "shop": "shop", "fake_merchant": "shop", "rest_site": "rest",
            "treasure": "treasure", "event": "event"}
SELECT_SCREENS = ("hand_select", "card_select", "bundle_select", "relic_select")
OUT_OF_COMBAT_DISCARD = ("rewards", "shop", "map", "event", "rest_site", "treasure")

END_TURN_FIRST_WAIT_S = 6.0      # sts2_live.Session.settle_end_turn
TREASURE_FIRST_CLICK_S = 4.0     # sts2_live.Session.settle (treasure re-click)


def _items(block, key):
    return [x for x in (block or {}).get(key) or [] if isinstance(x, dict)] if isinstance(block, dict) else []


def reward_key(item: dict) -> tuple:
    t = norm_reward_type(item.get("type"))
    if t == "gold":
        return t, item.get("gold_amount", item.get("amount"))
    if t == "potion":
        return t, item.get("potion_id") or item.get("id")
    if t == "relic":
        return t, item.get("relic_id") or item.get("id")
    if t == "special_card":
        return t, item.get("card_id") or item.get("id")
    return t, None


def shop_key(item: dict) -> tuple:
    cat = norm_shop_category(item.get("category"))
    return cat, item.get("card_id") or item.get("relic_id") or item.get("potion_id")


def view_of(state: dict, terminal: str | None = None) -> View:
    st = state.get("state_type")
    screen = SCREEN.get(st, "other")
    run = state.get("run") or {}
    p = state.get("player") or {}
    v = View(screen=screen, act=run.get("act"), floor=run.get("floor"), hp=p.get("hp"), max_hp=p.get("max_hp"),
             gold=p.get("gold"))
    if "potions" in p:
        v.potions = tuple(sorted((x.get("slot"), x.get("id")) for x in _items(p, "potions") if x.get("id")))
    if "relics" in p:
        v.relics = tuple(sorted(r.get("id") for r in _items(p, "relics")))
    if st in COMBAT_TYPES:
        battle = state.get("battle") or {}
        v.energy, v.block = p.get("energy"), p.get("block")
        v.hand = tuple((c.get("id"), norm_upgrade(c.get("is_upgraded"))) for c in _items(p, "hand"))
        v.enemies = tuple(sorted((e.get("combat_id"), e.get("id"), e.get("hp"), e.get("block", 0))
                                 for e in _items(battle, "enemies")
                                 if e.get("is_alive", True) and int(e.get("hp") or 0) > 0))
    if screen == "end":
        v.terminal = terminal or ("defeat" if int(p.get("hp") or 0) <= 0 else "victory")
        return v
    d = v.detail
    if st == "map":
        d["map_options"] = tuple(sorted((o.get("col"), o.get("row"), o.get("type"))
                                        for o in _items(state.get("map"), "next_options")))
    elif st == "event":
        ev = state.get("event") or {}
        d["event"] = ev.get("event_id")
        if not ev.get("in_dialogue"):
            d["event_options_n"] = sum(1 for o in _items(ev, "options") if not o.get("is_proceed"))
    elif st == "card_reward":
        d["card_reward"] = tuple((c.get("id"), norm_upgrade(c.get("is_upgraded")))
                                 for c in _items(state.get("card_reward"), "cards"))
    elif st == "rewards":
        d["rewards"] = tuple(sorted((reward_key(i) for i in _items(state.get("rewards"), "items")), key=repr))
    elif st in ("shop", "fake_merchant"):
        outer = state.get(st) or {}
        block = outer.get("shop") if st == "fake_merchant" and isinstance(outer.get("shop"), dict) else outer
        d["shop"] = tuple(sorted(((*shop_key(i), i.get("price")) for i in _items(block, "items")
                                  if i.get("is_stocked")), key=repr))
    elif st == "rest_site":
        d["rest"] = tuple(o.get("id") for o in _items(state.get("rest_site"), "options") if o.get("is_enabled"))
    elif st == "treasure":
        relics = _items(state.get("treasure"), "relics")
        if len(relics) == 1:
            d["treasure_relic"] = relics[0].get("id")
    elif st in SELECT_SCREENS:
        block = state.get(st) or {}
        cards = _items(block, "cards") or _items(block, "relics")
        picked = block.get("selected_count") or len(block.get("selected_cards") or [])
        if cards and not picked and st != "bundle_select":
            d["prompt_options"] = tuple(sorted((c.get("id"), norm_upgrade(c.get("is_upgraded"))) for c in cards))
    return v


def _is_selected(state: dict, card: dict) -> bool:
    """A card of a selection screen that is already selected (``is_selected`` / the grid's ``selected_indices``)."""
    if card.get("is_selected") is True:
        return True
    block = state.get(state.get("state_type")) or {}
    sel = block.get("selected_indices") if isinstance(block, dict) else None
    return isinstance(sel, list) and card.get("index") is not None and card.get("index") in sel


def acts_of(state: dict) -> list[Act]:
    """One Act per derive_legal_actions candidate (Act.native = the MCP wire body)."""
    st = state.get("state_type")
    out: list[Act] = []
    for a in derive_legal_actions(state):
        name = a.get("action")
        cand = a.get("candidate") or {}
        tc = a.get("target_candidate") or {}
        wire = {k: v for k, v in a.items() if k not in ("candidate", "target_candidate")}
        uid = tc.get("combat_id")
        if uid is not None:
            wire["target"] = str(uid)
        tl = f" -> u{uid} {tc.get('id')}" if uid is not None else ""
        up = norm_upgrade(cand.get("is_upgraded"))
        cid = cand.get("id")
        if name == "play_card":
            out.append(Act("play", (cid, up, uid), cand.get("index"), wire, f"play {cid}{'+' if up else ''}{tl}"))
        elif name == "end_turn":
            out.append(Act("end_turn", (), None, wire, "end turn"))
        elif name == "use_potion":
            out.append(Act("potion", (cid, cand.get("slot"), uid), cand.get("slot"), wire,
                           f"potion {cand.get('slot')} {cid}{tl}"))
        elif name == "discard_potion":
            out.append(Act("discard_potion", (cid, cand.get("slot")), cand.get("slot"), wire,
                           f"discard potion {cand.get('slot')} {cid}"))
        elif name == "choose_map_node":
            out.append(Act("travel", (cand.get("col"), cand.get("row")), cand.get("index"), wire,
                           f"travel ({cand.get('col')},{cand.get('row')}) {cand.get('type')}"))
        elif name == "claim_reward":
            k = reward_key(cand)
            out.append(Act("claim_reward", k, cand.get("index"), wire, f"claim {k[0]} {k[1] if k[1] is not None else ''}"
                           .strip()))
        elif name == "proceed":
            out.append(Act("leave", (LEAVE_OF.get(st, st),), None, wire, f"leave {LEAVE_OF.get(st, st)}"))
        elif name == "select_card_reward":
            out.append(Act("pick_card", (cid, up), cand.get("index"), wire, f"take card {cid}{'+' if up else ''}"))
        elif name == "skip_card_reward":
            out.append(Act("card_alt", ("skip",), None, wire, "card reward: skip"))
        elif name == "select_card_reward_alternative":
            alt = norm_alt(cand.get("id"))
            out.append(Act("card_alt", (alt,), cand.get("choice_index"), wire, f"card reward: {alt}"))
        elif name == "shop_purchase":
            k = shop_key(cand)
            out.append(Act("buy", k, cand.get("index"), wire, f"buy {k[0]} {k[1] or ''} ({cand.get('price')}g)"
                           .replace("  ", " ")))
        elif name == "choose_rest_option":
            out.append(Act("rest", (cid,), cand.get("index"), wire, f"rest: {cid}"))
        elif name == "open_chest":
            out.append(Act("open_chest", (), None, wire, "open chest"))
        elif name == "claim_treasure_relic":
            out.append(Act("take_relic", (cid,), cand.get("index"), wire, f"take relic {cid}"))
        elif name == "choose_event_option":
            i = cand.get("index")
            title = cand.get("title") or ""
            out.append(Act("event_option", (i,), i, wire, f"event option {i} {title}".strip()))
            if cand.get("is_proceed"):
                out.append(Act("leave", ("event",), i, wire, f"event: proceed (option {i})"))
        elif name == "advance_dialogue":
            out.append(Act("advance", (), None, wire, "advance dialogue"))
        elif name in ("combat_select_card", "select_card"):
            if _is_selected(state, cand):
                # a card already selected: selecting it again deselects it. The sim has no deselect (its picks are
                # monotone), so no twin pick may pair with it (a k-pick's later picks pair among the unselected cards)
                continue
            out.append(Act("select", (cid, up), cand.get("index"), wire, f"select {cid}{'+' if up else ''}"))
        elif name == "select_relic":
            out.append(Act("select", (cid, 0), cand.get("index"), wire, f"select relic {cid}"))
        elif name == "select_bundle":
            out.append(Act("select", ("bundle",), cand.get("index"), wire, f"select bundle {cand.get('index')}"))
        elif name in ("combat_confirm_selection", "confirm_selection", "confirm_bundle_selection"):
            out.append(Act("confirm", (), None, wire, "confirm selection"))
        elif name in ("cancel_selection", "skip_card_selection", "cancel_bundle_selection", "skip_relic_selection"):
            out.append(Act("cancel", (), None, wire, name.replace("_", " ")))
        else:
            out.append(Act("other", (name,), None, wire, name))
    # docs/sts2-live-protocol.md: "非战斗画面（奖励、商店、地图、事件、休息、宝箱）可直接 discard_potion"; derive_legal_actions
    # lists it only when the MCP state carries can_discard_out_of_combat, which current builds do not.
    if st in OUT_OF_COMBAT_DISCARD and not any(a.kind == "discard_potion" for a in out):
        for x in _items(state.get("player"), "potions"):
            if x.get("id") and x.get("slot") is not None:
                out.append(Act("discard_potion", (x.get("id"), x.get("slot")), x.get("slot"),
                               {"action": "discard_potion", "slot": x.get("slot")},
                               f"discard potion {x.get('slot')} {x.get('id')}"))
    return out


def find_current_saves(base: Path) -> list[Path]:
    """Every current_run.save under a game user-data root, newest first. Covers Steam profiles
    (steam/<id>/[modded/]profileN/saves) and the non-Steam layout headless workers use
    (default/<n>/[modded/]profileN/saves)."""
    base = Path(base)
    found = []
    for pattern in ("*/*/profile*/saves/current_run.save", "*/*/modded/profile*/saves/current_run.save"):
        found.extend(p for p in base.glob(pattern) if p.is_file())
    return sorted(set(found), key=lambda p: p.stat().st_mtime, reverse=True)


class McpGame(Game):
    """The real game on one STS2_MCP port."""

    name = "mcp"
    supports_saves = True

    def __init__(self, port: int, *, save_root: str | Path | None = None, save_path: str | Path | None = None,
                 settle_timeout: float = 60.0, log=print):
        self.port = int(port)
        self.save_root = Path(save_root).expanduser() if save_root else \
            Path.home() / "Library" / "Application Support" / "SlayTheSpire2"
        self.save_path = Path(save_path).expanduser() if save_path else None
        self.settle_timeout = settle_timeout
        self.log = log
        self.last_state: dict | None = None
        try:
            self.raw_get()
        except Exception as e:  # noqa: BLE001
            raise GameError(f"no STS2_MCP answer on port {self.port}: {e}") from e
        self.has_eval = self.eval_get() is not None

    # ---------------------------------------------------------------------------------------- HTTP

    def raw_get(self) -> dict:
        try:
            return executor.request(self.port)
        except SystemExit as e:
            raise GameError(str(e)) from None

    def eval_get(self, query: str = "?include_state=false", timeout: float = 30.0) -> dict | None:
        url = f"http://127.0.0.1:{self.port}/api/v1/eval{query}"
        try:
            with urllib.request.urlopen(url, timeout=timeout) as r:
                body = json.loads(r.read())
        except (urllib.error.URLError, OSError, ValueError):
            return None
        return body if isinstance(body, dict) and body.get("schema") == "sts2-eval-snapshot" else None

    def eval_post(self, body: dict, timeout: float = 200.0) -> dict:
        req = urllib.request.Request(f"http://127.0.0.1:{self.port}/api/v1/eval", data=json.dumps(body).encode(),
                                     method="POST", headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read())

    # ---------------------------------------------------------------------------------------- run control

    def start_run(self, character: str, ascension: int, seed: str, settings: dict | None = None) -> dict:
        """Seeded standard run through the eval API (headless workers / patched builds only)."""
        if not self.has_eval:
            raise GameError("this MCP build has no /api/v1/eval: start the run in the game yourself")
        self.leave_game_over()
        body = {"action": "start_run", "character": character, "ascension": int(ascension), "seed": seed,
                "mode": "standard", "timeout_ms": 120000, "abandon_existing": True}
        if settings:
            body["settings"] = settings
        reply = self.eval_post(body)
        rb = reply.get("readback") or {}
        if reply.get("status") != "ok" or not reply.get("verified") or \
                (str(rb.get("character", "")).upper(), rb.get("ascension"), rb.get("seed")) != \
                (character.upper(), int(ascension), seed):
            raise GameError(f"start_run not verified: {json.dumps(reply, ensure_ascii=False)[:600]}")
        return reply

    def leave_game_over(self, timeout: float = 90.0) -> None:
        """Click through a finished run's game-over screen to the main menu (as scripts/farm/run_farm.py does)."""
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            s = self.raw_get()
            if s.get("state_type") != "game_over":
                return
            try:
                executor.request(self.port, {"action": "menu_select", "option": "main_menu"})
            except SystemExit:
                try:
                    executor.request(self.port, {"action": "proceed"})
                except SystemExit:
                    pass
            time.sleep(2.0)
        raise GameError("stuck on the game-over screen")

    def run_info(self) -> dict:
        info = {}
        snap = self.eval_get() if self.has_eval else None
        if snap and (snap.get("run") or {}).get("in_progress"):
            r = snap["run"]
            info = {"seed": r.get("seed"), "character": r.get("character"), "ascension": r.get("ascension"),
                    "game_mode": r.get("game_mode"), "act": r.get("act"), "floor": r.get("floor"), "source": "eval"}
        path = self.latest_save()
        if path is not None:
            try:
                s = load_run_save(path)
                info.setdefault("seed", s.seed)
                info.setdefault("ascension", s.ascension)
                info.setdefault("game_mode", s.game_mode)
                if s.players:
                    info.setdefault("character", s.players[0].character_id.split(".")[-1])
                info["save"] = {"path": str(path), "floors": s.floors, "entry_point": _entry(s)}
                info.setdefault("source", "save")
            except RunSaveError as e:
                info["save_error"] = str(e)
        return info

    def result(self) -> dict:
        if not self.has_eval:
            return {}
        try:
            reply = self.eval_post({"action": "run_result"}, timeout=30)
        except Exception as e:  # noqa: BLE001
            return {"error": str(e)}
        return reply.get("run_result") or {}

    def latest_save(self) -> Path | None:
        if self.save_path is not None:
            return self.save_path if self.save_path.is_file() else None
        saves = find_current_saves(self.save_root)
        return saves[0] if saves else None

    # ---------------------------------------------------------------------------------------- lockstep

    def observe(self) -> Observation:
        t0 = time.perf_counter()
        try:
            fresh = executor.request(self.port)
            cached = self.last_state
            if cached is not None and executor.actionable(fresh) and \
                    executor.decision_digest(fresh) == executor.decision_digest(cached):
                # execute() already waited for this action's commitment and a quiet actionable frame
                # (settled_state); one fresh GET that shows the same decision is enough.
                state, ok = fresh, True
            else:
                state, ok = executor.wait_settled(self.port, timeout=self.settle_timeout, initial=fresh)
        except SystemExit as e:
            raise GameError(f"observe: {e}") from None
        self.last_state = state
        terminal = None
        if state.get("state_type") == "game_over" and self.has_eval:
            rr = self.result()
            if isinstance(rr, dict) and rr.get("status") != "in_progress" and "victory" in rr:
                terminal = "victory" if rr.get("victory") else "defeat"
        return Observation(raw=state, view=view_of(state, terminal), acts=acts_of(state), settled=ok,
                           ms=(time.perf_counter() - t0) * 1000, info={"state_type": state.get("state_type")})

    def execute(self, act: Act) -> ExecResult:
        wire = act.native
        pre = self.last_state if self.last_state is not None else self.raw_get()
        t0 = time.perf_counter()
        notes: list[str] = []
        try:
            executor.request(self.port, wire)
        except SystemExit as e:
            return ExecResult(False, wire, f"rejected: {e}", (time.perf_counter() - t0) * 1000)
        try:
            post = self._settle(pre, wire, notes)
        except SystemExit as e:
            self.last_state = None
            return ExecResult(False, wire, f"unsettled: {e}", (time.perf_counter() - t0) * 1000, notes)
        self.last_state = post
        return ExecResult(True, wire, None, (time.perf_counter() - t0) * 1000, notes)

    def _settle(self, pre: dict, wire: dict, notes: list[str]) -> dict:
        name = wire.get("action")
        if name == "end_turn":
            try:
                return executor.settled_state(self.port, pre, wire, timeout=END_TURN_FIRST_WAIT_S)
            except SystemExit:
                cur = executor.request(self.port)
                unchanged = executor.decision_digest(cur) == executor.decision_digest(pre)
                play = (cur.get("battle") or {}).get("is_play_phase")
                if executor._action_committed(pre, cur, wire) or not (unchanged and play):
                    return executor.settled_state(self.port, pre, wire)
                notes.append("end_turn_retry")
                executor.request(self.port, wire)
                return executor.settled_state(self.port, pre, wire)
        if name == "claim_treasure_relic":
            try:
                return executor.settled_state(self.port, pre, wire, timeout=TREASURE_FIRST_CLICK_S)
            except SystemExit:
                cur = executor.request(self.port)
                if executor._action_committed(pre, cur, wire):
                    raise
                notes.append("treasure_reclick")
                executor.request(self.port, wire)
                return executor.settled_state(self.port, pre, wire)
        return executor.settled_state(self.port, pre, wire)


def _entry(save) -> str:
    try:
        return save.entry_point
    except RunSaveError as e:
        return f"unsupported: {e}"
