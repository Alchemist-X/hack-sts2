#!/usr/bin/env python3
"""Build a synthetic "new version" decompile tree: a copy of a real tree with known edits.

    python3 scripts/version_synthetic.py <src tree> <dest dir> [--no-hook-edit] [--force]

Used to exercise the version-update tooling end to end (scripts/version_update.sh, docs/version-migration-playbook.md)
when no second game build is at hand. The edits are the ones of docs/version-delta-demo-2026-11-01.md
("Synthetic pair") plus one recorder hook signature change:

  content  Models.Cards/Bash.cs              DamageVar 8 -> 9
  content  Models.Monsters/LeafSlimeM.cs     ClumpDamage (A9+ value) 9 -> 10
  content  Models.Relics/Anchor.cs           BlockVar 10 -> 12
  kernel   Commands/PowerCmd.cs              Apply<T>(single target) returns early on amount == 0
  kernel   Map/StandardActMap.cs             maxElites 15 -> 16
  hook     Runs/RunManager.cs                OnEnded(bool isVictory) -> OnEnded(bool isVictory, bool isAbandoned)
  added    Models.Cards/SyntheticBash.cs     a copy of Bash under a new class name (a NEW model: packet + skeleton)

Every edit must match exactly once in the source tree (flattened Linux v0.111.0 layout); the script fails otherwise.
The destination is written with a MANIFEST.txt listing the edits. Stdlib only.
"""

import argparse
import os
import re
import shutil
import sys

NS = "MegaCrit.Sts2.Core."
EDITS = [
    ("content", NS + "Models.Cards/Bash.cs", "new DamageVar(8m, ValueProp.Move)", "new DamageVar(9m, ValueProp.Move)"),
    ("content", NS + "Models.Monsters/LeafSlimeM.cs",
     "ClumpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 9, 8)",
     "ClumpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 8)"),
    ("content", NS + "Models.Relics/Anchor.cs", "new BlockVar(10m, ValueProp.Unpowered)",
     "new BlockVar(12m, ValueProp.Unpowered)"),
    ("kernel", NS + "Commands/PowerCmd.cs",
     "Task<T?> Apply<T>(PlayerChoiceContext choiceContext, Creature target, decimal amount, Creature? applier, "
     "CardModel? cardSource, bool silent = false) where T : PowerModel\n\t{\n",
     "Task<T?> Apply<T>(PlayerChoiceContext choiceContext, Creature target, decimal amount, Creature? applier, "
     "CardModel? cardSource, bool silent = false) where T : PowerModel\n\t{\n\t\tif (amount == 0m)\n\t\t{\n"
     "\t\t\treturn null;\n\t\t}\n"),
    ("kernel", NS + "Map/StandardActMap.cs", "public const int maxElites = 15;", "public const int maxElites = 16;"),
    ("hook", NS + "Runs/RunManager.cs", "public SerializableRun OnEnded(bool isVictory)\n",
     "public SerializableRun OnEnded(bool isVictory, bool isAbandoned)\n"),
]
# added content model: a copy of an existing class under a new name (exercises the NEW-model skeleton path)
ADDS = [("added", NS + "Models.Cards/Bash.cs", NS + "Models.Cards/SyntheticBash.cs", "Bash", "SyntheticBash")]


def build(src, dest, hook_edit=True, force=False):
    if os.path.exists(dest):
        if not force:
            raise SystemExit("version_synthetic: %s exists (use --force)" % dest)
        shutil.rmtree(dest)
    shutil.copytree(src, dest)
    applied = []
    for scope, rel, old, new in EDITS:
        if scope == "hook" and not hook_edit:
            continue
        path = os.path.join(dest, rel)
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        n = text.count(old)
        if n != 1:
            raise SystemExit("version_synthetic: %r matches %d times in %s (want 1)" % (old[:60], n, rel))
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text.replace(old, new))
        applied.append((scope, rel, old.split("\n")[0][:70], new.replace("\n", " ").replace("\t", "")[:90]))
    for scope, src_rel, dst_rel, old_cls, new_cls in ADDS:
        with open(os.path.join(src, src_rel), encoding="utf-8") as fh:
            text = fh.read()
        with open(os.path.join(dest, dst_rel), "w", encoding="utf-8") as fh:
            fh.write(re.sub(r"\b%s\b" % old_cls, new_cls, text))
        applied.append((scope, dst_rel, "(new file)", "copy of %s with class %s" % (src_rel, new_cls)))
    with open(os.path.join(dest, "MANIFEST.txt"), "w", encoding="utf-8") as fh:
        fh.write("synthetic tree from %s\n" % src)
        for a in applied:
            fh.write("%s\t%s\t%s\t-> %s\n" % a)
    return applied


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("src")
    ap.add_argument("dest")
    ap.add_argument("--no-hook-edit", action="store_true")
    ap.add_argument("--force", action="store_true", help="replace dest if it exists")
    a = ap.parse_args(argv)
    applied = build(a.src, a.dest, not a.no_hook_edit, a.force)
    print("version_synthetic: %s <- %s, %d edits" % (a.dest, a.src, len(applied)))
    for scope, rel, _, new in applied:
        print("  %-7s %s" % (scope, rel))
    return 0


if __name__ == "__main__":
    sys.exit(main())
