import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys, collections
D = common.DECOMPILE_ROOT
CMDS = ["CardPileCmd","CardCmd","CardSelectCmd","CreatureCmd","PowerCmd","PlayerCmd","DamageCmd","OrbCmd","OstyCmd","PotionCmd","RelicCmd","RelicSelectCmd","ForgeCmd","Cmd","RewardsCmd","MapCmd","VfxCmd","SfxCmd","TalkCmd","ThinkCmd"]
sig = re.compile(r"^\tpublic static (?:async )?(.+?) (\w+)(<[^>(]*>)?\((.*)\)\s*(?:where .*)?$")
res = {}
for c in CMDS:
    p = os.path.join(D, "MegaCrit.Sts2.Core.Commands", c + ".cs")
    L = open(p, encoding="utf-8").read().split("\n")
    ms = []
    for i, l in enumerate(L):
        m = sig.match(l)
        if m:
            ret, name, gen, params = m.groups()
            ret = ret.replace("System.Threading.Tasks.", "").replace("System.Collections.Generic.", "")
            params = params.replace("System.Collections.Generic.", "")
            ms.append(dict(name=name, generic=gen or "", ret=ret, params=params, line=i+1))
    res[c] = ms
# AttackCommand builder public methods
p = os.path.join(D, "MegaCrit.Sts2.Core.Commands.Builders", "AttackCommand.cs")
L = open(p, encoding="utf-8").read().split("\n")
ms = []
for i, l in enumerate(L):
    m = re.match(r'^\tpublic (?:async )?(?!static)(.+?) (\w+)(<[^>(]*>)?\((.*)\)\s*$', l)
    if m:
        ret, name, gen, params = m.groups()
        ms.append(dict(name=name, generic=gen or "", ret=ret.replace("System.Threading.Tasks.", ""), params=params.replace("System.Collections.Generic.", ""), line=i+1))
res["AttackCommand"] = ms
# usage counts across Models.* by kind
srcs = {}
for d in sorted(os.listdir(D)):
    if d.startswith("MegaCrit.Sts2.Core.Models") or d in ("MegaCrit.Sts2.Core.Combat","MegaCrit.Sts2.Core.Hooks","MegaCrit.Sts2.Core.GameActions","MegaCrit.Sts2.Core.Entities.Players","MegaCrit.Sts2.Core.Entities.Cards","MegaCrit.Sts2.Core.Entities.Creatures","MegaCrit.Sts2.Core.Entities.Orbs","MegaCrit.Sts2.Core.Commands","MegaCrit.Sts2.Core.Commands.Builders","MegaCrit.Sts2.Core.Runs","MegaCrit.Sts2.Core.Rewards","MegaCrit.Sts2.Core.Entities.RestSite","MegaCrit.Sts2.Core.Entities.Merchant","MegaCrit.Sts2.Core.Rooms","MegaCrit.Sts2.Core.Events"):
        for f in os.listdir(os.path.join(D, d)):
            if f.endswith(".cs"):
                srcs[(d.replace("MegaCrit.Sts2.Core.", ""), f)] = open(os.path.join(D, d, f), encoding="utf-8", errors="replace").read()
def kind(d):
    if d.startswith("Models."):
        return d[len("Models."):]
    if d == "Models": return "Models(base)"
    return "core:" + d
# sim names
simfiles = {
 "CardPileCmd": "sim/Sts2Sim.Core/Combat/Loop/CardPileCmd.cs",
 "PlayerCmd": "sim/Sts2Sim.Core/Combat/Loop/PlayerCmd.cs",
 "CreatureCmd": "sim/Sts2Sim.Core/Combat/Pipeline/CreatureCmd.cs",
 "PowerCmd": "sim/Sts2Sim.Core/Combat/Pipeline/PowerCmd.cs",
 "DamageCmd": "sim/Sts2Sim.Core/Combat/Pipeline/AttackCommand.cs",
 "AttackCommand": "sim/Sts2Sim.Core/Combat/Pipeline/AttackCommand.cs",
}
energy = open(common.sim_path("Sts2Sim.Core/Combat/Pipeline/EnergyCmd.cs")).read()
simnames = {}
for c, f in simfiles.items():
    s = open(common.sim_path(f[len("sim/"):])).read()
    simnames[c] = set(re.findall(r'(?:public|internal) (?:static )?[A-Za-z<>\?\[\], ]+ (\w+)(?:<[^>]*>)?\(', s))
simnames["PlayerCmd"] |= set(re.findall(r'(?:public|internal) static [A-Za-z<>\?\[\], ]+ (\w+)\(', energy))
out = {}
for c, ms in res.items():
    names = sorted(set(m["name"] for m in ms))
    rows = []
    for m in ms:
        n = m["name"]
        pat = re.compile(r'\b' + (c if c != "AttackCommand" else r'') + (r'\.' if c != "AttackCommand" else r'\.') + n + r'\b' + r'\s*(<[^>(]*>)?\(')
        cnt = collections.Counter()
        for (d, f), s in srcs.items():
            if c == "AttackCommand":
                k = len(re.findall(r'\.' + n + r'(<[^>(]*>)?\(', s)) if ("DamageCmd.Attack" in s or "AttackCommand" in s) else 0
            else:
                k = len(re.findall(r'\b' + c + r'\.' + n + r'(<[^>(]*>)?\(', s))
            if k:
                cnt[kind(d)] += k
        m2 = dict(m)
        m2["uses"] = dict(sorted(cnt.items()))
        m2["uses_models"] = sum(v for k, v in cnt.items() if not k.startswith("core:") and "Mock" not in k)
        m2["ported"] = n in simnames.get(c, set())
        rows.append(m2)
    out[c] = rows
json.dump(out, open(common.work_path("kernel", "cmds.json"), "w"), indent=1)
for c, rows in out.items():
    print("==", c, len(rows))
    for r in rows:
        print(f'  {r["line"]:5} {"Y" if r["ported"] else "-"} {r["name"]}{r["generic"]}({r["params"][:110]}) -> {r["ret"][:40]}  models={r["uses_models"]} {r["uses"]}')
