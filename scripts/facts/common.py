"""Shared helpers for the STS2 fact-database pipeline (scripts/facts/).

Everything fetched from the network or extracted from the local game package is
cached under ``artifacts/facts-cache/<version>/`` (git-ignored). Only the
normalized, derived facts written by ``build_facts.py`` are committed.
"""

from __future__ import annotations

import datetime as _dt
import gzip
import json
import re
import time
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_VERSION = "0.107.1"
USER_AGENT = (
    "hack-sts2-facts/1.0 (research fact DB for an STS2 agent; "
    "low-rate, cached; https://github.com/ - contact via repo)"
)


def today() -> str:
    return _dt.date.today().isoformat()


def cache_dir(version: str) -> Path:
    path = REPO_ROOT / "artifacts" / "facts-cache" / f"v{version}"
    path.mkdir(parents=True, exist_ok=True)
    return path


def facts_dir(version: str) -> Path:
    path = REPO_ROOT / "knowledge" / "facts" / f"v{version}"
    path.mkdir(parents=True, exist_ok=True)
    return path


class Http:
    """Polite HTTP client: fixed UA, minimum interval between requests, retries."""

    def __init__(self, min_interval: float = 1.0) -> None:
        self.min_interval = min_interval
        self._last = 0.0

    def get(self, url: str, params: dict[str, Any] | None = None, attempts: int = 4) -> bytes:
        if params:
            url = url + ("&" if "?" in url else "?") + urllib.parse.urlencode(params)
        last_error: Exception | None = None
        for attempt in range(attempts):
            wait = self.min_interval - (time.monotonic() - self._last)
            if wait > 0:
                time.sleep(wait)
            self._last = time.monotonic()
            request = urllib.request.Request(
                url,
                headers={"User-Agent": USER_AGENT, "Accept-Encoding": "gzip"},
            )
            try:
                with urllib.request.urlopen(request, timeout=60) as response:
                    body = response.read()
                    if response.headers.get("Content-Encoding") == "gzip":
                        body = gzip.decompress(body)
                    return body
            except Exception as error:  # noqa: BLE001 - retry any transport error
                last_error = error
                time.sleep(2 ** attempt * self.min_interval)
        raise RuntimeError(f"GET failed after {attempts} attempts: {url}: {last_error}")

    def get_json(self, url: str, params: dict[str, Any] | None = None) -> Any:
        return json.loads(self.get(url, params).decode("utf-8"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=1, sort_keys=False) + "\n", encoding="utf-8")


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


_ID_RE = re.compile(r"[^A-Z0-9]+")


def norm_id(name: str) -> str:
    """'Torch Head Amalgam' -> 'TORCH_HEAD_AMALGAM'; 'Slay_the_Spire_2:Bash' -> 'BASH'."""
    name = name.split(":", 1)[-1] if name.startswith("Slay_the_Spire_2:") or name.startswith("Slay the Spire 2:") else name
    name = name.replace("'", "").replace("\u2019", "").replace("+", "_PLUS")
    return _ID_RE.sub("_", name.upper()).strip("_")


def camel_to_id(name: str) -> str:
    """'TorchHeadAmalgam' -> 'TORCH_HEAD_AMALGAM'."""
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", name)
    s = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", "_", s)
    return s.upper()


_BB_RE = re.compile(r"\[/?[a-z_]+(?:=[^\]]*)?\]")
_IMG_RE = re.compile(r"\[([a-z]+)_energy_icon\.png\]|\[energy_icon\.png\]|\[star_icon\.png\]")


def strip_bbcode(text: str) -> str:
    """Game rich text -> plain normalized text."""
    if text is None:
        return ""
    text = _IMG_RE.sub("[E]", text)
    text = re.sub(r"(?<=\d)\[energy:\d+\]", "[E]", text)
    text = re.sub(r"(?<=\d)\[star:\d+\]", "[Star]", text)
    text = re.sub(r"\[energy:(\d+)\]", r"\1[E]", text)
    text = re.sub(r"\[star:(\d+)\]", r"\1[Star]", text)
    text = re.sub(r"\[img\][^\[]*\[/img\]", "", text)
    text = _BB_RE.sub("", text)
    text = text.replace("\n", " ")
    return re.sub(r"\s+", " ", text).strip()


_WIKI_LINK_RE = re.compile(r"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]")
_TEMPLATE_RE = re.compile(r"\{\{([^{}]*)\}\}")


def _template_text(match: re.Match[str]) -> str:
    parts = match.group(1).split("|")
    head = parts[0].strip()
    args = [p for p in parts[1:] if "=" not in p]
    if head in {"Asc2", "Asc"} and len(args) >= 2:
        return f"[A{args[0]}: {args[1]}]"
    if args:
        return args[-1] if head.lower().startswith("energy") is False else f"{args[-1]}[E]"
    return head


def strip_wiki(text: str) -> str:
    """Wiki HTML/wikitext -> plain text. Keeps (base;upgraded) markers."""
    if text is None:
        return ""
    text = re.sub(r"\[\[File:StS2_?(?:Energy|Icon_Energy)[^\]]*\]\]", "[E]", text)
    text = re.sub(r"\[\[File:StS2_?Star[^\]]*\]\]", "[Star]", text)
    text = re.sub(r"\[\[File:[^\]]*\]\]", "", text)
    for _ in range(4):
        new = _TEMPLATE_RE.sub(_template_text, text)
        if new == text:
            break
        text = new
    text = _WIKI_LINK_RE.sub(r"\1", text)
    text = re.sub(r"\[https?://\S+ ([^\]]+)\]", r"\1", text)
    text = re.sub(r"<br\s*/?>", " ", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = text.replace("&#39;", "'").replace("&quot;", '"').replace("&amp;", "&").replace("&nbsp;", " ")
    text = text.replace("'''", "").replace("''", "")
    text = re.sub(r"\s+", " ", text).strip()
    return collapse_icons(text)


def collapse_icons(text: str) -> str:
    """'[E][E]' -> '2[E]' (wiki repeats icon images)."""
    def rep(m: re.Match[str]) -> str:
        n = m.group(0).count(m.group(1))
        return f"{n}{m.group(1)}"
    text = re.sub(r"(?:<[^>]*>|\s)*(\[E\]|\[Star\])(?:(?:<[^>]*>)*\1)*", lambda m: " " + rep(m) if m.group(0)[:1].isspace() else rep(m), text)
    return re.sub(r"(?<=[\d)])1(\[E\]|\[Star\])", r"\1", text)


_NUM_RE = re.compile(r"(?<![A-Za-z])\d+(?:\.\d+)?")


def numbers(text: str) -> list[float]:
    return [float(x) for x in _NUM_RE.findall(text or "")]


def wiki_variant(text: str, upgraded: bool) -> str:
    """Resolve wiki '(8;10)' markers into the base or upgraded text."""

    def pick(match: re.Match[str]) -> str:
        a, b = match.group(1), match.group(2)
        return b if upgraded else a

    return re.sub(r"\(([^();]*);([^();]*)\)", pick, text)
