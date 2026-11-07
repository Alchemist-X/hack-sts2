#!/usr/bin/env python3
"""Card-reward pick rate (taken / offered) for named cards (stdlib).
usage: python3 -I pick_rates.py <decisions.jsonl> CARD [CARD ...]"""
import collections as C, json, sys
b = lambda s: s.split("#")[0].rstrip("!*").rstrip("+")
off, tak = C.Counter(), C.Counter()
for l in open(sys.argv[1]):
    d = json.loads(l)
    tc = [c for c in d["cands"] if c.startswith("TakeCard:")]
    ch = d["chosen"]
    if d["phase"] != "Rewards" or not tc or not (ch.startswith("TakeCard:") or ch == "ProceedRewards"):
        continue
    for c in tc:
        off[b(c[9:])] += 1
    if ch.startswith("TakeCard:"):
        tak[b(ch[9:])] += 1
for c in sys.argv[2:]:
    print(f"{c}\t{tak[c]}/{off[c]}")
