import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import collections
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from condense import NEW, ROOT, SLICE  # noqa: E402
import manual_ironclad  # noqa: E402
import manual_colorless  # noqa: E402
import manual_other  # noqa: E402

KIND = "cards-ironclad-colorless"
OUT_JSON = common.stage1_path("%s.classified.json" % KIND)

MANUAL = {}
for mod in (manual_ironclad, manual_colorless, manual_other):
    for k, v in mod.M.items():
        assert k not in MANUAL, k
        MANUAL[k] = v

VD = {
    "ABUNDANCE": ("new", "not in v0.107.1; added with the Dowsing quest"),
    "BLAZE": ("new", "not in v0.107.1"),
    "DOWSING": ("new", "not in v0.107.1 (quest from relic DowsingRod)"),
    "MIDNIGHT": ("new", "not in v0.107.1"),
    "OUTRAGE": ("new", "not in v0.107.1"),
    "THE_BALL": ("new", "not in v0.107.1"),
    "MOCK_TURN_END_IN_HAND_RECORDER_CARD": ("new", "test mock, not in v0.107.1"),
    "ASCENDERS_BANE": ("rule-change", "adds CanBeGeneratedInCombat => false; v0.111.0 keyword array unreadable (LdMemberToken), v0.107.1 = Eternal, Unplayable, Ethereal"),
    "BEACON_OF_HOPE": ("rule-change", "cost 1 -> 2"),
    "BLOODLETTING": ("rule-change", "rarity Common -> Uncommon"),
    "BRIGHTEST_FLAME": ("rule-change", "max HP loss 1 -> 2"),
    "COLOSSUS": ("rule-change", "block 5 -> 4"),
    "CRIMSON_MANTLE": ("rule-change", "CrimsonMantlePower 8 -> 7, upgrade +2 -> +3"),
    "CRUELTY": ("rule-change", "rarity Rare -> Uncommon"),
    "DEMON_FORM": ("rule-change", "PowerVar<StrengthPower> 2 -> 3"),
    "DOMINATE": ("rule-change", "rarity Uncommon -> Rare"),
    "EXPECT_A_FIGHT": ("rule-change", "redesigned: v0.107.1 cost 2, energy per Attack in hand + NoEnergyGainPower (upgrade cost -1); v0.111.0 cost 3, block 15 + 5 x Strength (upgrade +1 / +3)"),
    "FOLLY": ("decompile-gap", "v0.111.0 keyword array unreadable (LdMemberToken); every readable member identical to v0.107.1"),
    "FORGOTTEN_RITUAL": ("rule-change", "v0.107.1 gained energy only if a card of the owner was exhausted this turn; v0.111.0 gains it unconditionally"),
    "FUEL": ("rule-change", "v0.107.1 energy 1 + draw 1 (upgrade +1 card); v0.111.0 energy 1 only (upgrade +1 energy)"),
    "GIANT_ROCK": ("rule-change", "damage 16 -> 20"),
    "HOWL_FROM_BEYOND": ("rule-change", "damage 16 -> 18, upgrade +5 -> +6"),
    "HUDDLE_UP": ("cosmetic", "CardPileCmd.Draw -> DrawWithoutBlockingOnOtherPlayers (multiplayer-only card)"),
    "MAD_SCIENCE": ("rule-change", "Violence: N separate DamageCmd.Attack calls -> one attack WithHitCount(N); Chaos card via AddGeneratedCardToCombat instead of CardPileCmd.Add"),
    "MANGLE": ("rule-change", "damage 15 -> 20, upgrade +5 -> +6"),
    "MAUL": ("rule-change", "Increase 1 -> 2"),
    "MINION_SACRIFICE": ("rule-change", "block 8 -> 7"),
    "MOCK_CARD_MODEL": ("cosmetic", "test mock: IsMock and Pool => DeprivedCardPool added"),
    "MOCK_SKILL_CARD": ("cosmetic", "test mock: only state-machine numbering differs"),
    "PACTS_END": ("rule-change", "damage 17 -> 18"),
    "RAMPAGE": ("rule-change", "damage 9 -> 10, Increase upgrade +4 -> +5"),
    "RELAX": ("rule-change", "block 15 -> 16"),
    "REND": ("rule-change", "cost 2 -> 1, CalculationBase 15 -> 10, upgrade base +3 -> +2"),
    "SALVO": ("rule-change", "rarity Rare -> Uncommon"),
    "SETUP_STRIKE": ("rule-change", "SetupStrikePower 2 -> 3"),
    "SHIV": ("cosmetic", "CreateInHand gains optional creator (creator ?? owner); identical in single player"),
    "SOUL": ("cosmetic", "CreateInHand gains optional creator (creator ?? owner); identical in single player"),
    "SOVEREIGN_BLADE": ("cosmetic", "VFX node calls only"),
    "SPLASH": ("rule-change", "rarity Uncommon -> Rare"),
    "SPOILS_MAP": ("same", "identical after normalisation (OnQuestComplete present in both)"),
    "TANK": ("cosmetic", "adds description vars DamageIncrease 1.5 / DamageDecrease 0.5; TankPower keeps its own constants (Models.Powers/TankPower.cs:113-124)"),
    "TAUNT": ("rule-change", "rarity Uncommon -> Common, block 7 -> 6"),
    "WHISTLE": ("rule-change", "cost 3 -> 2"),
    "WITHER": ("cosmetic", "fake-upgrade title/portrait overrides added (UI only)"),
    "EVIL_EYE": ("same", "predicate e.Card.Owner == Owner rewritten as e.Actor == Owner.Creature; CardExhaustedEntry.Actor is card.Owner.Creature (Combat.History.Entries/CardExhaustedEntry.cs), so equivalent"),
    "OMNISLICE": ("same", "same effects; v0.111.0 threads cardPlay into CreatureCmd.Damage (API)"),
    "DUAL_WIELD": ("same", "same effects (CreateClone per copy in both)"),
}

LEVELS = [
    (4, r"^decompile gap"),
    (3, r"CardSelectCmd|ChooseCard|Monster-driven prompt|KnowledgeDemon"),
    (3, r"CombatHistory"),
    (3, r"CardFactory|UnlockState"),
    (3, r"Potions runtime|PotionFactory|PlayerCmd\.GainGold|PlayerCmd\.LoseGold|RunEffects|Player\.Gold|Osty|FromOsty|OstyDamageVar|Forge subsystem|SandpitPower"),
    (3, r"Run layer|SavedProperty|TinkerTime|PlayerCmd\.CompleteQuest|TransformTo<T> \(deck\)|Hook listener: deck card|Hook\.AfterCombatEnd|RemoveFromDeck|DeckVersion"),
    (2, r"Hook listener: card|^Hook\.(?!ModifyDamage \(direct)|restore hook parameter|CardCmd\.Upgrade|CardCmd\.Transform \(combat\)|CardCmd\.AutoPlay|AutoPlayFromDrawPile|AddGeneratedCardsToCombat|CreateClone|CombatState\.CreateCard|DynamicVar: Calculated|CalculatedDamageVar\)|AttackContext|GeneratePlayCount|AfterDowngraded|AfterCloned|per-instance card state|ITemporaryPower|GetEnchantedReplayCount|dynamic (TargetType|CardType)|GetResultLocationForCardPlay override|DrawWithoutBlocking"),
]
CATEGORY = [
    ("run", r"Run layer|SavedProperty|TinkerTime|PlayerCmd\.CompleteQuest|TransformTo<T> \(deck\)|Hook listener: deck card|Hook\.AfterCombatEnd|RemoveFromDeck|DeckVersion"),
    ("subsystem", r"Potions runtime|PotionFactory|PlayerCmd\.GainGold|PlayerCmd\.LoseGold|RunEffects|Player\.Gold|Osty|FromOsty|OstyDamageVar|Forge subsystem|SandpitPower|Monster-driven prompt|KnowledgeDemon"),
    ("cardgen", r"CardFactory|UnlockState"),
    ("prompt", r"CardSelectCmd|ChooseCard"),
    ("history", r"CombatHistory"),
]
BATCH_OF_CATEGORY = {"history": 4, "prompt": 5, "cardgen": 6, "subsystem": 7, "run": 8}

BATCHES = {
    0: "already ported in P0; re-verify against the expanded kernel",
    1: "tier 1 on the current sim: existing facades, every dependency already ported",
    2: "tier 1 logic, but applies or reads powers (or uses tokens) not yet ported: after those powers land",
    3: "tier 2: needs kernel facades/hooks missing from the sim (card hook listeners, CardCmd.Upgrade/Transform/AutoPlay, CreateClone/CreateCard, Calculated* vars, AttackContext, per-instance card state)",
    4: "tier 3, combat history: CombatHistory queries (CardExhausted, DamageReceived, CardPlayStarted/Finished)",
    5: "tier 3, player prompts: choice API (HandSelect, HandUpgrade, PileSelect, ChooseCard)",
    6: "tier 3, card generation: CardFactory + UnlockState + ordered pool data",
    7: "tier 3, other subsystems: potions, run gold, Osty, Forge, monster-driven prompt (KnowledgeDemon)",
    8: "run layer: deck-resident quest/curse hooks, saved properties, deck removal, event-driven card state",
    9: "multiplayer-only: unreachable in single-player pools; port last or never",
}
TIERS = {
    1: "pure numbers through facades the sim already has (DamageCmd.Attack incl. multi/all/random targets, CreatureCmd damage/block/heal/stun/max HP, PowerCmd.Apply/ModifyAmount, energy, CardPileCmd.Draw/Add/Exhaust/AddGeneratedCardToCombat, keywords, local cost modifiers, X cost, replay count, turn-end-in-hand effects, CardDef.IsPlayable); unported power dependencies do not raise the tier",
    2: "needs facades or hooks that exist in the game but not in the sim",
    3: "needs a player prompt or a new subsystem (combat history, card generation, potions, Osty, Forge, run-state effects, run layer)",
    4: "unreadable/decompile-broken, cross-cutting, or test-only mock",
}

POWER_APPLY_RE = re.compile(r"\b(?:PowerCmd\.Apply|GetPower|HasPower|GetPowerAmount|OfType|Remove)<(\w+Power)>")
CARD_REF_RE = re.compile(r"\b(?:CreateCard|TransformTo|OfType)<(\w+)>")


def slug(name):
    out = re.sub(r"([A-Za-z0-9])([A-Z])", r"\1_\2", name)
    out = re.sub(r"([A-Z])([A-Z][a-z])", r"\1_\2", out)
    return out.upper()


def strip_hover(text):
    out = []
    skip = 0
    for line in text.split("\n"):
        if "ExtraHoverTips" in line or "HoverTipFactory" in line or "IHoverTip" in line:
            continue
        out.append(line)
    return "\n".join(out)


def level_of(req):
    for lvl, pat in LEVELS:
        if re.search(pat, req):
            return lvl
    return 1


def categories_of(reqs):
    cats = set()
    for r in reqs:
        for name, pat in CATEGORY:
            if re.search(pat, r):
                cats.add(name)
                break
    return cats


def auto_kreq(e, hooks_info, man):
    reqs = []
    if man.get("no_auto_hooks"):
        return reqs
    for h in e["abstract_model_hooks"]:
        info = hooks_info.get(h, {})
        if info.get("scope") == "run":
            reqs.append("Run layer: %s on deck cards" % h)
            continue
        reqs.append("Hook listener: card in combat piles (%s)" % h)
        if not info.get("in_sim"):
            reqs.append("Hook.%s (dispatcher + virtual missing in sim)" % h)
    return reqs


def main():
    cards = json.load(open(common.inventory_path("cards.json")))
    ported = json.load(open(common.inventory_path("ported.json")))
    hooks = json.load(open(common.inventory_path("hooks.json")))
    vdiff = json.load(open(common.work_path("cards-ironclad-colorless", "vdiff.json")))
    awaits = json.load(open(common.work_path("cards-ironclad-colorless", "awaits.json")))
    ported_powers = {p["id"] for p in ported["entries"]["powers"]}
    ported_cards = {p["id"] for p in ported["entries"]["cards"]}
    hooks_info = {h["name"]: h for h in hooks["hooks"]}
    power_ids = set()
    pj = json.load(open(common.inventory_path("powers.json")))
    power_class_to_id = {p["class"]: p["id"] for p in pj["entries"]}
    card_class_to_id = {c["class"]: c["id"] for c in cards["entries"]}

    entries = []
    for e in cards["entries"]:
        if e["pool"] not in SLICE:
            continue
        cid = e["id"]
        man = MANUAL.get(cid)
        if man is None:
            raise SystemExit("no manual entry for " + cid)
        path = os.path.join(NEW, "MegaCrit.Sts2.Core." + e["file"])
        text = strip_hover(re.sub(r"//[^\n]*", "", open(path, encoding="utf-8-sig", errors="replace").read()))
        dep_powers = []
        for m in POWER_APPLY_RE.finditer(text):
            pid = power_class_to_id.get(m.group(1))
            if pid and pid not in dep_powers:
                dep_powers.append(pid)
        for p in man.get("deps_powers", []):
            if p not in dep_powers:
                dep_powers.append(p)
        dep_cards = []
        for m in CARD_REF_RE.finditer(text):
            cc = card_class_to_id.get(m.group(1))
            if cc and cc != cid and cc not in dep_cards:
                dep_cards.append(cc)
        for c in man.get("deps_cards", []):
            if c not in dep_cards:
                dep_cards.append(c)
        scope = man.get("scope", "game")
        reqs = []
        for r in auto_kreq(e, hooks_info, man) + [x for x in man.get("kreq", []) if not x.startswith("Hook listener: card in combat piles")]:
            if r not in reqs:
                reqs.append(r)
        if cid in ("ASCENDERS_BANE", "FOLLY"):
            reqs.insert(0, "decompile gap: CanonicalKeywords InitializeArray (LdMemberToken); re-decompile with reference assemblies or read IL")
        prompts = man.get("prompts", [])
        for p in prompts:
            p.setdefault("site", "OnChosen (monster prompt)" if "KnowledgeDemon" in p.get("when", "") else "OnPlay")
            if "KnowledgeDemon" not in p.get("when", ""):
                r = p["cmd"] + (" (%s)" % p["pile"] if p["cmd"].endswith("FromCombatPile") else "") + " [choice API]"
                if r not in reqs:
                    reqs.append(r)
        if e["needs_player_choice"] and not prompts:
            raise SystemExit("choice tokens but no prompts: " + cid)
        tier = max([1] + [level_of(r) for r in reqs])
        if "tier" in man:
            tier = man["tier"]
        unported_powers = [p for p in dep_powers if p not in ported_powers]
        unported_cards = [c for c in dep_cards if c not in ported_cards]
        cats = categories_of(reqs)
        if scope == "test_only":
            batch = None
        elif scope == "multiplayer_only":
            batch = 9
        elif cid in ported_cards:
            batch = 0
        elif scope == "deprecated":
            batch = 8
        elif cats:
            batch = max(BATCH_OF_CATEGORY[c] for c in cats)
        elif tier >= 2 and any(level_of(r) == 2 for r in reqs):
            batch = 3
        elif unported_powers or unported_cards:
            batch = 2
        else:
            batch = 1
        if "batch" in man:
            batch = man["batch"]
        rng = man.get("rng")
        if rng is None:
            rng = []
            for t in e["rng_usage"]:
                if t.startswith("RunRng."):
                    rng.append("RunRngType." + t.split(".", 1)[1])
        vd = vdiff.get(cid, {})
        status, note = VD.get(cid, (vd.get("status", "same"), "identical after normalising decompile style (state machines vs async); engine calls gained cardPlay arguments"))
        if status == "diff":
            raise SystemExit("unclassified version diff: " + cid)
        aw = {}
        for k, v in awaits.get(cid, {}).get("new", {}).items():
            seq = [("AttackCommand.Execute" if x == "Execute" else re.sub(r"@\w*\??", "", x)) for x in v if x not in ("Wait", "GetResult")]
            if seq:
                aw[k] = seq
        notes = list(man.get("notes", []))
        if man.get("sources") and e["pool"] in ("IRONCLAD", "COLORLESS"):
            pass
        entry = collections.OrderedDict()
        entry["id"] = cid
        entry["model_id"] = e["model_id"]
        entry["class"] = e["class"]
        entry["file"] = e["file"]
        entry["pool"] = e["pool"]
        entry["type"] = e["type"]
        entry["rarity"] = e["rarity"]
        entry["cost"] = e["cost"]
        entry["costs_x"] = e["costs_x"]
        entry["target_type"] = e["target_type"]
        entry["keywords"] = e["keywords"]
        entry["tags"] = e["tags"]
        entry["ported"] = cid in ported_cards
        entry["scope"] = scope
        entry["mechanics"] = man["mech"]
        entry["prompts"] = prompts
        entry["kernel_requirements"] = reqs
        entry["rng_streams"] = rng
        entry["await_order"] = aw
        entry["depends_on"] = {
            "powers": [{"id": p, "ported": p in ported_powers} for p in dep_powers],
            "cards": [{"id": c, "ported": c in ported_cards} for c in dep_cards],
        }
        entry["obtained_from"] = man.get("sources", [])
        entry["ascension_sensitivity"] = ("indirect: added to the starting deck at A5+ (AscensionLevel.AscendersBane, Entities.Ascension/AscensionManager.cs:33-35); no ascension branch in the card"
                                          if cid == "ASCENDERS_BANE" else "none (no AscensionLevel reference in the file)")
        entry["version_delta_v107"] = {"status": status, "v107_file": vd.get("old"), "note": note}
        entry["tier"] = tier
        entry["port_batch"] = batch
        entry["notes"] = notes
        entry["open_questions"] = man.get("oq", [])
        entries.append(entry)

    env = collections.OrderedDict()
    env["schema"] = "sts2sim.inventory.classified/v1"
    env["kind"] = KIND
    env["game_version"] = cards["game_version"]
    env["raw_manifest"] = "sim/specs/inventory/cards.json"
    env["raw_source_digest"] = cards["source_digest"]
    env["slice"] = {"pools": ["IRONCLAD", "COLORLESS", "CURSE", "STATUS", "TOKEN", "EVENT", "QUEST", "DEPRIVED", "DEPRECATED"],
                    "rule": "every cards.json entry whose pool is not a playable character pool other than IRONCLAD (DEPRECATED included: the only card outside the named pools); no entry has pool 'unlisted'",
                    "order": "same order and ids as cards.json (sorted by id)"}
    env["generated_by"] = common.generator("cards-ironclad-colorless", "vdiff.py", "awaits.py", "build.py")
    env["rule_truth"] = "output/server-bootstrap/linux-decompiled (v0.111.0); v0.107.1 tree artifacts/knowledge/decompiled-v0.107.1 used for cross-reference only"
    env["count"] = len(entries)
    env["fields"] = {
        "id..tags": "copied from cards.json (join key: id / model_id)",
        "ported": "defined in sim/Sts2Sim.Core today (ported.json)",
        "scope": "game | multiplayer_only (MultiplayerConstraint.MultiplayerOnly: GetUnlockedCards drops it in single player, Models/CardPoolModel.cs:68-81, Runs/IRunState.cs:74-84) | test_only (mock) | deprecated",
        "mechanics": "effect primitives in execution order; 'upgrade:' items list keyword or cost changes only (numbers are in cards.json upgrade)",
        "prompts": "player prompts the card triggers: type (HandSelect, HandUpgrade, PileSelect, ChooseCard), game entry, pile, min/max select, filter, skip, when, site",
        "kernel_requirements": "Cmd/Hook/subsystem features the sim needs before this card can be ported; existing sim facades are not listed (var overloads such as GainBlock(BlockVar) map to the decimal facades with the var's props)",
        "rng_streams": "RunRngType streams consumed, in execution order, with the consuming call",
        "await_order": "awaited calls per async method in source order, recovered from v0.111.0 state numbers (UI awaits removed); synchronous calls are not listed",
        "depends_on": "powers applied or read (Apply/GetPower/HasPower/GetPowerAmount/OfType/Remove, hover tips excluded) and cards created or referenced, each with sim ported status",
        "obtained_from": "for non-reward cards: the models that create, offer or add the card (from cards.json referenced_by_models and the decompile)",
        "ascension_sensitivity": "ascension branches in the card or ascension rules that add it",
        "version_delta_v107": "status same | cosmetic | rule-change | new | decompile-gap, from a normalised diff (constructor, vars, keywords, tags, upgrade, literal overrides, Cmd/enum/var/call token multisets, await order) reviewed by hand; v107_file is relative to artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core",
        "tier": TIERS,
        "port_batch": BATCHES,
    }
    env["requirement_levels"] = {"level 4": "decompile gap", "level 3": [p for l, p in LEVELS if l == 3], "level 2": [p for l, p in LEVELS if l == 2],
                                 "level 1": "anything else (exists in the sim or maps onto existing facades)"}
    env["entries"] = entries
    with open(OUT_JSON, "w") as fh:
        json.dump(env, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    print("wrote", OUT_JSON, len(entries))


if __name__ == "__main__":
    main()
