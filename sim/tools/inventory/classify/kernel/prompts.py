import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys, collections
D = common.DECOMPILE_ROOT
rows = []
for d in sorted(os.listdir(D)):
    if not (d.startswith("MegaCrit.Sts2.Core.Models") or d in ("MegaCrit.Sts2.Core.Entities.RestSite","MegaCrit.Sts2.Core.Rewards","MegaCrit.Sts2.Core.Runs","MegaCrit.Sts2.Core.Multiplayer.Game")):
        continue
    for f in sorted(os.listdir(os.path.join(D, d))):
        p = os.path.join(D, d, f)
        L = open(p, encoding="utf-8", errors="replace").read().split("\n")
        enclosing = None
        method = None
        for i, l in enumerate(L):
            m = re.match(r'^\t(?:private|public) (?:struct|sealed class) _003C(\w+?)_003E[a-z]__\d+', l)
            if m:
                enclosing = m.group(1)
            m2 = re.match(r'^\t(?:public|protected|private|internal) (?:static )?(?:override )?(?:async )?[^\(=]*? (\w+)\(', l)
            if m2:
                method = m2.group(1); enclosing = None
            for mm in re.finditer(r'CardSelectCmd\.(\w+)\(', l):
                ctx = None
                c = re.search(r'CardSelectCmd\.\w+\((new \w+PlayerChoiceContext\(\)|\w+)', l)
                if c: ctx = c.group(1)
                # prefs
                rows.append(dict(kind=d.replace("MegaCrit.Sts2.Core.", ""), file=f[:-3], line=i+1, method=mm.group(1), site=enclosing or method, ctx=ctx, text=l.strip()[:220]))
json.dump(rows, open(common.work_path("kernel", "prompts.json"), "w"), indent=1)
for r in rows:
    if "Mock" in r["kind"]: continue
    print(f'{r["kind"]:22} {r["file"]:24} {r["line"]:5} {r["method"]:28} site={r["site"]} ctx={r["ctx"]}')
