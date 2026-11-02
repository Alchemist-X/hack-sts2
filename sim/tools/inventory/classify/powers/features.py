"""Mechanical features for every power (scratch): hooks, cmds, referrers with reference modes, tokens."""

import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys
from collections import defaultdict, OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, common.INVENTORY_TOOLS)

import cs_source  # noqa: E402

DECOMP = common.DECOMPILE_ROOT
INV = common.INVENTORY

KIND_OF_DIR = {
    "Models.Cards": "cards", "Models.Cards.Mocks": "cards(mock)",
    "Models.Powers": "powers", "Models.Powers.Mocks": "powers(mock)",
    "Models.Relics": "relics", "Models.Potions": "potions", "Models.Potions.Mocks": "potions(mock)",
    "Models.Monsters": "monsters", "Models.Monsters.Mocks": "monsters(mock)",
    "Models.Encounters": "encounters", "Models.Encounters.Mocks": "encounters(mock)",
    "Models.Events": "events", "Models.Events.Mocks": "events(mock)",
    "Models.Enchantments": "enchantments", "Models.Enchantments.Mocks": "enchantments(mock)",
    "Models.Afflictions": "afflictions", "Models.Afflictions.Mocks": "afflictions(mock)",
    "Models.Orbs": "orbs", "Models.Orbs.Mock": "orbs(mock)",
    "Models.Modifiers": "modifiers", "Models.Characters": "characters", "Models.Acts": "acts",
    "Models.Achievements": "achievements", "Models.Badges": "badges", "Models.Singleton": "singleton",
    "Models": "models_core", "Models.CardPools": "pools", "Models.RelicPools": "pools",
    "Models.PotionPools": "pools", "Models.Exceptions": "exceptions",
}

APPLY_METHODS = {"Apply"}
READ_METHODS = {"HasPower", "GetPower", "GetPowerAmount", "GetPowers", "TryGetPower", "GetPowerInstances",
                "HasPowerInstance", "GetPowersOfType"}
REMOVE_METHODS = {"Remove"}
TIP_METHODS = {"FromPower", "FromPowerWithPowerHoverTips", "FromPowerWithAmount"}


def load(name):
    with open(os.path.join(common.STAGE1 if name == "kernel-gaps.json" else INV, name)) as fh:
        return json.load(fh)


def cmd_status_table():
    k = load("kernel-gaps.json")
    st = defaultdict(set)
    for cls, lst in k["cmd_facades"].items():
        for m in lst:
            st[cls + "." + m["name"]].add(m["status"])
    return {a: sorted(b) for a, b in st.items()}


def scan_refs(power_classes):
    """Return {power_class: {referrer_label: set(modes)}} and core refs."""
    pat_generic = re.compile(r"\b(\w+)\s*<\s*(?:[\w.]+\.)?(\w+)\s*>")
    pat_ident = re.compile(r"\b([A-Z]\w*Power)\b")
    model_refs = defaultdict(lambda: defaultdict(set))
    core_refs = defaultdict(lambda: defaultdict(set))
    for d in sorted(os.listdir(DECOMP)):
        full = os.path.join(DECOMP, d)
        if not os.path.isdir(full):
            continue
        short = d.replace("MegaCrit.Sts2.Core.", "")
        for fn in sorted(os.listdir(full)):
            if not fn.endswith(".cs"):
                continue
            path = os.path.join(full, fn)
            with open(path, encoding="utf-8-sig", errors="replace") as fh:
                text = fh.read()
            if "Power" not in text:
                continue
            masked = cs_source.Masker(text).run()
            cls = fn[:-3]
            seen_generic = set()
            for m in pat_generic.finditer(masked):
                meth, arg = m.group(1), m.group(2)
                if arg in power_classes:
                    if meth in APPLY_METHODS:
                        mode = "apply"
                    elif meth in READ_METHODS:
                        mode = "read"
                    elif meth in REMOVE_METHODS:
                        mode = "remove"
                    elif meth in TIP_METHODS:
                        mode = "tip"
                    elif meth == "PowerVar":
                        mode = "var"
                    elif meth == "Power" and masked[max(0, m.start() - 8):m.start()].endswith("ModelDb."):
                        mode = "modeldb"
                    else:
                        mode = "generic:" + meth
                    seen_generic.add((arg, m.start(2)))
                    _add(model_refs, core_refs, short, cls, arg, mode)
            for m in pat_ident.finditer(masked):
                arg = m.group(1)
                if arg not in power_classes or (arg, m.start(1)) in seen_generic:
                    continue
                if short in ("Models.Powers", "Models.Powers.Mocks") and arg == cls:
                    continue
                pre = masked[max(0, m.start() - 12):m.start()]
                post = masked[m.end():m.end() + 2]
                if re.search(r"typeof\(\s*$", pre):
                    mode = "typeof"
                elif re.search(r"\bis\s+$", pre):
                    mode = "is"
                elif re.search(r"\bas\s+$", pre):
                    mode = "as"
                elif re.search(r"\bnew\s+$", pre):
                    mode = "new"
                elif re.search(r"\(\s*$", pre) and post.startswith(")"):
                    mode = "cast"
                elif post.startswith("."):
                    mode = "static"
                elif re.search(r"(class|:)\s+$", pre) or re.search(r"\bclass\s+\w+\s*:\s*$", pre):
                    mode = "inherit"
                else:
                    mode = "ident"
                _add(model_refs, core_refs, short, cls, arg, mode)
    return model_refs, core_refs


def _add(model_refs, core_refs, short, cls, arg, mode):
    if short.startswith("Models"):
        kind = KIND_OF_DIR.get(short, short)
        if kind.startswith("powers") and cls == arg:
            return
        model_refs[arg]["%s/%s" % (kind, cls)].add(mode)
    else:
        core_refs[arg]["%s/%s" % (short, cls)].add(mode)


TOKEN_PATTERNS = OrderedDict([
    ("participants", r"\bparticipants\b"),
    ("ascension", r"\bAscensionLevel\.\w+|\bHasAscension\b|\bGetValueIfAscension\b"),
    ("multiplayer", r"\bGetScaledAmountForMultiplayer\b|\bShouldScaleInMultiplayer\b|\bPlayers\.Count\b|\bIsMultiplayer\b|\bLocalContext\b|\bNetId\b"),
    ("history", r"\bHistory\b"),
    ("orbs", r"\bOrbCmd\b|\bOrbQueue\b|\bOrbModel\b|\bOrbs\b|\b\w+Orb\b"),
    ("osty", r"\bOstyCmd\b|\bOsty\b|\bIsOsty\b|\bPetOwner\b|\bPets\b|\bIsPet\b|\bAddPet\b|\bFromOsty\b"),
    ("stars", r"\bStars\b|\bGainStars\b|\bLoseStars\b|\bStarCost\b|\bAfterStars\w*"),
    ("forge", r"\bForgeCmd\b|\bSovereignBlade\b|\bAfterForge\b"),
    ("summon_monster", r"\bCreatureCmd\.Add\b|\bCreateCreature\b"),
    ("doom", r"\bDoomPower\b|\bAfterDiedToDoom\b|\bDoomKill\b"),
    ("affliction", r"\bAfflict\w*\b|\bAffliction\w*\b"),
    ("enchantment", r"\bEnchant\w*\b"),
    ("card_creation", r"\bCreateCard\b|\bCardFactory\b|\bGetDistinctForCombat\b|\bGetForCombat\b|\bAddGeneratedCard\w*\b|\bModelDb\.Card<"),
    ("card_pile_move", r"\bCardPileCmd\.\w+|\bCardCmd\.\w+"),
    ("card_cost", r"\bEnergyCost\b|\bSetThisTurn\w*\b|\bSetUntilPlayed\b|\bTemporaryCost\w*\b|\bCostModifiers\b"),
    ("keywords", r"\bCardKeyword\.\w+|\bAddKeyword\b|\bRemoveKeyword\b|\bApplyKeyword\b"),
    ("autoplay", r"\bAutoPlay\w*\b"),
    ("extra_turn", r"\bShouldTakeExtraTurn\b|\bExtraTurn\w*\b|\bPlayersTakingExtraTurn\b"),
    ("relic", r"\bRelicModel\b|\bGetRelic\b|\bRelics\b|\bRelicCmd\b"),
    ("potion", r"\bPotionCmd\b|\bPotionModel\b|\bPotion\w*Used\b"),
    ("gold", r"\bGold\b|\bGainGold\b|\bLoseGold\b"),
    ("max_hp", r"\bMaxHp\b|\bGainMaxHp\b|\bLoseMaxHp\b|\bSetMaxHp\b"),
    ("monster_ai", r"\bMoveStateMachine\b|\bSetMoveImmediate\b|\bNextMove\b|\bRollMove\b|\bMonster\.\w+|\bStun\b|\bIntent\w*\b|\bMoveState\b"),
    ("kill_escape", r"\bCreatureCmd\.Kill\b|\bCreatureCmd\.Escape\b|\bEscape\b"),
    ("card_select", r"\bCardSelectCmd\.\w+"),
    ("internal_data", r"\bInitInternalData\b|\bGetInternalData\b"),
    ("rng", r"\bRng\b|\bRunRngType\b|\bPlayerRngType\b|\bNextItem\b|\bNextInt\b|\bShuffle\b|\bStableShuffle\b|\bUnstableShuffle\b|\bTakeRandom\b|\bCombatTargets\b|\bCombatCardGeneration\b"),
    ("energy", r"\bEnergyCmd\b|\bGainEnergy\b|\bLoseEnergy\b|\bSetEnergy\b|\bEnergy\b"),
    ("draw", r"\bCardPileCmd\.Draw\b|\bModifyHandDraw\w*\b"),
    ("block", r"\bGainBlock\b|\bLoseBlock\b|\bClearBlock\b|\bBlock\b"),
    ("damage", r"\bCreatureCmd\.Damage\b|\bDamageCmd\.Attack\b|\bModifyDamage\w*\b"),
    ("hp_loss", r"\bModifyHpLost\w*\b|\bLoseHp\b"),
    ("heal", r"\bHeal\b"),
    ("x_value", r"\bModifyXValue\b|\bCapturedXValue\b|\bXValue\b"),
    ("replay", r"\bModifyCardPlayCount\b|\bBaseReplayCount\b|\bReplay\w*\b"),
    ("shuffle_order", r"\bModifyShuffleOrder\b"),
    ("result_pile", r"\bModifyCardPlayResultLocation\b|\bResultPile\b"),
    ("deck", r"\bDeck\b|\bRemoveFromDeck\b|\bDeckVersion\b"),
    ("combat_end", r"\bAfterCombatEnd\b|\bAfterCombatVictory\b"),
])

UI_ONLY_OVERRIDES = {"AmountLabelColor", "ExtraHoverTips", "DisplayAmount", "IsVisibleInternal", "ShouldPlayVfx",
                     "SmartDescriptionLocKey", "RemoteDescriptionLocKey", "Title", "Description"}


def main():
    powers = load("powers.json")
    hooks = {h["name"]: h for h in load("hooks.json")["hooks"]}
    cmdst = cmd_status_table()
    classes = {e["class"] for e in powers["entries"]}
    model_refs, core_refs = scan_refs(classes)
    out = OrderedDict()
    for e in powers["entries"]:
        path = os.path.join(DECOMP, "MegaCrit.Sts2.Core." + e["file"])
        cf = cs_source.CsFile(path, e["file"])
        masked = cf.masked
        td = cf.main_type()
        toks = OrderedDict()
        for name, pat in TOKEN_PATTERNS.items():
            found = sorted(set(re.findall(pat, masked)))
            if found:
                toks[name] = found
        inst_fields = []
        for tdecl in cs_source.iter_types(td):
            if tdecl is not td:
                continue
            for m in tdecl.members:
                if m.kind == "field" and "static" not in m.modifiers and "const" not in m.modifiers:
                    inst_fields.append({"name": m.name, "line": m.line, "decl": re.sub(r"\s+", " ", m.head.strip())[:140]})
        hk = []
        for h in e["abstract_model_hooks"]:
            info = hooks.get(h, {})
            hk.append({"hook": h, "in_sim": info.get("in_sim"), "scope": info.get("scope"),
                       "line": e["override_lines"].get(h)})
        non_hook = [o for o in e["hooks_overridden"] + e["protected_overrides"] if o not in e["abstract_model_hooks"]]
        cmds = []
        for c in e["api_calls"]:
            cmds.append({"call": c, "status": cmdst.get(c, ["(not a facade)" if not c.startswith("Hook.") else "hook-dispatcher"])})
        apply_targets = sorted(set(re.findall(r"\bApply<(\w+)>", masked)))
        refs_in = model_refs.get(e["class"], {})
        refs_core = core_refs.get(e["class"], {})
        out[e["class"]] = OrderedDict([
            ("id", e["id"]), ("file", e["file"]), ("is_mock", e["is_mock"]), ("is_abstract", e["is_abstract"]),
            ("base_class", e["base_class"]), ("power_type", e["power_type"]), ("stack_type", e["stack_type"]),
            ("instance_type", e["instance_type"]), ("allow_negative", e["allow_negative"]),
            ("hooks", hk), ("non_hook_overrides", non_hook),
            ("ui_only_overrides", [o for o in non_hook if o in UI_ONLY_OVERRIDES]),
            ("cmds", cmds), ("apply_targets", apply_targets),
            ("rng_usage", e["rng_usage"]), ("tokens", toks), ("instance_fields", inst_fields),
            ("dynamic_vars", e["dynamic_vars"]), ("constants", e["constants"]),
            ("lines_logic", e["lines_logic"]), ("lines_total", e["lines_total"]),
            ("referrers", OrderedDict(sorted((k, sorted(v)) for k, v in refs_in.items()))),
            ("core_referrers", OrderedDict(sorted((k, sorted(v)) for k, v in refs_core.items()))),
            ("manifest_referenced_by_models", e["referenced_by_models"]),
        ])
    with open(common.work_path("powers", "features.json"), "w") as fh:
        json.dump(out, fh, indent=1)
    print("wrote", len(out))


if __name__ == "__main__":
    main()
