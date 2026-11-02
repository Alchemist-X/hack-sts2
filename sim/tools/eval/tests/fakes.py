"""Fake Anthropic client and fake play server for unit tests (no network)."""

from __future__ import annotations

from types import SimpleNamespace

from sts2eval.simclient import SimIllegal


def response(text: str, stop_reason: str = "end_turn", usage: dict | None = None, category=None):
    u = {"input_tokens": 100, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 1400,
         "output_tokens": 20}
    u.update(usage or {})
    content = [SimpleNamespace(type="thinking", thinking=""), SimpleNamespace(type="text", text=text)] if text else []
    return SimpleNamespace(content=content, stop_reason=stop_reason, usage=SimpleNamespace(**u),
                           model="claude-sonnet-5-5", _request_id="req_test",
                           stop_details=SimpleNamespace(category=category) if category else None)


class FakeApiError(Exception):
    def __init__(self, status_code: int, message: str = "boom"):
        super().__init__(message)
        self.status_code = status_code
        self.message = message


class FakeMessages:
    def __init__(self, script):
        self.script = list(script)
        self.calls: list[dict] = []

    async def create(self, **kw):
        self.calls.append(kw)
        item = self.script.pop(0) if self.script else response("end")
        if callable(item):
            item = item(kw)
        if isinstance(item, BaseException):
            raise item
        return item


class FakeAnthropic:
    def __init__(self, script):
        self.messages = FakeMessages(script)


class FakeSim:
    """A run of `length` steps; every step offers 'play 0 -> 0' and 'end'; terminal 'loss' at the end."""

    def __init__(self, length: int = 3):
        self.length = length
        self.tokens: list[str] = []
        self.deleted = False
        self.new_run_kwargs: dict | None = None

    def _obs(self):
        n = len(self.tokens)
        terminal = n >= self.length
        legal = [] if terminal else [{"token": "play 0 -> 0", "desc": "strike"}, {"token": "end", "desc": "end turn"}]
        text = "== r1 Combat | act 1 floor 2 | turn 1\n-- legal actions\n" + "\n".join(f" {a['token']}" for a in legal)
        return {"game_id": "r1", "kind": "run", "phase": "Defeat" if terminal else "Combat",
                "terminal": terminal, "result": "loss" if terminal else None, "act": 1, "floor": 2,
                "turn": None if terminal else 1 + n // 2, "player": {"hp": 0 if terminal else 50, "max_hp": 80},
                "combat": None if terminal else {"enemies": [{"index": 0, "id": "NIBBIT"}]},
                "map": None, "legal_actions": legal, "text": text}

    async def health(self):
        return {"ok": True, "version": "fake"}

    async def new_run(self, seed, character, ascension, content, **kw):
        self.new_run_kwargs = dict(kw)
        return {"id": "r1", "observation": self._obs()}

    async def step(self, gid, token):
        legal = [a["token"] for a in self._obs()["legal_actions"]]
        if token not in legal:
            raise SimIllegal(400, {"ok": False, "error": "illegal", "legal_actions": self._obs()["legal_actions"]})
        self.tokens.append(token)
        return {"observation": self._obs(), "log": [f"{token}: ok"], "ok": True}

    async def history(self, gid):
        return {"id": gid, "tokens": list(self.tokens), "steps": len(self.tokens), "crashed": False}

    async def piles(self, gid):
        self.piles_calls = getattr(self, "piles_calls", 0) + 1
        return {"game_id": gid, "in_combat": True, "deck": []}

    async def delete(self, gid):
        self.deleted = True
        return {"ok": True}


class FakeRewardsSim(FakeSim):
    """The rewards screen after a fight on floor 3, as the server plays it: `reward 0` opens the card
    reward (phase CardReward), `skip` returns to the rewards screen with the card reward still there,
    `card 0` takes the card, `proceed` leaves (here the run then ends: terminal 'loss')."""

    def __init__(self):
        super().__init__(0)
        self.screen = "rewards"
        self.card_taken = False

    def _obs(self):
        if self.screen == "done":
            return {"game_id": "r1", "kind": "run", "phase": "Defeat", "terminal": True, "result": "loss",
                    "act": 1, "floor": 3, "turn": None, "player": {"hp": 0, "max_hp": 80}, "combat": None,
                    "map": None, "legal_actions": [], "text": "== r1 Defeat | act 1 floor 3"}
        if self.screen == "card":
            phase, screen = "CardReward", "-- card reward (pick one or skip)\n (0) INFLAME Power Uncommon cost 1"
            legal = [{"token": "card 0", "desc": "add INFLAME to the deck"}, {"token": "skip", "desc": "skip the card reward"}]
        else:
            phase = "Rewards"
            screen = "-- rewards\n" + ("" if self.card_taken else " (0) Card: choose 1 of 3 cards\n")
            legal = ([] if self.card_taken else [{"token": "reward 0", "desc": "claim Card reward"}]) \
                + [{"token": "proceed", "desc": "leave the rewards screen (unclaimed rewards are lost)"}]
        text = (f"== r1 {phase} | act 1 floor 3 | IRONCLAD HP 50/80 gold 99\n{screen}\n-- legal actions\n"
                + "\n".join(f" {a['token']}  : {a['desc']}" for a in legal))
        return {"game_id": "r1", "kind": "run", "phase": phase, "terminal": False, "result": None, "act": 1,
                "floor": 3, "turn": None, "player": {"hp": 50, "max_hp": 80}, "combat": None, "map": None,
                "legal_actions": legal, "text": text}

    async def step(self, gid, token):
        legal = [a["token"] for a in self._obs()["legal_actions"]]
        if token not in legal:
            raise SimIllegal(400, {"ok": False, "error": "illegal", "legal_actions": self._obs()["legal_actions"]})
        self.tokens.append(token)
        before = self._obs()["phase"]
        if token == "reward 0":
            self.screen = "card"
        elif token == "skip":
            self.screen = "rewards"
        elif token == "card 0":
            self.screen, self.card_taken = "rewards", True
        elif token == "proceed":
            self.screen = "done"
        after = self._obs()["phase"]
        log = [f"{token}: ok"] + ([f"phase {before} -> {after}"] if before != after else [])
        return {"observation": self._obs(), "log": log, "ok": True}
