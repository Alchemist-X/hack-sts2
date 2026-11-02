"""Token normalisation and reply parsing, mirroring Sts2Sim.Server ActionTokens.Normalize / TryResolve."""

from __future__ import annotations

import re

_SPACES = re.compile(r"\s+")
_TWO_NUMBERS = re.compile(r"^(play|potion) (\d+) (\d+)$")
_PREFIX = re.compile(r"^\s*(?:token|action|answer)\s*[:=]\s*", re.IGNORECASE)
EXIT_TOKENS = ("proceed", "leave")


def normalize(raw: str | None) -> str:
    """Same rules as the server: lower case, '->' spaced, whitespace collapsed, 'end turn' = 'end',
    'play 0 1' = 'play 0 -> 1'."""
    s = (raw or "").strip().lower()
    s = s.replace("->", " -> ")
    s = _SPACES.sub(" ", s).strip()
    if s in ("end turn", "end_turn", "endturn"):
        s = "end"
    m = _TWO_NUMBERS.match(s)
    if m:
        s = f"{m.group(1)} {m.group(2)} -> {m.group(3)}"
    return s


def resolve(candidate: str | None, legal: list[str]) -> str | None:
    """Return the legal token (verbatim, as listed by the server) that `candidate` names, or None."""
    if candidate is None:
        return None
    token = normalize(candidate)
    if not token:
        return None
    by_norm = {normalize(t): t for t in legal}
    if token in by_norm:
        return by_norm[token]
    alias = None
    if token == "take":
        alias = "take 0"
    elif token in ("next", "proceed", "leave", "continue"):
        alias = next((t for t in legal if normalize(t) in EXIT_TOKENS), None)
    if alias is not None and normalize(alias) in by_norm:
        return by_norm[normalize(alias)]
    return None


def first_line_token(reply: str | None) -> str | None:
    """Extract the candidate token from a model reply: the first non-empty line, with an optional
    'TOKEN:' prefix, markdown emphasis/backticks and surrounding quotes stripped."""
    if not reply:
        return None
    for line in reply.splitlines():
        s = line.strip()
        if not s:
            continue
        if s.startswith("```"):
            # a fenced block: the token is the first non-fence line inside it
            continue
        s = s.strip("*_`\"' ")
        s = _PREFIX.sub("", s)
        s = s.strip("*_`\"' ").rstrip(".")
        return s or None
    return None


def _clean_line(line: str) -> str:
    s = line.strip().strip("*_`\"' ")
    s = _PREFIX.sub("", s)
    return s.strip("*_`\"' ").rstrip(".")


def any_line_token(reply: str | None, legal: list[str]) -> tuple[str, int] | None:
    """Fallback when the first line is not a legal token: the LAST line of the reply that resolves
    to a legal token (models that reason first put the answer last). Returns (legal token as listed,
    0-based line index) or None. Fence lines are skipped."""
    if not reply:
        return None
    found = None
    for i, line in enumerate(reply.splitlines()):
        if not line.strip() or line.strip().startswith("```"):
            continue
        token = resolve(_clean_line(line), legal)
        if token is not None:
            found = (token, i)
    return found


def illegal_message(candidate: str | None, legal: list[str]) -> str:
    """The server's 400 wording, so a re-ask reads the same as a rejected step."""
    if not legal:
        return f"'{candidate}' is not legal: the game has no legal actions (it is over)."
    if candidate is None:
        return "your reply had no token on its first line; pick one of legal_actions."
    return (f"'{candidate}' is not a legal token here (normalised '{normalize(candidate)}'); "
            "pick one of legal_actions.")
