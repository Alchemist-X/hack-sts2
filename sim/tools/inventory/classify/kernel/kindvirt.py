import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys, collections
D = common.DECOMPILE_ROOT
BASES = {"CardModel": ["Cards"], "PowerModel": ["Powers"], "RelicModel": ["Relics"], "PotionModel": ["Potions"], "OrbModel": ["Orbs"],
         "EnchantmentModel": ["Enchantments"], "AfflictionModel": ["Afflictions"], "ModifierModel": ["Modifiers"], "MonsterModel": ["Monsters"],
         "EncounterModel": ["Encounters"], "EventModel": ["Events"], "CharacterModel": ["Characters"]}
out = {}
for base, kinds in BASES.items():
    p = os.path.join(D, "MegaCrit.Sts2.Core.Models", base + ".cs")
    L = open(p, encoding="utf-8").read().split("\n")
    vs = []
    for i, l in enumerate(L):
        m = re.match(r'^\t(public|protected|internal|protected internal) (virtual|abstract) (?!class)(.+?) (\w+)(\((.*)\))?\s*(\{|=>.*|;)?\s*$', l)
        if m:
            vis, va, ret, name, _, params, tail = m.groups()
            vs.append(dict(name=name, vis=vis, kind=va, ret=ret.replace("System.Threading.Tasks.", "").replace("System.Collections.Generic.", ""),
                           params=(params or "").replace("System.Collections.Generic.", ""), line=i+1, prop=params is None))
    counts = collections.Counter()
    for k in kinds:
        d = os.path.join(D, "MegaCrit.Sts2.Core.Models." + k)
        for f in os.listdir(d):
            s = open(os.path.join(d, f), encoding="utf-8", errors="replace").read()
            for n in set(re.findall(r'(?:public|protected|internal) (?:sealed )?override (?:async )?[^\(=;{]*? (\w+)\s*(?:\(|=>|\{|$)', s, re.M)):
                counts[n] += 1
    for v in vs:
        v["overrides"] = counts.get(v["name"], 0)
    nfiles = sum(len(os.listdir(os.path.join(D, "MegaCrit.Sts2.Core.Models." + k))) for k in kinds)
    out[base] = dict(files=nfiles, virtuals=vs)
json.dump(out, open(common.work_path("kernel", "kindvirt.json"), "w"), indent=1)
for base, d in out.items():
    vs = sorted(d["virtuals"], key=lambda v: -v["overrides"])
    print(f"== {base} files={d['files']} virtuals={len(vs)}")
    print("   " + "; ".join(f'{v["name"]}{"" if v["prop"] else "()"}:{v["overrides"]}@{v["line"]}' for v in vs if v["overrides"] > 0))
    print("   zero: " + ", ".join(v["name"] for v in vs if v["overrides"] == 0))
