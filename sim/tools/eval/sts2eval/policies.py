"""Policies: async choose(obs_text, legal_tokens, recent_history, feedback=None, context=None) -> (candidate, meta).

`obs_text` is the step text (the server's `text` in --obs-mode full, the compact step block otherwise);
`context` is None in full mode and the run-context blocks (sts2eval.compact) in compact mode.

`candidate` is the raw token string the policy proposes (validated by the runner) or None when the call
failed. `meta` always has: raw, stop_reason, usage, latency_ms, cost_usd, error (None | 'api_error' |
'refusal' | 'timeout'), plus model specific details.
"""

from __future__ import annotations

import asyncio
import random
import time

from . import prices as P
from .prompt import SYSTEM_PROMPT, build_user_message
from .tokens import first_line_token

MODELS = ("claude-sonnet-5-5", "claude-haiku-5-5", "claude-opus-5-5", "claude-fable-5-1")
EFFORTS = ("low", "medium", "high", "xhigh", "max")
# HTTP statuses that mean every later call will fail the same way: abort the run instead of
# silently playing it out with random fallbacks.
FATAL_STATUSES = (401, 402, 403, 404)


class BudgetExceeded(Exception):
    pass


class FatalApiError(Exception):
    pass


def empty_meta(**kw) -> dict:
    m = {"raw": None, "stop_reason": None, "usage": P.usage_dict(None), "latency_ms": 0,
         "cost_usd": 0.0, "error": None}
    m.update(kw)
    return m


class RandomPolicy:
    """Uniform over the legal tokens; the rng is seeded per game by the runner."""
    name = "random"
    model = None

    def __init__(self, rng: random.Random | None = None):
        self.rng = rng or random.Random(0)

    def for_game(self, seed: int) -> "RandomPolicy":
        return RandomPolicy(random.Random(seed))

    async def choose(self, obs_text: str, legal_tokens: list[str], recent_history: dict | None = None,
                     feedback: str | None = None, context: tuple[str, ...] | None = None):
        token = self.rng.choice(legal_tokens)
        return token, empty_meta(raw=token)


def thinking_param(model: str, thinking: str, effort: str | None) -> dict | None:
    """The `thinking` request field for --thinking adaptive|off, or a ValueError for an
    unsupported combination (claude-api skill, models.md / model-migration.md)."""
    if thinking == "adaptive":
        return None  # adaptive is the default on every 5.x model: omit the field
    if thinking != "off":
        raise ValueError(f"--thinking must be 'adaptive' or 'off', not {thinking!r}")
    if effort in ("xhigh", "max"):
        raise ValueError(f"--thinking off needs --effort high or lower (got {effort})")
    if model == "claude-sonnet-5-5":
        return {"type": "between_tools"}  # Sonnet 5.5's lowest setting; 'disabled' is a 400 there
    if model == "claude-haiku-5-5":
        return {"type": "disabled"}  # accepted at effort high or below; 'between_tools' is Sonnet 5.5 only
    raise ValueError(f"{model} cannot disable thinking (thinking is always on); use --effort low instead")


class RateLimiter:
    """At most `rps` request starts per second across all games."""

    def __init__(self, rps: float | None):
        self.interval = 1.0 / rps if rps else 0.0
        self._next = 0.0
        self._lock = asyncio.Lock()

    async def wait(self) -> None:
        if not self.interval:
            return
        async with self._lock:
            now = time.monotonic()
            delay = self._next - now
            self._next = max(now, self._next) + self.interval
        if delay > 0:
            await asyncio.sleep(delay)


class ClaudePolicy:
    """Stateless per step: cached static system prompt + one user message (observation and history)."""
    name = "claude"

    def __init__(self, client, model: str = "claude-sonnet-5-5", effort: str | None = None,
                 thinking: str = "adaptive", max_tokens: int = 1024, prices: dict | None = None,
                 tracker: P.CostTracker | None = None, limiter: RateLimiter | None = None,
                 system_prompt: str = SYSTEM_PROMPT):
        if model not in MODELS:
            raise ValueError(f"unknown model {model!r}; use one of {', '.join(MODELS)}")
        if effort is not None and effort not in EFFORTS:
            raise ValueError(f"--effort must be one of {EFFORTS}")
        self.client = client
        self.model = model
        self.effort = effort
        self.thinking_mode = thinking
        self.thinking = thinking_param(model, thinking, effort)
        self.max_tokens = int(max_tokens)
        self.prices = prices if prices is not None else P.load_prices()
        self.model_prices = P.model_prices(self.prices, model)
        self.tracker = tracker or P.CostTracker()
        self.limiter = limiter or RateLimiter(None)
        self.system = [{"type": "text", "text": system_prompt, "cache_control": {"type": "ephemeral"}}]

    def for_game(self, seed: int) -> "ClaudePolicy":
        return self  # stateless: one shared instance (shared client, tracker, limiter)

    def system_blocks(self, context: tuple[str, ...] | None = None) -> list[dict]:
        """The static system prompt, then (compact mode) one cached block per run-context block, in
        order of volatility, so a change re-writes only that block and the ones after it (at most 4
        cache breakpoints per request: static + 3)."""
        if not context:
            return self.system
        return self.system + [{"type": "text", "text": b, "cache_control": {"type": "ephemeral"}} for b in context]

    def request_kwargs(self, user_message: str, context: tuple[str, ...] | None = None) -> dict:
        kw = {"model": self.model, "max_tokens": self.max_tokens, "system": self.system_blocks(context),
              "messages": [{"role": "user", "content": user_message}]}
        if self.effort is not None:
            kw["output_config"] = {"effort": self.effort}
        if self.thinking is not None:
            kw["thinking"] = self.thinking
        return kw

    async def choose(self, obs_text: str, legal_tokens: list[str], recent_history: dict | None = None,
                     feedback: str | None = None, context: tuple[str, ...] | None = None):
        if self.tracker.exhausted():
            raise BudgetExceeded(f"spent ${self.tracker.spent_usd:.4f} >= --max-cost-usd {self.tracker.max_cost_usd}")
        user = build_user_message(obs_text, legal_tokens, recent_history, feedback)
        kw = self.request_kwargs(user, context)
        await self.limiter.wait()
        t0 = time.monotonic()
        try:
            resp = await self.client.messages.create(**kw)
        except Exception as e:  # noqa: BLE001 - every API failure is counted, never fatal to the game
            status = getattr(e, "status_code", None)
            if status in FATAL_STATUSES:
                raise FatalApiError(f"{type(e).__name__} (HTTP {status}): {getattr(e, 'message', e)}") from None
            return None, empty_meta(error="api_error", latency_ms=int((time.monotonic() - t0) * 1000),
                                    exception=f"{type(e).__name__}: {str(e)[:500]}", status=status)
        latency = int((time.monotonic() - t0) * 1000)
        usage = P.usage_dict(getattr(resp, "usage", None))
        cost = P.cost_usd(usage, self.model_prices)
        self.tracker.add(usage, cost)
        text = "".join(getattr(b, "text", "") for b in (resp.content or []) if getattr(b, "type", None) == "text")
        meta = empty_meta(raw=text, stop_reason=resp.stop_reason, usage=usage, latency_ms=latency,
                          cost_usd=cost, request_id=getattr(resp, "_request_id", None),
                          served_model=getattr(resp, "model", None))
        if resp.stop_reason == "refusal":
            details = getattr(resp, "stop_details", None)
            meta["error"] = "refusal"
            meta["refusal_category"] = getattr(details, "category", None) if details is not None else None
            return None, meta
        if resp.stop_reason == "max_tokens" and "\n" not in text.strip():
            # cut off before the first line was finished (usually inside thinking): no usable token;
            # the runner re-asks like any other unparseable reply
            return None, meta
        # 'max_tokens' after a finished first line: the token is complete; the runner validates it.
        return first_line_token(text), meta
