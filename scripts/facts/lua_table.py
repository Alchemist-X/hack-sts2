"""Minimal parser for Lua table literals used by wiki.gg data modules.

Supports nested tables, ["key"]=, key=, positional values, single/double quoted
and [[long]] strings, numbers, true/false/nil, and -- comments. It is not a Lua
interpreter: it parses the first top-level table literal in a module.
"""

from __future__ import annotations

import re
from typing import Any


class LuaParseError(ValueError):
    pass


class _Parser:
    def __init__(self, text: str) -> None:
        self.s = text
        self.i = 0

    def ws(self) -> None:
        s = self.s
        while self.i < len(s):
            c = s[self.i]
            if c in " \t\r\n,;":
                self.i += 1
            elif s.startswith("--[[", self.i):
                end = s.find("]]", self.i)
                self.i = len(s) if end < 0 else end + 2
            elif s.startswith("--", self.i):
                end = s.find("\n", self.i)
                self.i = len(s) if end < 0 else end + 1
            else:
                break

    def string(self) -> str:
        s = self.s
        q = s[self.i]
        if q == "[":
            m = re.match(r"\[(=*)\[", s[self.i:])
            if not m:
                raise LuaParseError("bad long string")
            close = "]" + m.group(1) + "]"
            start = self.i + len(m.group(0))
            end = s.find(close, start)
            self.i = end + len(close)
            return s[start:end]
        self.i += 1
        out = []
        while self.i < len(s):
            c = s[self.i]
            if c == "\\":
                nxt = s[self.i + 1]
                out.append({"n": "\n", "t": "\t", "\\": "\\", '"': '"', "'": "'"}.get(nxt, nxt))
                self.i += 2
                continue
            if c == q:
                self.i += 1
                return "".join(out)
            out.append(c)
            self.i += 1
        raise LuaParseError("unterminated string")

    def value(self) -> Any:
        self.ws()
        s = self.s
        c = s[self.i]
        if c == "{":
            return self.table()
        if c in "\"'" or (c == "[" and re.match(r"\[=*\[", s[self.i:])):
            return self.string()
        m = re.match(r"-?\d+(?:\.\d+)?", s[self.i:])
        if m:
            self.i += len(m.group(0))
            txt = m.group(0)
            return float(txt) if "." in txt else int(txt)
        for lit, val in (("true", True), ("false", False), ("nil", None)):
            if s.startswith(lit, self.i):
                self.i += len(lit)
                return val
        # Unsupported expression (function call, identifier): capture raw token.
        m = re.match(r"[A-Za-z_][\w\.]*(\([^)]*\))?", s[self.i:])
        if m:
            self.i += len(m.group(0))
            return {"__expr__": m.group(0)}
        raise LuaParseError(f"unexpected {s[self.i:self.i+30]!r}")

    def table(self) -> Any:
        assert self.s[self.i] == "{"
        self.i += 1
        keyed: dict[Any, Any] = {}
        positional: list[Any] = []
        while True:
            self.ws()
            if self.s[self.i] == "}":
                self.i += 1
                break
            if self.s[self.i] == "[" and not re.match(r"\[=*\[", self.s[self.i:]):
                self.i += 1
                key = self.value()
                self.ws()
                if self.s[self.i] != "]":
                    raise LuaParseError("expected ]")
                self.i += 1
                self.ws()
                if self.s[self.i] != "=":
                    raise LuaParseError("expected =")
                self.i += 1
                keyed[key] = self.value()
                continue
            m = re.match(r"([A-Za-z_]\w*)\s*=(?!=)", self.s[self.i:])
            if m:
                self.i += len(m.group(0))
                keyed[m.group(1)] = self.value()
                continue
            positional.append(self.value())
        if keyed and positional:
            for n, v in enumerate(positional, 1):
                keyed[n] = v
            return keyed
        return keyed if keyed else positional


def parse_module(text: str) -> Any:
    """Parse the first top-level table literal (after 'return' or '=')."""
    m = re.search(r"(?:^|\n)\s*(?:return|local\s+\w+\s*=)\s*\{", text)
    if not m:
        raise LuaParseError("no table literal found")
    p = _Parser(text)
    p.i = m.end() - 1
    return p.value()
