#!/usr/bin/env python3
"""Discard picks split by Ethereal vs harmful-in-hand curses/statuses (stdlib).
usage: python3 -I discard_ethereal.py <decisions.jsonl>"""
import collections as C, json, sys
SITES = {"d73a63f5", "b9f97c4c", "2f54bd40", "7370bfce", "66cc3de6", "0fb8cdd7", "f9858a16"}
ETH = {"ASCENDERS_BANE", "CLUMSY", "DAZED"}            # exhaust at end of turn if kept in hand
HARM = {"BECKON", "INFECTION"}                         # lose HP at end of turn if kept in hand
b = lambda s: s.split("#")[0].rstrip("!*").rstrip("+")
r = C.Counter()
for l in open(sys.argv[1]):
    d = json.loads(l)
    p = d.get("prompt")
    if not p or p["site"] not in SITES or d["kind"] != "PromptSelect":
        continue
    av = [b(o) for o in p["options"] if "#" not in o]
    ch = b(d["chosen"][7:])
    if len(av) < 2:
        continue
    for name, S in (("ethereal", ETH), ("harmful", HARM)):
        if set(av) & S:
            r[name + "_n"] += 1
            r[name + "_chosen"] += ch in S
            r[name + "_uniform"] += sum(a in S for a in av) / len(av)
    if set(av) & HARM and set(av) & ETH:
        r["both_n"] += 1
        r["both_chose_harm"] += ch in HARM
        r["both_chose_eth"] += ch in ETH
print({k: round(v, 1) for k, v in r.items()})
