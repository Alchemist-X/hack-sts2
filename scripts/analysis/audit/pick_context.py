#!/usr/bin/env python3
"""Does the archetype already in the deck change the pick? P(take a family card | one offered) by family count in deck.
usage: python3 -I pick_context.py <decisions.jsonl>"""
import collections as C, json, sys
b = lambda s: s.split("#")[0].rstrip("!*").rstrip("+")
FAM = {"poison": {"ACCELERANT", "BOUNCING_FLASK", "BUBBLE_BUBBLE", "CORROSIVE_WAVE", "DEADLY_POISON", "ENVENOM", "HAZE",
                  "MIRAGE", "NOXIOUS_FUMES", "OUTBREAK", "POISONED_STAB", "SNAKEBITE"},
       "shiv": {"ACCURACY", "BLADE_DANCE", "BLADE_OF_INK", "BLADE_SYMPHONY", "CLOAK_AND_DAGGER", "FAN_OF_KNIVES", "FINISHER",
                "HIDDEN_DAGGERS", "INFINITE_BLADES", "KNIFE_TRAP", "LEADING_STRIKE", "PHANTOM_BLADES", "STORM_OF_STEEL", "UP_MY_SLEEVE"}}
r = C.defaultdict(lambda: [0, 0])
for l in open(sys.argv[1]):
    d = json.loads(l)
    tc = [b(c[9:]) for c in d["cands"] if c.startswith("TakeCard:")]
    ch = d["chosen"]
    if d["phase"] != "Rewards" or not tc or not (ch.startswith("TakeCard:") or ch == "ProceedRewards"):
        continue
    for f, S in FAM.items():
        if not set(tc) & S:
            continue
        n = sum(b(c) in S for c in d["deck"])
        k = (f, "0" if n == 0 else ("1-2" if n <= 2 else "3+"))
        r[k][1] += 1
        r[k][0] += ch.startswith("TakeCard:") and b(ch[9:]) in S
for k in sorted(r):
    print(k, f"{r[k][0]}/{r[k][1]} = {100*r[k][0]/r[k][1]:.1f}%")
