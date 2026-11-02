"""Classified manifests for the six minor kinds: enchantments, afflictions, orbs, modifiers, acts, characters.

Inputs: the raw manifests (extract_models.py), the manual records in data_<kind>.py and the
v0.107.1 fingerprint diff written by vdiff.py. Output: <work>/stage1/<kind>.classified.json.
"""

import json
import os
import re
import sys
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, ".."))
import common  # noqa: E402
import lib  # noqa: E402
import data_enchantments  # noqa: E402,F401
import data_afflictions  # noqa: E402,F401
import data_orbs  # noqa: E402,F401
import data_modifiers  # noqa: E402,F401
import data_acts  # noqa: E402,F401
import data_characters  # noqa: E402,F401

KINDS = ["enchantments", "afflictions", "orbs", "modifiers", "acts", "characters"]
IDENTITY = ["id", "model_id", "class", "file", "class_line", "base_class", "is_mock", "is_abstract", "registered", "is_deprecated"]
FACTS = {
    "enchantments": ["dynamic_vars", "constants"],
    "afflictions": ["dynamic_vars", "constants"],
    "orbs": ["passive_val_raw", "evoke_val_raw", "modeldb_orbs_index", "constants"],
    "modifiers": ["modifier_list", "mutually_exclusive", "clears_player_deck"],
    "acts": ["index", "is_default", "number_of_weak_encounters", "base_number_of_rooms", "encounters_weak", "encounters_normal",
             "encounters_elite", "encounters_boss", "boss_discovery_order", "ancients", "events", "map_params", "first_run_order",
             "ancient_unlock_gates", "is_unlocked_raw"],
    "characters": ["starting_hp", "starting_gold", "max_energy", "base_orb_slot_count", "starting_deck", "starting_relics",
                   "starting_potions", "card_pool", "relic_pool", "potion_pool", "is_playable", "all_characters_index"],
}
BATCHES = {
    "enchantments": OrderedDict([
        ("1", "enchantment core: EnchantmentModel on a CardInstance (Amount, Status, DeckVersion), the damage/block pre-pass, EnchantPlayCount, OnPlay after the card, OnEnchant keyword/cost edits, enchantments as listeners after their card"),
        ("2", "enchantments that call other kernel features: block/draw/energy facades with vars, turn-1 auto-play, temporary costs, deck-version writes"),
    ]),
    "afflictions": OrderedDict([("1", "affliction core: AfflictionModel on a CardInstance (Amount, stacking, CanAfflict); ships with the power that applies it")]),
    "orbs": OrderedDict([("1", "orb core: OrbQueue, the five orbs, Channel/Evoke/Passive, ModifyOrbValue (Focus), hook-counted passive triggers")]),
    "modifiers": OrderedDict([("1", "custom and daily run modifiers: outside the standard single-player scope; after the run layer")]),
    "acts": OrderedDict([("1", "standard acts with the W2 run layer: room sets, map point counts, ancient and unlock gates")]),
    "characters": OrderedDict([("1", "playable characters with the W2 run layer: starting deck, relics, HP, gold, energy, orb slots, pools")]),
}
SCOPES = {
    "game": "reachable in a standard single-player run",
    "test_only": "test mock (IsMock or a Mocks namespace)",
    "deprecated": "empty placeholder kept for save compatibility",
    "unreferenced": "registered but applied by no model in v0.111.0",
    "custom_modes_only": "only in custom or daily runs (run modifiers); excluded from the standard single-player scope",
    "not_playable": "not selectable as a run character",
}
TIER_LEGEND = OrderedDict([
    ("1", "only the kind's runtime (kind_baseline) and facades the P0 sim has"),
    ("2", "needs game facades, hooks, hook parameters or model APIs the P0 sim lacks"),
    ("3", "needs a player prompt or a new subsystem (orbs, Osty, stars, run layer, card generation, ...)"),
    ("4", "test-only or cross-cutting"),
])


def asc_of(text):
    toks = sorted(set(re.findall(r"\b(AscensionHelper|AscensionLevel|HasAscension|IsAscension)\b", text)))
    return "none" if not toks else "direct: " + ", ".join(toks)


def main():
    vdiff = common.load_json(common.work_path("minor-kinds", "vdiff.json"))
    for kind in KINDS:
        raw = common.load_json(common.inventory_path(kind + ".json"))
        entries = []
        for e in raw["entries"]:
            rec = lib.DATA.get((kind, e["class"]))
            if rec is None:
                raise SystemExit("no record for %s/%s" % (kind, e["class"]))
            out = OrderedDict((k, e.get(k)) for k in IDENTITY)
            out["scope"] = rec["scope"]
            for k in FACTS[kind]:
                out[k] = e.get(k)
            out["mechanics"] = rec["mechanics"]
            lines = e.get("override_lines") or {}
            out["hooks"] = [OrderedDict([("member", h), ("cite", "%s:%d" % (e["file"], lines[h]) if h in lines else None)])
                            for h in rec["hooks"]]
            out["prompts"] = rec["prompts"]
            out["phases"] = rec["phases"]
            out["kernel_requirements"] = rec["kernel_requirements"]
            out["kernel_requirement_notes"] = rec["kernel_requirement_notes"]
            out["dependencies"] = rec["dependencies"]
            out["rng_streams"] = rec["rng_streams"]
            src = open(os.path.join(common.DECOMPILE_ROOT, "MegaCrit.Sts2.Core." + e["file"]), encoding="utf-8-sig").read()
            out["ascension_sensitivity"] = asc_of(re.sub(r"//[^\n]*", "", src))
            vk = "%s/%s" % (kind, e["class"])
            mech = vdiff.get(vk, {})
            if rec["vd"]:
                status, note = rec["vd"]
            elif mech.get("status") == "same":
                status, note = "same", "token fingerprint identical (powers/vdiff.py method)"
            elif mech.get("status") == "new":
                status, note = "new", "not in v0.107.1"
            else:
                raise SystemExit("unclassified version difference: " + vk)
            vd = OrderedDict([("status", status), ("note", note), ("v107_file", mech.get("v107_file"))])
            if mech.get("diff"):
                vd["fingerprint_diff"] = mech["diff"]
            out["version_delta_v107"] = vd
            out["tier"] = rec["tier"]
            out["port_batch"] = rec["port_batch"]
            out["notes"] = rec["notes"]
            out["open_questions"] = rec["open_questions"]
            entries.append(out)
        env = OrderedDict()
        env["schema"] = "sts2sim.inventory.classified/v1"
        env["kind"] = kind
        env["game_version"] = raw["game_version"]
        env["raw_manifest"] = "sim/specs/inventory/%s.json" % kind
        env["raw_source_digest"] = raw["source_digest"]
        env["generated_by"] = common.generator("minor-kinds", "vdiff.py", "build.py", "data_%s.py" % kind)
        env["count"] = len(entries)
        env["scope_legend"] = SCOPES
        env["tier_legend"] = TIER_LEGEND
        env["port_batch_legend"] = BATCHES[kind]
        env["entries"] = entries
        with open(common.stage1_path(kind + ".classified.json"), "w", encoding="utf-8") as fh:
            json.dump(env, fh, indent=1, ensure_ascii=False)
            fh.write("\n")
        print(kind, len(entries))
    missing = sorted("%s/%s" % k for k in lib.DATA if k[0] in KINDS and not any(
        e["class"] == k[1] for e in common.load_json(common.inventory_path(k[0] + ".json"))["entries"]))
    if missing:
        raise SystemExit("records without a raw entry: %s" % missing)


if __name__ == "__main__":
    main()
