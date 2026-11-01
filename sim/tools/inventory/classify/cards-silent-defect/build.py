import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys
from collections import Counter, OrderedDict, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import data_defect  # noqa: E402
import data_powers  # noqa: E402
import data_silent  # noqa: E402
import vdelta  # noqa: E402
from data_common import rng  # noqa: E402

D1 = common.DECOMPILE_ROOT
OUT_JSON = common.stage1_path("cards-silent-defect.classified.json")
POOLS = ("SILENT", "DEFECT")

DELTA = {
    "Accelerant": ("rule-change", "rarity Rare -> Uncommon (reward/shop odds); effect unchanged"),
    "AllForOne": ("cosmetic", "foreach/Dispose decompile pattern only"),
    "Anticipate": ("rule-change", "upgrade +1 -> +2 Dexterity"),
    "Barrage": ("cosmetic", "decompiler casts (decimal.op_Implicit, IReadOnlyCollection)"),
    "BiasedCognition": ("rule-change", "Focus 4 -> 5"),
    "BouncingFlask": ("cosmetic", "loop condition and null test inverted by the state machine"),
    "BubbleBubble": ("cosmetic", "null-coalescing rewritten in the UI glow getter"),
    "Chill": ("cosmetic", "foreach/Dispose decompile pattern only"),
    "Claw": ("cosmetic", "local DamageVar temporaries"),
    "Compact": ("rule-change", "per-card CardCmd.Transform -> one batched Transform(IEnumerable<CardTransformation>, null): "
                               "same cards, different side-effect order"),
    "CompileDriver": ("cosmetic", "LINQ query syntax -> method syntax"),
    "Darkness": ("cosmetic", "foreach/Dispose decompile pattern only"),
    "EchoingSlash": ("rule-change", "rarity Rare -> Uncommon; AttackContext and CreatureCmd.Damage now take the CardPlay (API)"),
    "EnergySurge": ("cosmetic", "foreach/Dispose decompile pattern only"),
    "Expertise": ("rule-change", "draw until 6 (+1) cards in hand -> draw 2 (+1) and give them Retain this turn"),
    "Expose": ("cosmetic", "CreatureCmd.LoseBlock gained a remover argument (API)"),
    "Finisher": ("cosmetic", "history filter e.CardPlay.Card.Owner -> e.CardPlay.Player (same in single player)"),
    "FlakCannon": ("cosmetic", "decompiler casts"),
    "Flanking": ("rule-change", "rarity Uncommon -> Rare (MultiplayerOnly card)"),
    "Flechettes": ("cosmetic", "decompiler casts"),
    "FlickFlack": ("rule-change", "damage 6 -> 7"),
    "Ftl": ("cosmetic", "history filter Card.Owner -> CardPlay.Player (same in single player)"),
    "GeneticAlgorithm": ("cosmetic", "decompiler casts"),
    "GrandFinale": ("cosmetic", "VFX null check inverted"),
    "Haze": ("rule-change", "cost 3 -> 2, Sly removed, adds Weak 1 (+1) to all hittable enemies, list overloads"),
    "HelixDrill": ("cosmetic", "explicit generic arguments"),
    "HiddenDaggers": ("cosmetic", "nested await textual order in v0.107.1 only"),
    "Hyperbeam": ("rule-change", "damage 28 (+8) -> 24 (+6); permanent -3 FocusPower -> HyperbeamFocusDownPower (this turn only)"),
    "Ignition": ("rule-change", "rarity Rare -> Uncommon (MultiplayerOnly card)"),
    "KnifeTrap": ("cosmetic", "foreach/Dispose decompile pattern only"),
    "MementoMori": ("cosmetic", "history filter e.Card.Owner == owner -> e.Actor == owner creature (same in single player)"),
    "Mirage": ("rule-change", "upgrade: cost -1 -> remove Exhaust"),
    "MomentumStrike": ("rule-change", "damage 10 (+3) -> 11 (+4)"),
    "Murder": ("cosmetic", "decompiler casts"),
    "Nightmare": ("rule-change", "CanBeGeneratedInCombat override (false) added: no longer offered by in-combat random generation"),
    "Null": ("rule-change", "Weak 2 -> 1"),
    "Outbreak": ("rule-change", "reworked: 1-cost Uncommon Power (OutbreakPower 11 (+4), repeat 3) -> 3-cost Rare AllEnemies "
                                "Skill: Poison 9 (+3) to all, then trigger each enemy's Poison"),
    "PiercingWail": ("cosmetic", "foreach/Dispose decompile pattern only"),
    "Pinpoint": ("cosmetic", "history filter Card.Owner -> CardPlay.Player (same in single player)"),
    "PreciseCut": ("cosmetic", "decompiler casts"),
    "Prepared": ("cosmetic", "nested await textual order in v0.107.1 only"),
    "Reboot": ("cosmetic", "state-machine goto case"),
    "Refract": ("rule-change", "damage 9 -> 10"),
    "RocketPunch": ("rule-change", "on Status creation: cost 0 until played (SetUntilPlayed) -> cost -1 until played, stacking (AddUntilPlayed)"),
    "Scrape": ("rule-change", "zero-cost filter CostModifiers.Local -> CostModifiers.All"),
    "Shatter": ("rule-change", "gains Exhaust"),
    "Sunder": ("rule-change", "damage 24 -> 26"),
    "Synchronize": ("rule-change", "Exhaust removed (upgrade now raises the multiplier); Focus per distinct orb type 2 -> 1 (+1)"),
    "TeslaCoil": ("cosmetic", "state-machine try-region guard"),
    "TheHunt": ("cosmetic", "explicit generic arguments"),
    "Thunder": ("rule-change", "ThunderPower 6 (+2) -> 8 (+3)"),
    "Tracking": ("rule-change", "applies TrackingPower 50 every time (was 2 first, 1 after); TrackingPower now multiplies by 1 + Amount/100"),
    "TrashToTreasure": ("rule-change", "upgrade: Innate -> cost -1"),
    "Voltaic": ("cosmetic", "decompiler casts"),
    "WellLaidPlans": ("rule-change", "reworked: Uncommon 1-cost retain-N prompt (SingleplayerOnly) -> Rare 2-cost (-1) "
                                     "whole-hand retain via WellLaidPlansPower.ShouldFlush"),
}

OQ_NESTED = ("Nested prompt chain: a Sly-granted prompting Skill discarded by this card's prompt auto-plays inside this play "
             "and opens its own HandSelect; confirm the choice runtime re-executes it exactly (design-combat-kernel §4.5-4.6).")
OQ_UNLOCK = "Pool filtering depends on the run's UnlockState (epoch gates); the run input must carry the recorded UnlockState."
EXTRA_OQ = {
    "Survivor": [OQ_NESTED], "Acrobatics": [OQ_NESTED], "DaggerThrow": [OQ_NESTED], "Prepared": [OQ_NESTED],
    "HiddenDaggers": [OQ_NESTED],
    "ToolsOfTheTrade": [OQ_NESTED + " Here the chain starts inside the turn-setup HookPlayerChoiceContext."],
    "HandTrick": ["Gives Sly to a Skill that may itself prompt; see the nested prompt chain question (Survivor and other discarders)."],
    "MasterPlanner": ["Played Skills keep Sly permanently; a prompting Skill discarded later auto-plays and prompts inside "
                      "the discarder's play (nested prompt chain)."],
    "WhiteNoise": [OQ_UNLOCK], "CreativeAi": [OQ_UNLOCK],
    "GeneticAlgorithm": ["Does the recorder/save export carry the [SavedProperty] CurrentBlock/IncreasedBlock values so a "
                         "mid-run import starts from the right block?"],
    "TheHunt": ["When does the extra CardReward(ForRoom, 3) roll its cards (at AddExtraReward or at the reward screen), and "
                "in which order relative to the room's normal rewards (PlayerRng.Rewards)? Run-layer question."],
}

TIER_LEGEND = {
    "1": "pure numbers through facades/hooks the sim already has (incl. X cost, temporary costs, keywords Exhaust/Innate/Retain/Ethereal)",
    "2": "needs facades/hooks that exist in the game but not in the sim (Sly discard auto-play, AutoPlay, Shiv batch generation, "
         "CardCmd.Upgrade/ApplyKeyword/ApplySingleTurn*, CalculatedVar, AttackContext, card listeners, CreateClone, Transform)",
    "3": "needs a player-choice prompt or a new subsystem (orbs, combat history, card generation from pools, enchantments)",
    "4": "decompile-unreadable or cross-cutting combat/run effects (deck persistence, run-layer rewards)",
}

BATCH_LEGEND = OrderedDict([
    (1, "basics on the current kernel: only ported facades and ported powers"),
    (2, "new simple powers on existing hooks (poison family, temporary stats, next-turn powers, draw/cost powers) and status-card generators"),
    (3, "card-kernel facades: Sly discard auto-play, CardCmd.AutoPlay, Shiv generation, CalculatedVar, AttackContext, card listeners, clone/transform/upgrade"),
    (4, "combat history readers (CardPlaysFinished/Started, CardDiscarded, CardDrawn, EnergySpent)"),
    (5, "player-choice prompts (CardSelectCmd hand/pile selection; includes the Silent starter Survivor)"),
    (6, "orb core: OrbQueue, 5 orbs, Channel/EvokeNext, FocusPower and temporary focus (includes the Defect starters Zap/Dualcast)"),
    (7, "orb advanced: evoke loops, passive triggers, slot changes, random orbs, orb-trigger powers, orb history"),
    (8, "generation and cross-cutting: CardFactory pools + UnlockState, enchantments, deck persistence, run-layer rewards"),
    (99, "deferred: MultiplayerOnly cards (removed from single-player pools)"),
])

BATCH_PREREQ = {1: [], 2: [1], 3: [1, 2], 4: [3], 5: [3], 6: [3], 7: [4, 6], 8: [3, 5], 99: []}

IDENTITY = ["id", "model_id", "class", "file", "pool", "character", "rarity", "type", "cost", "costs_x", "target_type",
            "keywords", "tags", "starting_deck_of", "ported"]


def read(path):
    return open(path, encoding="utf-8-sig").read()


def epoch_gates():
    d = os.path.join(D1, "MegaCrit.Sts2.Core.Timeline.Epochs")
    out = {}
    for fn in sorted(os.listdir(d)):
        if not fn.endswith(".cs"):
            continue
        t = read(os.path.join(d, fn))
        for m in re.finditer(r"ModelDb\.Card<(\w+)>", t):
            out.setdefault(m.group(1), []).append(fn[:-3])
    return out


def line_of(text, pat):
    m = re.search(pat, text, re.M)
    if not m:
        return None
    return text.count("\n", 0, m.start()) + 1


def implicit_rng(entry, data, text):
    api = set(entry["api_calls"])
    out = []
    if "CardPileCmd.Draw" in api or "CardCmd.DiscardAndDraw" in api:
        out.append(rng("Shuffle", "CardPileCmd.Draw reshuffles discard into draw when the draw pile is empty "
                                  "(StableShuffle with Rng.Shuffle)", explicit=False))
    if any(a.startswith("OrbCmd.Channel") or a.startswith("OrbCmd.EvokeNext") for a in api):
        out.append(rng("CombatTargets", "a Channel into a full queue or an EvokeNext evokes the front orb; a LightningOrb "
                                        "evoke picks a random hittable opponent", explicit=False))
    if "CardCmd.Discard" in api or "CardCmd.DiscardAndDraw" in api:
        out.append(rng("CombatTargets", "a discarded Sly card with TargetType AnyEnemy auto-plays at a random hittable enemy "
                                        "(Commands/CardCmd.cs:411)", explicit=False))
    for pw in data["deps"]["powers"]:
        for stream, via in data_powers.POWER_RNG.get(pw, []):
            out.append(rng(stream, via, site="power:" + pw, explicit=False))
    for r in reversed(data["rng_explicit"]):
        out.insert(0, r)
    seen = []
    for r in out:
        key = (r["stream"], r["via"])
        if key not in [(s["stream"], s["via"]) for s in seen]:
            seen.append(r)
    for i, r in enumerate(seen, 1):
        r["order"] = i
    return seen


def main():
    raw = json.load(open(common.inventory_path("cards.json")))
    data = {}
    data.update(data_silent.S)
    data.update(data_defect.D)
    gates = epoch_gates()
    entries = []
    deltas = {}
    for e in raw["entries"]:
        if e["pool"] not in POOLS:
            continue
        cls = e["class"]
        d = data[cls]
        path = os.path.join(D1, e["file"].replace("Models.Cards/", "MegaCrit.Sts2.Core.Models.Cards/"))
        text = read(path)
        code = re.sub(r"//[^\n]*", "", text)
        vd = vdelta.compare(cls)
        if vd["status"] == "new":
            delta = {"status": "new", "note": "no v0.107.1 file (card added after v0.107.1)", "old_file": None}
        elif vd["status"] == "same_tokens":
            delta = {"status": "same", "note": "normalized identifier/literal/operator multiset, awaited Cmd order, ctor, vars, "
                                               "keywords, tags, upgrade body and override set identical",
                     "old_file": vd["old_file"]}
        else:
            st, note = DELTA[cls]
            delta = {"status": st, "note": note, "old_file": vd["old_file"], "mechanical_flags": vd["flags"]}
        dep_notes = []
        for pw in d["deps"]["powers"]:
            pi = data_powers.P[pw]
            if pi["delta_v107"] not in ("same", "cosmetic") and not pi["delta_v107"].startswith("cosmetic"):
                dep_notes.append("%s: %s" % (pw, pi["delta_v107"]))
        if dep_notes:
            delta["dependency_deltas"] = dep_notes
        deltas[cls] = delta

        asc_tokens = sorted(set(re.findall(r"\b(AscensionHelper|AscensionLevel|HasAscension|IsAscension)\b", code)))
        asc = {"level": "none" if not asc_tokens else "direct", "tokens": asc_tokens,
               "note": "no ascension reference in the card file" if not asc_tokens else "reads ascension"}

        mp_only = bool(re.search(r"MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.MultiplayerOnly", code))
        tiers = [d["tier_own"]]
        for pw in d["deps"]["powers"]:
            tiers.append(data_powers.P[pw]["tier"])
        for cd in d["deps"]["cards"]:
            tiers.append(data_powers.CARD_DEPS[cd]["tier"])
        if d["deps"]["orbs"]:
            tiers.append(3)
        if d["deps"]["enchantments"]:
            tiers.append(3)
        tier = max(tiers)
        dep_detail = {
            "powers": [{"power": pw, "tier": data_powers.P[pw]["tier"], "ported": data_powers.P[pw]["ported"],
                        "batch": data_powers.P[pw]["batch"]} for pw in d["deps"]["powers"]],
            "cards": [{"card": cd, "tier": data_powers.CARD_DEPS[cd]["tier"], "pool": data_powers.CARD_DEPS[cd]["pool"]}
                      for cd in d["deps"]["cards"]],
            "orbs": list(d["deps"]["orbs"]),
            "enchantments": list(d["deps"]["enchantments"]),
        }
        cites = []
        ol = e["override_lines"]
        f = e["file"]
        if e.get("ctor_line"):
            cites.append("%s:%d (ctor)" % (f, e["ctor_line"]))
        for k in ("CanonicalVars", "CanonicalKeywords", "CanonicalTags", "OnPlay", "OnUpgrade", "IsPlayable",
                  "HasEnergyCostX", "MultiplayerConstraint", "CanBeGeneratedInCombat", "AfterCardPlayed",
                  "AfterCardEnteredCombat", "AfterCardGeneratedForCombat", "AfterDowngraded", "TargetType", "GainsBlock"):
            if k in ol:
                cites.append("%s:%d (%s)" % (f, ol[k], k))
        sm = line_of(text, r"private void MoveNext\(\)")
        if sm and "OnPlay" in ol:
            cites.append("%s:%d (OnPlay state machine MoveNext)" % (f, sm))

        out = OrderedDict()
        for k in IDENTITY:
            out[k] = e.get(k)
        out["scope"] = "multiplayer_only" if mp_only else "single_player"
        out["epoch_gate"] = gates.get(cls, [None])[0]
        out["mechanics"] = d["mechanics"]
        out["prompts"] = d["prompts"]
        out["kernel_requirements"] = d["kernel_requirements"]
        out["depends_on"] = dep_detail
        out["rng_streams"] = implicit_rng(e, d, code)
        out["ascension_sensitivity"] = asc
        out["version_delta_v107"] = delta
        out["tier"] = tier
        out["tier_own"] = d["tier_own"]
        out["port_batch"] = d["port_batch"]
        out["notes"] = d["notes"]
        out["open_questions"] = list(d["open_questions"]) + EXTRA_OQ.get(cls, [])
        out["citations"] = cites
        entries.append(out)

    manifest = OrderedDict()
    manifest["schema"] = "sts2sim.inventory.classified/v1"
    manifest["kind"] = "cards-silent-defect"
    manifest["game_version"] = raw["game_version"]
    manifest["source_manifest"] = "sim/specs/inventory/cards.json"
    manifest["source_digest"] = raw["source_digest"]
    manifest["slice"] = {"pools": list(POOLS), "join_key": "id (same order and ids as cards.json entries filtered to these pools)"}
    manifest["generated_by"] = common.generator("cards-silent-defect", "build.py")
    manifest["count"] = len(entries)
    manifest["tier_legend"] = TIER_LEGEND
    manifest["tier_rule"] = ("tier = max(tier_own, tiers of depends_on powers/cards, 3 if orbs or enchantments); tier_own covers "
                             "the card file only")
    manifest["port_batch_legend"] = {str(k): v for k, v in BATCH_LEGEND.items()}
    manifest["port_batch_prerequisites"] = {str(k): v for k, v in BATCH_PREREQ.items()}
    manifest["field_notes"] = {
        "prompts": "every CardSelectCmd site the card (or its own power) opens; type HandSelect/PileSelect per "
                   "docs/sim-port/design-combat-kernel.md §4.3",
        "rng_streams": "explicit = named in the card/power code; explicit=false = reached through a facade the card calls "
                       "(draw reshuffle, orb evoke, Sly auto-play); order = listing order, not a consumption trace",
        "version_delta_v107": "mechanical comparison against artifacts/knowledge/decompiled-v0.107.1 (sim/tools/inventory/classify/cards-silent-defect/vdelta.py) "
                              "then manual classification of every flagged file",
        "depends_on.powers": "power classes the card applies or reads (tier, ported, batch); hooks/facades they need are in dependency_powers",
    }
    manifest["dependency_powers"] = OrderedDict(sorted((k, v) for k, v in data_powers.P.items()))
    manifest["dependency_cards"] = data_powers.CARD_DEPS
    manifest["orbs"] = data_powers.ORBS
    manifest["entries"] = entries
    with open(OUT_JSON, "w") as fh:
        json.dump(manifest, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    print("wrote", OUT_JSON, len(entries))
    json.dump({"entries": entries}, open(common.work_path("cards-silent-defect", "entries_cache.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
