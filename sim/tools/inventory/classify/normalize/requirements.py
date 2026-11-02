"""Map W0 free-text kernel requirements onto catalogue keys (see catalog.py for the key forms)."""

import re

import catalog

VARIANT_KEYS = {
    "CreatureCmd.Damage(DamageVar)": ("CreatureCmd", "Damage", "DamageVar"),
    "CreatureCmd.GainBlock(BlockVar)": ("CreatureCmd", "GainBlock", "BlockVar"),
    "DamageCmd.Attack(CalculatedDamageVar)": ("DamageCmd", "Attack", "CalculatedDamageVar"),
    "CardCmd.Discard(list)": ("CardCmd", "Discard", "IEnumerable<CardModel>"),
    "CardCmd.Discard(single, Sly)": ("CardCmd", "Discard", "CardModel card"),
    "CardPileCmd.Add(CardPile|clonedBy)": ("CardPileCmd", "Add", "CardPile"),
}
THIN_ADAPTERS = {"CreatureCmd.Damage(DamageVar)", "CreatureCmd.GainBlock(BlockVar)"}
MODEL_BASES = ("CardModel", "PowerModel", "RelicModel", "PotionModel", "OrbModel", "EnchantmentModel", "AfflictionModel",
               "ModifierModel", "MonsterModel", "EncounterModel", "EventModel", "CharacterModel")
_RULES = [(re.compile(rx), keys) for rx, keys in catalog.RULES]
_PRESENT = re.compile(catalog.PRESENT)
_HOOK_CALL = re.compile(r"\bHook\.(\w+)")
_WORD = re.compile(r"\b[A-Z]\w+\b")
_MODEL = re.compile(r"\b(" + "|".join(MODEL_BASES) + r")\.(\w+)")


class Catalog(object):
    def __init__(self, kernel_gaps):
        self.kg = kernel_gaps
        self.facade_rows = {}
        for cls, rows in kernel_gaps["cmd_facades"].items():
            for r in rows:
                self.facade_rows.setdefault((cls, r["name"]), []).append(r)
        self.hooks = dict((h["name"], h) for h in kernel_gaps["hook_virtuals"])
        self.rule_hooks = set(n for n, h in self.hooks.items() if h["relevance"] not in ("infra", "ui"))
        self.dispatchers = dict((d["name"], d) for d in kernel_gaps["hook_dispatchers"])
        self.model_virtuals = {}
        for base, v in kernel_gaps["model_kind_virtuals"].items():
            for x in v["virtuals"]:
                self.model_virtuals[(base, x["name"])] = x
        cmd_classes = sorted(kernel_gaps["cmd_facades"], key=len, reverse=True)
        self._facade = re.compile(r"\b(" + "|".join(re.escape(c) for c in cmd_classes) + r")\.(\w+)")

    def key_kind(self, key):
        if key in catalog.FEATURES:
            return "feature"
        if key in VARIANT_KEYS:
            return "cmd_facade"
        if "." in key:
            a, b = key.split(".", 1)
            if (a, b) in self.facade_rows:
                return "cmd_facade"
            if (a, b) in self.model_virtuals:
                return "model_virtual"
            return None
        if key in self.hooks:
            return "hook_virtual"
        return None

    def keys_for(self, text):
        if text in catalog.OVERRIDES:
            return list(catalog.OVERRIDES[text])
        if _PRESENT.search(text):
            return []
        keys = []

        def add(k):
            if k not in keys:
                keys.append(k)

        for rx, ks in _RULES:
            if rx.search(text):
                for k in ks:
                    add(k)
        for m in self._facade.finditer(text):
            cls, meth = m.group(1), m.group(2)
            if (cls, meth) in self.facade_rows:
                add(cls + "." + meth)
        for m in _HOOK_CALL.finditer(text):
            name = m.group(1)
            if name in self.rule_hooks:
                add(name)
            elif name in self.dispatchers:
                for p in self.dispatchers[name]["passes"]:
                    if p in self.rule_hooks:
                        add(p)
        for w in _WORD.findall(text):
            if w in self.rule_hooks:
                add(w)
        for m in _MODEL.finditer(text):
            if (m.group(1), m.group(2)) in self.model_virtuals:
                add(m.group(1) + "." + m.group(2))
        return keys

    def describe(self, key):
        """Catalogue row for one key: kind, level, title, joins into the gap manifests, P0 status."""
        kind = self.key_kind(key)
        out = {"key": key, "kind": kind}
        if kind == "feature":
            f = catalog.FEATURES[key]
            out.update({"level": f["level"], "title": f["title"], "rows": f["rows"], "status": "missing" if f["level"] > 1 else "n/a"})
            return out
        if kind == "cmd_facade":
            if key in VARIANT_KEYS:
                cls, meth, ptype = VARIANT_KEYS[key]
                rows = [r for r in self.facade_rows[(cls, meth)] if ptype in r["params"]]
            else:
                cls, meth = key.split(".", 1)
                rows = self.facade_rows[(cls, meth)]
            statuses = sorted(set(r["status"] for r in rows))
            scope = sorted(set(r["scope"] for r in rows))
            status = statuses[0] if len(statuses) == 1 else "partial"
            level = 1 if status in ("ported", "strip") else (3 if any(s.startswith("run") for s in scope) else 2)
            if key in THIN_ADAPTERS:
                level = 1
            if cls in ("CardSelectCmd", "RelicSelectCmd") and status != "ported":
                level = 3
            if cls in ("OrbCmd", "OstyCmd", "ForgeCmd", "PotionCmd", "RewardsCmd", "MapCmd", "RelicCmd"):
                level = max(level, 3)
            out.update({"level": level, "title": "%s.%s facade" % (cls, meth) + (" (%s overloads)" % key.split("(", 1)[1][:-1] if "(" in key else ""),
                        "rows": [{"file": catalog.KG, "section": "cmd_facades", "name": "%s/%s@%d" % (cls, meth, r["line"])} for r in rows],
                        "status": status, "scope": scope})
            return out
        if kind == "hook_virtual":
            h = self.hooks[key]
            level = 1 if h["ported"] else 2
            out.update({"level": level, "title": "%s hook virtual (%s)" % (key, h["dispatch"]),
                        "rows": [{"file": catalog.KG, "section": "hook_virtuals", "name": key}],
                        "status": "ported" if h["ported"] else "missing", "scope": [h["relevance"]]})
            return out
        if kind == "model_virtual":
            base, name = key.split(".", 1)
            level = 3 if base in ("RelicModel", "EventModel", "EncounterModel", "ModifierModel", "CharacterModel") else 2
            out.update({"level": level, "title": "%s.%s model virtual" % (base, name),
                        "rows": [{"file": catalog.KG, "section": "model_kind_virtuals", "name": "%s/%s" % (base, name)}],
                        "status": "missing"})
            return out
        out.update({"level": None, "title": None, "rows": [], "status": None})
        return out
