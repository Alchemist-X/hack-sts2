"""Sly (奇巧) usage audit over the per-decision dump (stdlib only)."""
import json, sys, collections
dec, games = sys.argv[1], sys.argv[2]
SLY_STATIC = {"REFLEX","TACTICIAN","RICOCHET","FLICK_FLACK","ABRASIVE","UNTOUCHABLE"}
JUNK = {"BURN","DAZED","WOUND","SLIMED","VOID","ASCENDERS_BANE","CLUMSY","DECAY","DOUBT","INJURY","NORMALITY","PAIN","PARASITE","REGRET","SHAME","WRITHE","PRIDE","CURSE_OF_THE_BELL","NECRONOMICURSE","SPLINTER","SPLINTERS"}
def base(c): return c.replace("*","").replace("!","").split("+")[0].split(">")[0]
C = collections.Counter()
outlet_plays = collections.Counter(); outlet_with_sly_in_hand = collections.Counter()
discard_prompts = 0; dp_with_sly_opt = 0; dp_pick_sly = 0; dp_pick_junk = 0; dp_pick_other = 0
pick_targets = collections.Counter(); sly_in_hand_steps = 0; combat_steps = 0
sly_played_normally = collections.Counter()   # Sly card played from hand (not via discard)
handtrick = 0; masterplanner = 0
prev_enabler_by_game = {}
for line in open(dec):
    r = json.loads(line)
    if r.get("phase") == "Combat" and r.get("combat"):
        combat_steps += 1
        hand = r["combat"].get("hand") or []
        if any("*" in h for h in hand): sly_in_hand_steps += 1
    k = r.get("kind")
    if k == "PlayCard":
        cb = base(r.get("card") or "")
        if r.get("discard_enabler"):
            outlet_plays[cb] += 1
            hand = (r.get("combat") or {}).get("hand") or []
            if any("*" in h for h in hand): outlet_with_sly_in_hand[cb] += 1
        if r.get("discard_payoff") == "HAND_TRICK": handtrick += 1
        if r.get("discard_payoff") == "MASTER_PLANNER": masterplanner += 1
        if cb in SLY_STATIC: sly_played_normally[cb] += 1
    if r.get("dk") == "Prompt" and k == "PromptSelect":
        pr = r.get("prompt") or {}
        if pr.get("kind") == "HandSelect" and pr.get("after") and base(pr["after"].replace("P:","")) in {"ACROBATICS","CALCULATED_GAMBLE","DAGGER_THROW","HIDDEN_DAGGERS","PREPARED","SCRAPE","SHADOW_STEP","STORM_OF_STEEL","SURVIVOR","TOOLS_OF_THE_TRADE","GAMBLING_CHIP","UNLOAD","REFLEX","TACTICIAN"} or (pr.get("kind")=="HandSelect" and (pr.get("after") or "").split(":")[0] in ("ACROBATICS","SURVIVOR","DAGGER_THROW","PREPARED","CALCULATED_GAMBLE","SCRAPE","STORM_OF_STEEL","TOOLS_OF_THE_TRADE")):
            discard_prompts += 1
            opts = pr.get("options") or []
            has_sly = any("*" in o and base(o) in SLY_STATIC or ("*" in o) for o in opts)
            if has_sly: dp_with_sly_opt += 1
            chosen = r.get("chosen") or ""
            cb = base(chosen.replace("Select:",""))
            pick_targets[cb] += 1
            if r.get("sly") or "*" in chosen: dp_pick_sly += 1
            elif cb in JUNK: dp_pick_junk += 1
            else: dp_pick_other += 1
# decks
deck_games = collections.Counter(); deck_cards = collections.Counter(); relic_games = collections.Counter(); n=0
for line in open(games):
    g = json.loads(line); n += 1
    d = [base(c) for c in g.get("final_deck") or []]
    s = [c for c in d if c in SLY_STATIC]
    if s: deck_games["any_sly"] += 1
    for c in set(s): deck_games[c] += 1
    for c in s: deck_cards[c] += 1
    for rl in g.get("final_relics") or []:
        if rl in ("TINGSHA","TOUGH_BANDAGES","GAMBLING_CHIP"): relic_games[rl] += 1
print(f"games {n}; games with a static-Sly card in the final deck: {deck_games['any_sly']}; by card: {dict((k,v) for k,v in deck_games.items() if k!='any_sly')}; copies: {dict(deck_cards)}")
print(f"relics: {dict(relic_games)}")
print(f"combat decisions {combat_steps}; decisions with a Sly card in hand: {sly_in_hand_steps}")
print(f"discard-outlet plays: {dict(outlet_plays)} (total {sum(outlet_plays.values())}); of which a Sly card was in hand: {dict(outlet_with_sly_in_hand)}")
print(f"discard prompts (HandSelect after an outlet): {discard_prompts}; with a Sly option: {dp_with_sly_opt}; picked Sly: {dp_pick_sly}; picked junk (status/curse): {dp_pick_junk}; picked other: {dp_pick_other}")
print(f"most discarded: {pick_targets.most_common(12)}")
print(f"Sly cards played from hand normally: {dict(sly_played_normally)}; Hand Trick plays {handtrick}; Master Planner plays {masterplanner}")
