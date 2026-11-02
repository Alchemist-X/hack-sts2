"""Mechanical v0.107.1 vs v0.111.0 fingerprint diff of the six minor kinds (reuses powers/vdiff.py's fingerprint)."""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import common  # noqa: E402

import importlib.util  # noqa: E402

_spec = importlib.util.spec_from_file_location("powers_vdiff", os.path.join(HERE, "..", "powers", "vdiff.py"))
pv = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pv)

DIRS = [("enchantments", "Models.Enchantments", "Models/Enchantments"),
        ("enchantments", "Models.Enchantments.Mocks", "Models/Enchantments/Mocks"),
        ("afflictions", "Models.Afflictions", "Models/Afflictions"),
        ("afflictions", "Models.Afflictions.Mocks", "Models/Afflictions/Mocks"),
        ("orbs", "Models.Orbs", "Models/Orbs"), ("orbs", "Models.Orbs.Mock", "Models/Orbs/Mock"),
        ("modifiers", "Models.Modifiers", "Models/Modifiers"), ("acts", "Models.Acts", "Models/Acts"),
        ("characters", "Models.Characters", "Models/Characters")]


def main():
    out = {}
    for kind, d111, d107 in DIRS:
        p111 = os.path.join(common.DECOMPILE_ROOT, "MegaCrit.Sts2.Core." + d111)
        p107 = os.path.join(common.V107_ROOT, d107)
        for fn in sorted(os.listdir(p111)):
            if not fn.endswith(".cs"):
                continue
            rel111 = "%s/%s" % (d111, fn)
            old = os.path.join(p107, fn)
            rec = {"kind": kind, "file": rel111, "v107_file": ("%s/%s" % (d107, fn)) if os.path.exists(old) else None}
            if not os.path.exists(old):
                rec["status"] = "new"
            else:
                diff = pv.compare(pv.fingerprint(os.path.join(p111, fn)), pv.fingerprint(old))
                rec["status"] = "differs" if diff else "same"
                rec["diff"] = diff
            out["%s/%s" % (kind, fn[:-3])] = rec
        if os.path.isdir(p107):
            for fn in sorted(os.listdir(p107)):
                if fn.endswith(".cs") and not os.path.exists(os.path.join(p111, fn)):
                    out["%s/%s" % (kind, fn[:-3])] = {"kind": kind, "file": None, "v107_file": "%s/%s" % (d107, fn), "status": "removed"}
    with open(common.work_path("minor-kinds", "vdiff.json"), "w") as fh:
        json.dump(out, fh, indent=1, sort_keys=True)


if __name__ == "__main__":
    main()
