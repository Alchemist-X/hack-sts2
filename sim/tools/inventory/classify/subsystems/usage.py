"""Content usage index: which Models.* files match each named regex of spec1.json / spec2.json."""

import json
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import common  # noqa: E402

CATS = ['Models.Cards', 'Models.Powers', 'Models.Relics', 'Models.Potions', 'Models.Monsters', 'Models.Encounters',
        'Models.Events', 'Models.Enchantments', 'Models.Afflictions', 'Models.Orbs', 'Models.Modifiers',
        'Models.Characters', 'Models.Acts', 'Models.Badges']


def scan(root, spec):
    files = {}
    for c in CATS:
        d = os.path.join(root, 'MegaCrit.Sts2.Core.' + c)
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            if f.endswith('.cs'):
                files.setdefault(c, []).append((f[:-3], open(os.path.join(d, f), encoding='utf-8', errors='replace').read()))
    res = {}
    for name, pat in spec.items():
        rx = re.compile(pat)
        per = {}
        for c, lst in files.items():
            hits = [n for n, s in lst if rx.search(s)]
            if hits:
                per[c] = hits
        res[name] = per
    return res


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    names = common.REST or ["spec1.json", "spec2.json"]
    for name in names:
        spec = json.load(open(os.path.join(here, name)))
        res = scan(common.DECOMPILE_ROOT, spec)
        for key, per in res.items():
            tot = sum(len(v) for v in per.values())
            print('## %s: total %d files' % (key, tot))
        json.dump(res, open(common.work_path("subsystems", name + ".out.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
