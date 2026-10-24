#!/usr/bin/env python3
"""Fetch STS2 facts from the Slay the Spire Wiki (slaythespire.wiki.gg).

Uses only the MediaWiki API: Cargo tables (action=cargoquery), raw Lua data
modules (action=query&prop=revisions) and batched page wikitext for event /
ancient / boss / elite pages. Respects robots.txt (generic UA allowed, ~1 req/s,
results cached). Output: artifacts/facts-cache/v<version>/wiki/*.json

Usage: python3 scripts/facts/fetch_wiki.py --version 0.107.1 [--refresh]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from common import Http, cache_dir, read_json, today, write_json  # noqa: E402
from lua_table import parse_module  # noqa: E402

API = "https://slaythespire.wiki.gg/api.php"
PAGE_URL = "https://slaythespire.wiki.gg/wiki/"

CARGO_TABLES = {
    "Cards": "Link,Name,Color,OverrideColor,Type,Rarity,Cost,CostPlus,StarCost,Description,NoUpgrade,MultiplayerOnly,Tags",
    "Relics": "Link,Name,Rarity,ExclusiveTo,AncientSource,EventSource,Cost,Description,Requirements,Tags",
    "Potions": "Link,Name,Rarity,ExclusiveTo,Description",
    "Enemies": "Link,Name,HP,AscHP,Powers,Debut,Type",
    "Intents": "Link,Enemy,Name,Images,Effect",
    "Encounters": "Name,Enemies,Act,Type",
    "Powers": "Link,Name,Description,ObtainableBy,Stacks,Type,BetweenTurns,Cap",
    "Keywords": "Link,Name,Description",
}
LUA_MODULES = [
    "Module:Events/StS2 data",
    "Module:Ancients/data",
    "Module:Enemies/StS2 data/Overgrowth",
    "Module:Enemies/StS2 data/Underdocks",
    "Module:Enemies/StS2 data/Hive",
    "Module:Enemies/StS2 data/Glory",
    "Module:Enemies/StS2 data/Elites",
    "Module:Enemies/StS2 data/Bosses",
    "Module:Enemies/StS2 data/Events",
    "Module:Ascension/StS2 data",
]


def cargo_all(http: Http, table: str, fields: str) -> list[dict]:
    rows: list[dict] = []
    offset = 0
    while True:
        data = http.get_json(API, {
            "action": "cargoquery", "format": "json", "tables": table, "fields": fields,
            "where": "Game='2'", "limit": 500, "offset": offset,
        })
        if "error" in data:
            raise RuntimeError(f"cargo {table}: {data['error']}")
        batch = [r["title"] for r in data.get("cargoquery", [])]
        rows.extend(batch)
        if len(batch) < 500:
            return rows
        offset += 500


def page_contents(http: Http, titles: list[str]) -> dict[str, dict]:
    out: dict[str, dict] = {}
    for i in range(0, len(titles), 50):
        chunk = titles[i:i + 50]
        data = http.get_json(API, {
            "action": "query", "format": "json", "formatversion": 2, "redirects": 1,
            "prop": "revisions", "rvprop": "content|ids|timestamp", "rvslots": "main",
            "titles": "|".join(chunk),
        })
        redirects = {r["to"]: r["from"] for r in data["query"].get("redirects", [])}
        normalized = {n["to"]: n["from"] for n in data["query"].get("normalized", [])}
        for page in data["query"]["pages"]:
            if page.get("missing") or page.get("invalid") or "revisions" not in page:
                continue
            rev = page["revisions"][0]
            title = page["title"]
            requested = redirects.get(title, title)
            requested = normalized.get(requested, requested)
            out[requested] = {
                "title": title,
                "revid": rev["revid"],
                "rev_timestamp": rev["timestamp"],
                "content": rev["slots"]["main"]["content"],
                "url": PAGE_URL + title.replace(" ", "_"),
            }
    return out


def section(text: str, name: str) -> str:
    m = re.search(r"^(=+)\s*" + re.escape(name) + r"\s*\1\s*$", text, re.M | re.I)
    if not m:
        return ""
    level = len(m.group(1))
    rest = text[m.end():]
    end = re.search(r"^={1,%d}[^=].*?={1,%d}\s*$" % (level, level), rest, re.M)
    return rest[: end.start()] if end else rest


def parse_event_page(text: str) -> dict:
    options = []
    for line in section(text, "Options").splitlines():
        m = re.match(r"\s*\*?\s*\{\{Frame\|([^}]*)\}\}\s*(.*)", line)
        if m:
            options.append({"title": m.group(1).strip(), "text_wiki": m.group(2).strip()})
    bullets = lambda sec: [b.strip()[1:].strip() for b in section(text, sec).splitlines() if b.strip().startswith("*")]
    return {
        "options": options,
        "mechanics": bullets("Mechanics"),
        "conditions": bullets("Conditions"),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.107.1")
    ap.add_argument("--refresh", action="store_true")
    ap.add_argument("--interval", type=float, default=1.0, help="seconds between requests")
    args = ap.parse_args()
    out = cache_dir(args.version) / "wiki"
    http = Http(args.interval)
    fetched = today()

    for table, fields in CARGO_TABLES.items():
        path = out / f"cargo_{table}.json"
        if path.exists() and not args.refresh:
            continue
        rows = cargo_all(http, table, fields)
        write_json(path, {"source": f"{API}?action=cargoquery&tables={table}&where=Game='2'", "fetched": fetched, "rows": rows})
        print(f"cargo {table}: {len(rows)}")

    path = out / "lua_modules.json"
    if args.refresh or not path.exists():
        pages = page_contents(http, LUA_MODULES)
        modules = {}
        for name, page in pages.items():
            modules[name] = {"url": page["url"], "revid": page["revid"], "rev_timestamp": page["rev_timestamp"],
                             "data": parse_module(page["content"])}
        write_json(path, {"fetched": fetched, "modules": modules})
        print(f"lua modules: {len(modules)}")
    modules = read_json(path)["modules"]

    path = out / "pages.json"
    if args.refresh or not path.exists():
        titles = []
        for name in modules["Module:Events/StS2 data"]["data"]:
            titles.append(f"Slay the Spire 2:{name}")
        for name in modules["Module:Ancients/data"]["data"]:
            titles.append(f"Slay the Spire 2:{name}")
        enemies = read_json(out / "cargo_Enemies.json")["rows"]
        for row in enemies:
            if row.get("Type") in ("Boss", "Elite") and row.get("Link"):
                titles.append(row["Link"].replace("_", " ").split("#")[0])
        titles = sorted(set(titles))
        pages = page_contents(http, titles)
        for page in pages.values():
            page["parsed"] = parse_event_page(page["content"])
        write_json(path, {"fetched": fetched, "pages": pages})
        print(f"pages: {len(pages)} / {len(titles)}")


if __name__ == "__main__":
    main()
