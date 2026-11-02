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

    async def new_run(self, seed, character, ascension, content):
        return {"id": "r1", "observation": self._obs()}

    async def step(self, gid, token):
        legal = [a["token"] for a in self._obs()["legal_actions"]]
        if token not in legal:
            raise SimIllegal(400, {"ok": False, "error": "illegal", "legal_actions": self._obs()["legal_actions"]})
        self.tokens.append(token)
        return {"observation": self._obs(), "log": [f"{token}: ok"], "ok": True}

    async def history(self, gid):
        return {"id": gid, "tokens": list(self.tokens), "steps": len(self.tokens), "crashed": False}

    async def delete(self, gid):
        self.deleted = True
        return {"ok": True}
