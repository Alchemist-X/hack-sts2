"""Dependencies of a classified entry, as (kind, id) pairs over the whole inventory."""

KIND_OF_FILE = {
    "cards-ironclad-colorless": "cards", "cards-necrobinder-regent": "cards", "cards-silent-defect": "cards",
    "powers": "powers", "relics": "relics", "potions": "potions", "monsters": "monsters", "encounters": "encounters",
    "events": "events", "enchantments": "enchantments", "afflictions": "afflictions", "orbs": "orbs",
    "modifiers": "modifiers", "acts": "acts", "characters": "characters",
}
CONTENT_KINDS = ["cards", "powers", "relics", "potions", "monsters", "encounters", "events", "enchantments", "afflictions",
                 "orbs", "modifiers", "acts", "characters"]


class Index(object):
    """class name -> entry id per kind, from the raw manifests (the join key of every classified file)."""

    def __init__(self, raw):
        self.by_class = {}
        self.ids = {}
        for kind in CONTENT_KINDS:
            entries = raw[kind]["entries"]
            self.by_class[kind] = dict((e["class"], e["id"]) for e in entries)
            self.ids[kind] = set(e["id"] for e in entries)

    def resolve(self, kind, name):
        if name is None:
            return None
        if name in self.ids.get(kind, ()):
            return name
        return self.by_class.get(kind, {}).get(name)


REQUIRES, READS, CO_PORT = "requires", "reads", "co_port"
POWER_REQUIRE_MODES = {"apply", "afflict", "channel", "create", "spawn", "modeldb", "new", "inherit"}


def _add(out, kind, ident, rel=REQUIRES):
    if not ident:
        return
    for i, (k, d, r) in enumerate(out):
        if (k, d) == (kind, ident):
            if r != REQUIRES and rel == REQUIRES:
                out[i] = (k, d, REQUIRES)
            return
    out.append((kind, ident, rel))


def dependencies(fname, entry, index):
    """(kind, id, relation) for every model this entry's own code needs.

    requires: applied, created, summoned or offered models (they must be ported first);
    reads: models only read or type-tested; co_port: definitions that call each other (port together).
    """
    out = []
    r = index.resolve
    if fname == "cards-ironclad-colorless":
        for p in entry["depends_on"]["powers"]:
            _add(out, "powers", r("powers", p["id"]))
        for c in entry["depends_on"]["cards"]:
            _add(out, "cards", r("cards", c["id"]))
    elif fname == "cards-necrobinder-regent":
        for p in entry["depends_on"]["powers"]:
            _add(out, "powers", r("powers", p))
        for c in entry["depends_on"]["cards"]:
            _add(out, "cards", r("cards", c))
    elif fname == "cards-silent-defect":
        d = entry["depends_on"]
        for p in d["powers"]:
            _add(out, "powers", r("powers", p["power"]))
        for c in d["cards"]:
            _add(out, "cards", r("cards", c["card"]))
        for o in d["orbs"]:
            _add(out, "orbs", r("orbs", o))
        for x in d["enchantments"]:
            _add(out, "enchantments", r("enchantments", x))
    elif fname == "powers":
        for kind, names in sorted(entry["depends_on"].items()):
            for n in sorted(names):
                modes = set(names[n])
                if kind == "monsters":
                    rel = CO_PORT
                elif modes & POWER_REQUIRE_MODES:
                    rel = REQUIRES
                else:
                    rel = READS
                _add(out, kind, r(kind, n), rel)
        for n in entry["manual_deps"]:
            _add(out, "powers", r("powers", n))
        if entry.get("base_class"):
            _add(out, "powers", r("powers", entry["base_class"]))
    elif fname in ("relics", "potions"):
        for p in entry["powers_applied"]:
            _add(out, "powers", r("powers", p["power"]))
        for p in entry["powers_referenced"]:
            _add(out, "powers", r("powers", p["power"]), READS)
        for c in entry["cards_referenced"]:
            _add(out, "cards", r("cards", c["card"]))
    elif fname == "monsters":
        d = entry["deps"]
        for p in d["powers"]:
            _add(out, "powers", r("powers", p["power"]))
        for kind in ("cards", "monsters", "afflictions", "enchantments"):
            for n in d.get(kind, []):
                _add(out, kind, r(kind, n))
    elif fname == "encounters":
        for n in entry["monsters"] + entry.get("summoned_during_combat", []):
            _add(out, "monsters", r("monsters", n))
    elif fname == "events":
        for kind, names in sorted(entry["content_deps"].items()):
            for n in names:
                _add(out, kind, r(kind, n))
    else:
        rels = entry.get("dependencies") or {}
        for rel in (REQUIRES, READS, CO_PORT):
            for kind, names in sorted((rels.get(rel) or {}).items()):
                for n in names:
                    _add(out, kind, r(kind, n), rel)
    me = (KIND_OF_FILE[fname], entry["id"])
    return [x for x in out if (x[0], x[1]) != me]
