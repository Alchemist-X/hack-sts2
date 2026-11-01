#!/usr/bin/env python3
"""Print N random manifest entries per kind next to the source lines they cite.

    python3 sim/tools/inventory/spotcheck.py --kind cards -n 10 --seed 7

For eyeball review only; prints, writes nothing.
"""

import argparse
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEFAULT_ROOT = "/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled"
NS_PREFIX = "MegaCrit.Sts2.Core."

KEY_FIELDS = {
    "cards": ["pool", "rarity", "type", "cost", "costs_x", "star_cost", "target_type", "keywords", "tags",
              "dynamic_vars", "upgrade", "needs_player_choice", "choice_tokens", "powers_referenced",
              "cards_referenced", "lines_logic", "field_sources", "dynamic_fields"],
    "powers": ["power_type", "stack_type", "instance_type", "allow_negative", "defaulted", "dynamic_fields",
               "abstract_model_hooks", "dynamic_vars", "constants", "referenced_by", "lines_logic"],
    "relics": ["rarity", "pool", "pools", "character", "starting_relic_of", "abstract_model_hooks", "hook_scopes",
               "combat_relevant", "run_relevant", "run_members", "needs_player_choice", "dynamic_vars",
               "lines_logic"],
    "potions": ["rarity", "usage", "usable_in_combat", "usable_out_of_combat", "target_type", "pool", "pools",
                "api_calls", "needs_player_choice", "dynamic_vars", "lines_logic"],
    "monsters": ["min_hp_raw", "max_hp_raw", "min_hp", "max_hp", "ascension_branches", "uses_private_rng",
                 "run_rng_streams", "powers_referenced", "move_states", "encounters", "lines_logic"],
    "encounters": ["room_type", "is_weak", "kind", "act", "act_index", "monsters", "slots", "tags",
                   "uses_encounter_rng", "encounter_rng_calls", "min_gold_reward", "max_gold_reward"],
    "events": ["is_ancient", "acts", "shared_pool", "pool_status", "options_count", "option_keys",
               "relic_options", "rng_usage", "encounters_referenced", "cards_referenced", "relics_referenced",
               "potions_referenced", "api_calls"],
    "acts": ["index", "is_default", "number_of_weak_encounters", "base_number_of_rooms", "encounters_weak",
             "encounters_normal", "encounters_elite", "encounters_boss", "boss_discovery_order", "ancients",
             "events", "map_params"],
    "characters": ["starting_hp", "starting_gold", "max_energy", "base_orb_slot_count", "starting_deck",
                   "starting_relics", "starting_potions", "card_pool", "relic_pool", "potion_pool", "is_playable"],
}
GENERIC = ["hooks_overridden", "protected_overrides", "api_calls", "lines_total", "lines_logic", "dynamic_vars"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.environ.get("STS2_DECOMPILE_ROOT", DEFAULT_ROOT))
    ap.add_argument("--inventory", default=os.path.join(REPO, "sim", "specs", "inventory"))
    ap.add_argument("--kind", required=True)
    ap.add_argument("-n", type=int, default=10)
    ap.add_argument("--seed", type=int, default=20261101)
    ap.add_argument("--class", dest="classes", action="append", help="pick these classes instead of random ones")
    ap.add_argument("--context", type=int, default=0, help="extra source lines around each cited line")
    ap.add_argument("--no-source", action="store_true")
    args = ap.parse_args()
    with open(os.path.join(args.inventory, args.kind + ".json"), encoding="utf-8") as fh:
        data = json.load(fh)
    entries = data["entries"]
    if args.classes:
        picked = [e for e in entries if e["class"] in args.classes]
    else:
        rnd = random.Random(args.seed)
        picked = rnd.sample(entries, min(args.n, len(entries)))
    fields = KEY_FIELDS.get(args.kind, GENERIC)
    for e in picked:
        print("=" * 100)
        print("%s  %s  %s:%d  lines=%d" % (e["id"], e["class"], e["file"], e["class_line"], e["lines_total"]))
        for k in fields:
            print("  %-22s %s" % (k, json.dumps(e.get(k), ensure_ascii=False)))
        if args.no_source:
            continue
        path = os.path.join(args.root, NS_PREFIX + e["file"].split("/")[0], e["file"].split("/")[1])
        with open(path, encoding="utf-8-sig", errors="replace") as fh:
            lines = fh.read().split("\n")
        cited = set()
        for ln in (e.get("override_lines") or {}).values():
            cited.add(ln)
        if e.get("ctor_line"):
            cited.update([e["ctor_line"], e["ctor_line"] + 1])
        for d in e.get("dynamic_var_details") or []:
            cited.add(d["line"])
        print("  -- cited source lines")
        for ln in sorted(cited):
            for k in range(max(1, ln - args.context), min(len(lines), ln + args.context) + 1):
                text = lines[k - 1].strip()
                if text and not text.startswith("//"):
                    print("  %5d| %s" % (k, text[:200]))


if __name__ == "__main__":
    sys.exit(main())
