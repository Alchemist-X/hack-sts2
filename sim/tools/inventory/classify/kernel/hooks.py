import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys, collections
D = common.DECOMPILE_ROOT
H = os.path.join(D, "MegaCrit.Sts2.Core.Hooks", "Hook.cs")
L = open(H, encoding="utf-8").read().split("\n")
virt = json.load(open(common.work_path("kernel", "virtuals.json")))["virtuals"]
vnames = set(v["name"] for v in virt)
# struct ranges
structs = {}
i = 0
while i < len(L):
    m = re.match(r'^\tprivate (?:struct|sealed class) (_003C(\w+?)_003Ed__\d+)', L[i])
    if m:
        name = m.group(1)
        depth = 0
        j = i + 1
        started = False
        while j < len(L):
            depth += L[j].count("{") - L[j].count("}")
            if "{" in L[j]:
                started = True
            if started and depth == 0:
                break
            j += 1
        structs[name] = (i + 1, j + 1)
        i = j
    i += 1
def body_of(start):
    depth = 0; j = start; started = False
    while j < len(L):
        depth += L[j].count("{") - L[j].count("}")
        if "{" in L[j]: started = True
        if started and depth == 0: break
        j += 1
    return start, j
SRC_PAT = [
    (r'IterateCombatHookListeners\(combatState\)', "Guarded"),
    (r'(?<![\w.])combatState\.IterateHookListeners\(\)', "Combat"),
    (r'power\.CombatState\.IterateHookListeners\(\)', "Combat(power.CombatState)"),
    (r'runState\.IterateHookListeners\(combatState\)', "Run+Combat"),
    (r'runState\.IterateHookListeners\(null\)', "RunOnly"),
]
simsrc = open(common.sim_path("Sts2Sim.Core/Combat/Pipeline/Hook.cs")).read()
simnames = set(re.findall(r'public static [^\(]*? (\w+)\(', simsrc))
rows = []
for i, l in enumerate(L):
    m = re.match(r'^\tpublic static (?:async )?(.+?) (\w+)\((.*)\)\s*$', l)
    if not m:
        continue
    ret, name, params = m.groups()
    attr = L[i-1] if i > 0 else ""
    sm = re.search(r'typeof\((\w+)\)', attr)
    if sm and sm.group(1) in structs:
        a, b = structs[sm.group(1)]
        kind = "async"
    else:
        a, b = body_of(i + 1)
        kind = "sync"
    body = "\n".join(L[a-1:b])
    srcs = []
    for pat, lab in SRC_PAT:
        for mm in re.finditer(pat, body):
            srcs.append((mm.start(), lab))
    srcs = [lab for _, lab in sorted(srcs)]
    calls = []
    for mm in re.finditer(r'\.(\w+)\(', body):
        n = mm.group(1)
        if n in vnames and n not in calls:
            calls.append(n)
    flags = []
    if "LocalContext.NetId" in body: flags.append("NetId gate")
    if "new HookPlayerChoiceContext" in body: flags.append("per-listener new HookPlayerChoiceContext + WhenAll (pause-deferral)")
    if "PushModel" in body: flags.append("PushModel/PopModel on caller ctx")
    if "PlayerChoiceContext" in params: flags.append("ctx param")
    if re.search(r'Enumerable\.Contains<AbstractModel>\(\w*modifiers|modifiers\)\.Contains|\.Contains\(_003Cmodel|Contains<AbstractModel>', body): flags.append("modifier-filtered")
    if re.search(r'== preventer|preventer ==|== modifier\b|modifier ==', body): flags.append("only-preventer")
    if "Enchantment" in body: flags.append("enchantment pre-pass")
    outs = re.findall(r'(out|ref) ([\w\.<>\?\[\], ]+?) (\w+)(?=,|$)', params)
    rows.append(dict(name=name, line=i+1, ret=ret.replace("System.Threading.Tasks.", "").replace("System.Collections.Generic.", ""),
                     params=params.replace("System.Collections.Generic.", ""), kind=kind, body=[a, b], sources=srcs, calls=calls,
                     flags=flags, outs=[f"{o} {t} {n}" for o, t, n in outs], ported=name in simnames))
json.dump(rows, open(common.work_path("kernel", "hooks.json"), "w"), indent=1)
print(len(rows), "dispatchers; ported", sum(r["ported"] for r in rows))
for r in rows:
    print(f'{r["line"]:6} {"Y" if r["ported"] else "-"} {r["kind"]:5} {r["name"]:42} src={",".join(r["sources"])} calls={",".join(r["calls"])} flags={";".join(r["flags"])} outs={r["outs"]}')
