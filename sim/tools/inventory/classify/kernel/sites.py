import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys
D = common.DECOMPILE_ROOT
K = common.work_dir("kernel")
rows = json.load(open(os.path.join(K, "prompts.json")))
def args_of(s, start):
    depth = 0; i = start
    while i < len(s):
        ch = s[i]
        if ch == "(": depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0: return s[start+1:i]
        i += 1
    return s[start+1:start+400]
def split_top(a):
    out = []; depth = 0; cur = ""
    for ch in a:
        if ch in "([{<": depth += 1
        if ch in ")]}>": depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip()); cur = ""
        else:
            cur += ch
    if cur.strip(): out.append(cur.strip())
    return out
IN_COMBAT_KINDS = ("Models.Cards", "Models.Potions", "Models.Powers", "Models.Relics", "Models.Monsters", "Models.Enchantments", "Models.Afflictions", "Models.Orbs")
sites = []
seen = set()
for r in rows:
    if "Mock" in r["kind"] or r["method"] in ("Reset", "PushSelector", "UseSelector"):
        continue
    if r["kind"] not in IN_COMBAT_KINDS:
        continue
    key = (r["kind"], r["file"], r["method"])
    if key in seen: continue
    seen.add(key)
    p = os.path.join(D, "MegaCrit.Sts2.Core." + r["kind"], r["file"] + ".cs")
    s = open(p, encoding="utf-8", errors="replace").read()
    m = re.search(r'CardSelectCmd\.' + r["method"] + r'\(', s)
    a = args_of(s, m.end() - 1)
    parts = split_top(a)
    named = {}
    pos = []
    for x in parts:
        mm = re.match(r'^(\w+):\s*(.*)$', x, re.S)
        if mm: named[mm.group(1)] = mm.group(2)
        else: pos.append(x)
    prefs = named.get("prefs")
    if prefs is None:
        for x in pos:
            if "CardSelectorPrefs" in x or x == "prefs": prefs = x
    if prefs == "prefs" or prefs is None:
        pm = re.search(r'new CardSelectorPrefs\(([^;]*?)\)(\s*\{[^}]*\})?', s)
        prefs = ("new CardSelectorPrefs(" + pm.group(1) + ")" + (pm.group(2) or "")) if pm else None
    minmax = None; flags = []
    if prefs:
        pm = re.search(r'new CardSelectorPrefs\((.*)\)', prefs, re.S)
        if pm:
            pp = split_top(pm.group(1))
            if len(pp) == 2: minmax = (pp[1], pp[1])
            elif len(pp) >= 3: minmax = (pp[1], pp[2])
        if "Cancelable = false" in prefs: flags.append("Cancelable=false")
        if "RequireManualConfirmation = true" in prefs: flags.append("RMC=true")
        if "PretendCardsCanBePlayed = true" in prefs: flags.append("PretendCardsCanBePlayed")
    pile = None
    pm = re.search(r'PileType\.(\w+)\.GetPile', a)
    if pm: pile = pm.group(1)
    if r["method"] in ("FromHand", "FromHandForDiscard", "FromHandForUpgrade"): pile = "Hand"
    if r["method"] in ("FromChooseACardScreen", "FromSimpleGrid", "FromSimpleGridForRewards"): pile = "generated"
    canskip = "canSkip: true" in a
    filt = None
    if r["method"] in ("FromHand", "FromHandForDiscard"):
        f = named.get("filter") or (pos[3] if len(pos) > 3 else None)
        filt = None if (f is None or f.strip() == "null") else re.sub(r'\s+', ' ', f)[:120]
    if r["method"] == "FromCombatPile":
        f = named.get("filter") or (pos[4] if len(pos) > 4 else None)
        filt = None if (f is None or f.strip() == "null") else re.sub(r'\s+', ' ', f)[:120]
    ctx = named.get("context") or (pos[0] if pos else None)
    site = r["site"]
    if site in ("AfterPlayerTurnStart", "BeforeHandDraw"):
        cat = "turn-setup deferral (SetupPlayerTurn ctx)"
    elif site == "AfterShuffle":
        cat = "caller ctx: inline in card play, turn-setup deferral when the shuffle is in the turn-start draw"
    elif ctx and "Blocking" in ctx:
        cat = "inline (BlockingPlayerChoiceContext)"
    elif site == "OnUse":
        cat = "inline (UsePotionAction GameActionPlayerChoiceContext)"
    elif site == "OnPlay":
        cat = "inline (PlayCardAction ctx; caller ctx when auto-played)"
    else:
        cat = "caller ctx"
    sites.append(dict(kind=r["kind"].replace("Models.", ""), file=r["file"], line=r["line"], site=site, method=r["method"],
                      pile=pile, min=minmax[0] if minmax else None, max=minmax[1] if minmax else None, can_skip=canskip,
                      flags=flags, filter=filt, context=(ctx or "")[:60], category=cat))
json.dump(sites, open(os.path.join(K, "sites.json"), "w"), indent=1)
for x in sites:
    print(f'{x["kind"]:9} {x["file"]:22} {x["site"]:22} {x["method"]:22} pile={x["pile"]} min={x["min"]} max={x["max"]} skip={x["can_skip"]} flags={x["flags"]} filt={(x["filter"] or "")[:50]} ctx={x["context"][:25]}')
print(len(sites))
