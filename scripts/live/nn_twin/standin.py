"""Dry-run stand-in for the real game: a run on the simulator's HTTP play server (``sim/Sts2Sim.Server``, sim/PLAY.md).

The play server is a separate process with its own copy of the run; the runner sees it only through JSON
observations and token actions, the same way it sees the real game only through STS2_MCP. Its candidates are
decoded from the observation and the ``legal_actions`` tokens into semantic Acts, independently of the twin's
candidate table, so a dry run exercises the whole lockstep (state diffing, semantic matching, execution, resync
bookkeeping) without the real game.
"""

from __future__ import annotations

import json
import re
import time
import urllib.error
import urllib.request

from .game import ExecResult, Game, GameError, Observation
from .semantics import Act, View, norm_alt, norm_reward_type, norm_shop_category, norm_upgrade

PHASE_SCREEN = {"Map": "map", "Combat": "combat", "Rewards": "rewards", "CardReward": "card_reward",
                "Shop": "shop", "RestSite": "rest", "Treasure": "treasure", "Event": "event", "Prompt": "prompt",
                "Victory": "end", "Defeat": "end"}


def _uid(value) -> int | None:
    if value is None:
        return None
    s = str(value)
    return int(s[1:]) if s.startswith("u") and s[1:].isdigit() else (int(s) if s.isdigit() else None)


def screen_of(o: dict) -> str:
    if o.get("prompt"):
        return "prompt"
    return PHASE_SCREEN.get(o.get("phase"), "other")


def reward_item(r: dict):
    t = norm_reward_type(r.get("type"))
    detail = str(r.get("detail") or "")
    if t == "gold":
        m = re.match(r"\s*(\d+)", detail)
        return t, int(m.group(1)) if m else None
    if t in ("potion", "relic", "special_card"):
        return t, detail.split()[0] if detail else None
    return t, None


def view_of(o: dict) -> View:
    screen = screen_of(o)
    p = o.get("player") or {}
    v = View(screen=screen, act=o.get("act"), floor=o.get("floor"), hp=p.get("hp"), max_hp=p.get("max_hp"),
             gold=p.get("gold"))
    v.potions = tuple(sorted((x.get("slot", i), x.get("id")) for i, x in enumerate(p.get("potions") or []) if x))
    v.relics = tuple(sorted(r.get("id") for r in p.get("relics") or []))
    combat = o.get("combat")
    if combat:
        v.energy, v.block = p.get("energy"), p.get("block")
        v.hand = tuple((c.get("id"), norm_upgrade(c.get("upgraded"))) for c in combat.get("hand") or [])
        v.enemies = tuple(sorted((_uid(e.get("uid")), e.get("id"), e.get("hp"), e.get("block"))
                                 for e in combat.get("enemies") or []))
    if o.get("terminal"):
        v.screen = "end"
        v.terminal = "victory" if o.get("result") == "win" else "defeat"
        return v
    d = v.detail
    if screen == "map":
        d["map_options"] = tuple(sorted((x["col"], x["row"], x["type"]) for x in (o.get("map") or {}).get("options")
                                        or []))
    elif screen == "event":
        d["event"] = (o.get("event") or {}).get("id")
    elif screen == "card_reward":
        d["card_reward"] = tuple((c.get("id"), norm_upgrade(c.get("upgraded")))
                                 for c in (o.get("card_reward") or {}).get("cards") or [])
    elif screen == "rewards":
        d["rewards"] = tuple(sorted((reward_item(r) for r in o.get("rewards") or []), key=repr))
    elif screen == "shop":
        shop = o.get("shop") or {}
        items = [(norm_shop_category(e.get("category")), e.get("id"), e.get("price"))
                 for e in shop.get("entries") or [] if not e.get("sold")]
        if shop.get("removal_price") is not None:
            items.append(("card_removal", None, shop.get("removal_price")))
        d["shop"] = tuple(sorted(items, key=repr))
    elif screen == "rest":
        d["rest"] = tuple(x.get("id") for x in (o.get("rest") or {}).get("options") or [] if x.get("enabled"))
    elif screen == "treasure":
        t = o.get("treasure") or {}
        if t.get("chest_opened") and not t.get("relic_claimed") and not t.get("skipped") and t.get("relic"):
            d["treasure_relic"] = t.get("relic")
    elif screen == "prompt":
        pr = o.get("prompt") or {}
        if not pr.get("picked") and pr.get("kind") != "Bundle":
            d["prompt_options"] = tuple(sorted((x.get("id"), norm_upgrade(x.get("upgraded")))
                                               for x in pr.get("options") or [] if x.get("id") is not None))
    return v


def acts_of(o: dict) -> list[Act]:
    """One Act per legal token (Act.native = the token)."""
    screen = screen_of(o)
    p = o.get("player") or {}
    combat = o.get("combat") or {}
    hand = {c.get("index"): c for c in combat.get("hand") or []}
    enemies = {e.get("index"): e for e in combat.get("enemies") or []}
    potions = {x.get("slot", i): x for i, x in enumerate(p.get("potions") or []) if x}
    out: list[Act] = []
    for la in o.get("legal_actions") or []:
        tok = la["token"]
        parts = tok.split()
        head = parts[0]
        nums = [int(x) for x in parts[1:] if x.lstrip("-").isdigit()]
        target = None
        if "->" in parts and "ally" not in parts:
            e = enemies.get(nums[-1])
            target = _uid(e.get("uid")) if e else None
        if head == "play":
            c = hand.get(nums[0]) or {}
            out.append(Act("play", (c.get("id"), norm_upgrade(c.get("upgraded")), target), nums[0], tok, tok))
        elif head == "end":
            out.append(Act("end_turn", (), None, tok, tok))
        elif head == "potion":
            pid = (potions.get(nums[0]) or {}).get("id")
            out.append(Act("potion", (pid, nums[0], target), nums[0], tok, tok))
        elif head == "discard":
            pid = (potions.get(nums[0]) or {}).get("id")
            out.append(Act("discard_potion", (pid, nums[0]), nums[0], tok, tok))
        elif head == "travel":
            x = ((o.get("map") or {}).get("options") or [])[nums[0]]
            out.append(Act("travel", (x["col"], x["row"]), nums[0], tok, tok))
        elif head == "reward":
            r = next((r for r in o.get("rewards") or [] if r.get("index") == nums[0]), {})
            out.append(Act("claim_reward", reward_item(r), nums[0], tok, tok))
        elif head == "proceed":
            out.append(Act("leave", ("rewards",), None, tok, tok))
        elif head == "card":
            c = ((o.get("card_reward") or {}).get("cards") or [])[nums[0]]
            out.append(Act("pick_card", (c.get("id"), norm_upgrade(c.get("upgraded"))), nums[0], tok, tok))
        elif head == "skip" and screen == "treasure":
            out.append(Act("leave", ("treasure",), None, tok, tok))
        elif head in ("skip", "reroll"):
            out.append(Act("card_alt", (head,), None, tok, tok))
        elif head == "alt":
            alts = (o.get("card_reward") or {}).get("alternatives") or []
            alt = next((a for a in alts if a.get("index") == nums[0]), {})
            out.append(Act("card_alt", (norm_alt(alt.get("id")),), nums[0], tok, tok))
        elif head == "buy":
            e = next((e for e in (o.get("shop") or {}).get("entries") or [] if e.get("index") == nums[0]), {})
            out.append(Act("buy", (norm_shop_category(e.get("category")), e.get("id")), nums[0], tok, tok))
        elif head == "remove":
            out.append(Act("buy", ("card_removal", None), None, tok, tok))
        elif head == "leave":
            out.append(Act("leave", ({"shop": "shop", "rest": "rest", "event": "event"}.get(screen, screen),),
                           None, tok, tok))
        elif head == "rest":
            x = next((x for x in (o.get("rest") or {}).get("options") or [] if x.get("index") == nums[0]), {})
            out.append(Act("rest", (x.get("id"),), nums[0], tok, tok))
        elif head == "open":
            out.append(Act("open_chest", (), None, tok, tok))
        elif head == "take":
            out.append(Act("take_relic", ((o.get("treasure") or {}).get("relic"),), None, tok, tok))
        elif head == "event":
            out.append(Act("event_option", (nums[0],), nums[0], tok, tok))
        elif head == "pick":
            pr = o.get("prompt") or {}
            x = next((x for x in pr.get("options") or [] if x.get("index") == nums[0]), {})
            if pr.get("kind") == "Bundle":
                key = ("bundle",)          # a bundle's id is its card list; both other sides use ("bundle",) + rank
            elif x.get("id") is None:
                key = ("index",)
            else:
                key = (x.get("id"), norm_upgrade(x.get("upgraded")))
            out.append(Act("select", key, nums[0], tok, tok))
        elif head == "confirm":
            out.append(Act("confirm", (), None, tok, tok))
        elif head == "cancel":
            out.append(Act("cancel", (), None, tok, tok))
        else:
            out.append(Act("other", (tok,), None, tok, tok))
    return out


class PlayServerGame(Game):
    """A run on the play server at `url` (``POST /runs``), driven by tokens."""

    name = "standin"

    def __init__(self, url: str, *, seed: str, character: str, ascension: int, content: str = "all",
                 timeout: float = 60.0):
        self.url = url.rstrip("/")
        self.timeout = timeout
        status, d = self._call("GET", "/health")
        if status != 200 or not d.get("ok"):
            raise GameError(f"play server at {self.url} is not healthy: {status} {d}")
        self.server_version = d.get("version")
        status, d = self._call("POST", "/runs", {"seed": seed, "character": character, "ascension": int(ascension),
                                                 "content": content, "reveal_moves": False})
        if status != 200:
            raise GameError(f"POST /runs failed: {status} {d}")
        self.id = d["id"]
        self.config = {"seed": seed, "character": character, "ascension": int(ascension), "content": content}
        self._obs = d["observation"]

    def _call(self, method: str, path: str, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.url + path, method=method, data=data,
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as r:
                return r.status, json.loads(r.read())
        except urllib.error.HTTPError as e:
            try:
                return e.code, json.loads(e.read())
            except ValueError:
                return e.code, {"error": str(e)}

    def observe(self) -> Observation:
        t0 = time.perf_counter()
        status, d = self._call("GET", f"/games/{self.id}")
        if status != 200:
            raise GameError(f"GET /games/{self.id}: {status} {d}")
        o = self._obs = d["observation"]
        return Observation(raw=o, view=view_of(o), acts=acts_of(o), ms=(time.perf_counter() - t0) * 1000,
                           info={"phase": o.get("phase")})

    def execute(self, act: Act) -> ExecResult:
        t0 = time.perf_counter()
        status, d = self._call("POST", f"/games/{self.id}/step", {"token": act.native})
        ms = (time.perf_counter() - t0) * 1000
        if status != 200:
            return ExecResult(False, act.native, f"{status}: {d.get('error')}", ms)
        self._obs = d["observation"]
        return ExecResult(True, act.native, None, ms, notes=[x for x in (d.get("log") or [])[1:4]])

    def run_info(self) -> dict:
        return dict(self.config)

    def history(self) -> list[str]:
        status, d = self._call("GET", f"/games/{self.id}/history")
        return list(d.get("tokens") or []) if status == 200 else []

    def result(self) -> dict:
        o = self._obs or {}
        return {"terminal": bool(o.get("terminal")), "result": o.get("result"), "act": o.get("act"),
                "floor": o.get("floor")}

    def close(self) -> None:
        try:
            self._call("DELETE", f"/games/{self.id}")
        except Exception:  # noqa: BLE001
            pass
