"""Information-parity check of the compact observation mode against a live server (`sts2eval parity`).

Replays recorded games (their `history_tokens`) or plays seeded random games, renders every observation
the way the runner does in --obs-mode compact (CompactView, /piles on the runner's schedule) and checks
`compact.parity_problems` on a seeded sample of observations, plus run-context stability: whenever the
inputs of the run context (act, relics, potions, deck nodes) are the same as on the previous step, the
blocks must be byte-identical.
"""

from __future__ import annotations

import json
import random
from pathlib import Path

from .compact import CompactView, parity_problems
from .simclient import SimClient, SimIllegal


def context_inputs(obs: dict, view: CompactView) -> str:
    p = obs.get("player") or {}
    relics = [{k: r.get(k) for k in ("id", "name", "text")} for r in p.get("relics") or []]
    return json.dumps([obs.get("act"), p.get("character"), relics, p.get("potions"), p.get("deck"),
                       view.deck_nodes], sort_keys=True)


class ParityStats:
    def __init__(self, samples: int, seed: int = 0):
        self.samples = samples
        self.rng = random.Random(seed)
        self.records: list[tuple[str, dict, tuple, str]] = []  # (server text, obs, ctx, step)
        self.steps = 0
        self.server_chars = 0
        self.step_chars = 0
        self.unstable = 0
        self.context_changes = 0
        self.games = 0

    def observe(self, view: CompactView, obs: dict, prev: dict) -> None:
        ctx, step, first = view.render(obs)
        inputs = context_inputs(obs, view)
        if prev.get("inputs") == inputs and prev.get("ctx") is not None:
            if any(a is not b or a != b for a, b in zip(prev["ctx"], ctx)):
                self.unstable += 1
        if first is not None:
            self.context_changes += 1
        prev["inputs"], prev["ctx"] = inputs, ctx
        self.steps += 1
        self.server_chars += len(obs.get("text") or "")
        self.step_chars += len(step)
        self.records.append((obs.get("text") or "", obs, ctx, step))

    def result(self) -> dict:
        idx = list(range(len(self.records)))
        if self.samples and self.samples < len(idx):
            idx = sorted(self.rng.sample(idx, self.samples))
        failures = []
        for i in idx:
            text, obs, ctx, step = self.records[i]
            probs = parity_problems(text, ctx, step, obs)
            if probs:
                failures.append({"index": i, "phase": obs.get("phase"), "floor": obs.get("floor"), "problems": probs[:10]})
        return {"games": self.games, "observations": self.steps, "checked": len(idx), "failures": len(failures),
                "failure_examples": failures[:5], "unstable_contexts": self.unstable,
                "context_changes": self.context_changes,
                "server_text_chars_mean": round(self.server_chars / max(1, self.steps), 1),
                "compact_step_chars_mean": round(self.step_chars / max(1, self.steps), 1)}


async def _drive(client: SimClient, stats: ParityStats, created: dict, ascension: int, next_token) -> None:
    gid, obs = created["id"], created["observation"]
    view = CompactView(ascension)
    prev: dict = {}
    stats.games += 1
    try:
        n = 0
        while not obs.get("terminal"):
            if view.needs_piles(obs):
                view.set_piles(obs, await client.piles(gid))
            stats.observe(view, obs, prev)
            token = next_token(obs, n)
            if token is None:
                break
            try:
                obs = (await client.step(gid, token))["observation"]
            except SimIllegal:
                break  # the recorded history diverged from this server build: stop that game
            n += 1
    finally:
        await client.delete(gid)


async def replay_run_dir(client: SimClient, run_dir: Path, stats: ParityStats, keys: list[str] | None = None,
                         games: int = 3) -> None:
    """Replay `games` recorded games of a run directory (or the named keys), longest first."""
    summaries = []
    for p in sorted(Path(run_dir, "games").glob("*.summary.json")):
        with open(p, encoding="utf-8") as f:
            s = json.load(f)
        if s.get("history_tokens") and (not keys or s.get("key") in keys):
            summaries.append(s)
    if not keys:
        summaries.sort(key=lambda s: -len(s["history_tokens"]))
        summaries = summaries[:games]
    for s in summaries:
        created = await client.new_run(s["seed"], s["character"], s["ascension"], s["content"],
                                       reveal_moves=bool(s.get("reveal_moves")))
        tokens = s["history_tokens"]
        await _drive(client, stats, created, s["ascension"], lambda obs, n, t=tokens: t[n] if n < len(t) else None)


def biased_token(rng: random.Random, legal: list[str]) -> str:
    """Random play that reaches deeper floors: prefers plays over `end`, taking over skipping/leaving."""
    plays = [t for t in legal if t.startswith(("play", "card ", "reward", "pick", "confirm", "event", "rest", "travel",
                                              "open", "take", "buy"))]
    if plays and rng.random() < 0.85:
        return rng.choice(plays)
    return rng.choice(legal)


async def play_random(client: SimClient, stats: ParityStats, games: list[tuple[str, str]], ascension: int = 0,
                      content: str = "ported_only", step_cap: int = 400, seed: int = 0) -> None:
    for i, (game_seed, character) in enumerate(games):
        rng = random.Random(seed * 1000 + i)
        created = await client.new_run(game_seed, character, ascension, content, reveal_moves=False)

        def nxt(obs, n, rng=rng):
            legal = [a["token"] for a in obs.get("legal_actions") or []]
            return biased_token(rng, legal) if legal and n < step_cap else None

        await _drive(client, stats, created, ascension, nxt)
