import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys, collections
D = common.DECOMPILE_ROOT
AM = os.path.join(D, "MegaCrit.Sts2.Core.Models", "AbstractModel.cs")
lines = open(AM, encoding="utf-8").read().split("\n")
virt = []
i = 0
pat = re.compile(r'^\s*public virtual (.+?) (\w+)(\((.*)\))?\s*(=>.*)?$')
while i < len(lines):
    l = lines[i]
    m = pat.match(l)
    if m:
        ret, name, _, params, arrow = m.groups()
        body = []
        if arrow:
            body = [arrow]
        else:
            j = i + 1
            depth = 0
            while j < len(lines):
                body.append(lines[j].strip())
                depth += lines[j].count("{") - lines[j].count("}")
                if depth <= 0 and "}" in lines[j]:
                    break
                j += 1
        b = " ".join(x for x in body if x not in ("{", "}"))
        dflt = b
        mm = re.search(r'return (.*?);', b)
        if mm:
            dflt = mm.group(1)
        if arrow:
            dflt = arrow[2:].strip().rstrip(";")
        outs = re.findall(r'(\w+) = (.*?);', b)
        if ret.endswith("Task"):
            dflt = "CompletedTask"
        virt.append(dict(name=name, line=i+1, ret=ret.replace("System.Threading.Tasks.", "").replace("System.Collections.Generic.", ""),
                         params=(params or "").replace("System.Collections.Generic.", ""), default=dflt,
                         outs=[f"{a}={c}" for a, c in outs if a not in ("return",)]))
    i += 1
names = [v["name"] for v in virt]
dups = [n for n, c in collections.Counter(names).items() if c > 1]
SIM_AM = common.sim_path("Sts2Sim.Core/Combat/Pipeline/AbstractModel.cs")
simsrc = open(SIM_AM).read()
simnames = set(re.findall(r'public virtual [^\(]*? (\w+)\(', simsrc))
kinds = {}
for d in sorted(os.listdir(D)):
    if d.startswith("MegaCrit.Sts2.Core.Models"):
        k = d.replace("MegaCrit.Sts2.Core.Models", "").lstrip(".") or "(base)"
        kinds[k] = os.path.join(D, d)
ovr = collections.defaultdict(lambda: collections.Counter())
ovr_files = collections.defaultdict(lambda: collections.defaultdict(list))
for k, p in kinds.items():
    for f in os.listdir(p):
        if not f.endswith(".cs"):
            continue
        src = open(os.path.join(p, f), encoding="utf-8", errors="replace").read()
        for n in set(re.findall(r'public (?:sealed )?override (?:async )?[^\(=]*? (\w+)\(', src)):
            ovr[n][k] += 1
            ovr_files[n][k].append(f[:-3])
out = []
for v in virt:
    n = v["name"]
    c = ovr.get(n, {})
    v["ported"] = n in simnames
    v["overrides"] = dict(sorted(c.items()))
    v["override_total_content"] = sum(x for kk, x in c.items() if "Mock" not in kk and kk != "(base)")
    out.append(v)
json.dump(dict(virtuals=out, dups=dups, sim_only=sorted(simnames - set(names))), open(common.work_path("kernel", "virtuals.json"), "w"), indent=1)
json.dump({n: {k: sorted(v) for k, v in d.items()} for n, d in ovr_files.items()}, open(common.work_path("kernel", "override_files.json"), "w"), indent=0)
print(len(virt), "virtuals; ported", sum(v["ported"] for v in out), "; dups", dups, "; sim-only", sorted(simnames - set(names)))
