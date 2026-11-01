"""Unify the W0 prompt records onto the ChoicePromptKind enum of sim/CONTRACTS.md Part B section B1.

A prompt record (`prompts`) is a selection screen awaited inside an effect; everything else the old
records called a prompt (rewards screen, rest-site option, event combat, minigames, shop) is a phase
record (`phases`), following the prompt-vs-phase rule of B1.
"""

import re

PROMPT_KINDS = ["HandSelect", "HandUpgrade", "PileSelect", "ChooseCard", "SimpleGrid", "DeckUpgrade", "DeckTransform",
                "DeckEnchant", "DeckRemove", "DeckGeneric", "RewardsGrid", "Bundle", "CardReward"]
ENTRY_KIND = {
    "FromHand": "HandSelect", "FromHandForDiscard": "HandSelect", "FromHandForUpgrade": "HandUpgrade",
    "FromCombatPile": "PileSelect", "FromChooseACardScreen": "ChooseCard", "FromSimpleGrid": "SimpleGrid",
    "FromDeckForUpgrade": "DeckUpgrade", "FromDeckForTransformation": "DeckTransform",
    "FromDeckForEnchantment": "DeckEnchant", "FromDeckForRemoval": "DeckRemove", "FromDeckGeneric": "DeckGeneric",
    "FromSimpleGridForRewards": "RewardsGrid", "FromChooseABundleScreen": "Bundle",
}
TURN_SETUP_SITES = ("AfterPlayerTurnStart", "BeforeHandDraw")
PROMPT_FIELDS = ["kind", "entry", "site", "opened_by", "via", "pile", "min", "max", "require_confirm", "can_skip",
                 "cancelable", "context", "filter", "cite", "notes"]
PHASE_FIELDS = ["phase", "role", "entry", "site", "opened_by", "via", "detail", "cite"]


def _num(v):
    if isinstance(v, str) and re.match(r"^-?\d+$", v.strip()):
        return int(v)
    return v


def _entry_method(entry):
    m = re.search(r"CardSelectCmd\.(\w+)", entry or "")
    return m.group(1) if m else None


def _pile(p):
    if p is None:
        return None
    s = str(p)
    for word in ("Hand", "Draw", "Discard", "Exhaust", "Deck", "generated"):
        if s.lower().startswith(word.lower()):
            return word
    if "generated" in s or "cards" in s.lower():
        return "generated"
    return s


def _require_confirm(kind, mn, mx, explicit=None):
    if explicit is not None:
        return bool(explicit)
    if kind in ("ChooseCard", "HandUpgrade", "Bundle", "CardReward"):
        return False
    if isinstance(mn, int) and isinstance(mx, int):
        return mn >= 0 and mn != mx
    return None


def _context(method):
    if method in TURN_SETUP_SITES:
        return "TurnSetup"
    if method == "AfterShuffle":
        return "Inline|TurnSetup"
    return "Inline"


def _join(*parts):
    out = [str(p) for p in parts if p not in (None, "", [], {})]
    return "; ".join(out) if out else None


def make_prompt(kind, entry, site, opened_by, pile=None, mn=None, mx=None, require_confirm=None, can_skip=False,
                cancelable=False, filt=None, cite=None, notes=None, via=None, method=None):
    mn, mx = _num(mn), _num(mx)
    rec = {
        "kind": kind, "entry": entry, "site": site, "opened_by": opened_by, "via": via, "pile": _pile(pile),
        "min": mn, "max": mx, "require_confirm": _require_confirm(kind, mn, mx, require_confirm),
        "can_skip": bool(can_skip), "cancelable": bool(cancelable),
        "context": _context(method or (site.split(":", 1)[1] if site and ":" in site else None)),
        "filter": filt, "cite": cite, "notes": notes,
    }
    return dict((k, rec[k]) for k in PROMPT_FIELDS)


def make_phase(phase, role, entry, site, opened_by, detail=None, cite=None, via=None):
    rec = {"phase": phase, "role": role, "entry": entry, "site": site, "opened_by": opened_by, "via": via,
           "detail": detail, "cite": cite}
    return dict((k, rec[k]) for k in PHASE_FIELDS)


def _flag(flags, name):
    m = re.search(name + r"\s*=\s*(true|false)", flags or "", re.I)
    return None if not m else m.group(1).lower() == "true"


def unify(fname, entry, ids, raw_entry):
    """(prompts, phases, nested) for one entry of a W0 classified file.

    ids: power class -> power id; raw_entry: the entry's raw manifest row (override lines for citations).
    """
    me = entry["id"]
    lines = (raw_entry or {}).get("override_lines") or {}

    def cite_of(method):
        return "%s:%d" % (raw_entry["file"], lines[method]) if raw_entry and method in lines else None

    kind_of_file = {"cards-ironclad-colorless": "cards", "cards-necrobinder-regent": "cards",
                    "cards-silent-defect": "cards"}.get(fname, fname)
    self_ref = "%s:%s" % (kind_of_file, me)
    prompts, phases, nested = [], [], []
    for q in entry.get("prompts") or []:
        if fname == "cards-ironclad-colorless":
            if "monster prompt" in q["site"]:
                prompts.append(make_prompt(q["type"], q["cmd"], "KNOWLEDGE_DEMON:ChooseCurse", "monsters:KNOWLEDGE_DEMON",
                                           q["pile"], q["min"], q["max"], can_skip=q["can_skip"], filt=q["filter"],
                                           notes=_join(q.get("when"), "offered as one of %s options" % q.get("options")),
                                           via="monsters:KNOWLEDGE_DEMON", method="ChooseCurse",
                                           cite="Models.Monsters/KnowledgeDemon.cs:124"))
                continue
            rc = True if "RequireManualConfirmation" in (q.get("when") or "") else None
            prompts.append(make_prompt(q["type"], q["cmd"], "%s:%s" % (me, q["site"]), self_ref, q["pile"], q["min"], q["max"],
                                       require_confirm=rc, can_skip=q["can_skip"], filt=q["filter"],
                                       notes=_join(q.get("when"), q.get("prefs"),
                                                   ("%d options" % q["options"]) if q.get("options") else None),
                                       cite=cite_of(q["site"])))
        elif fname == "cards-necrobinder-regent":
            if q["kind"] == "Nested":
                nested.append({"via": q["entry"], "notes": q["then"]})
                continue
            site = q["site"]
            opener = self_ref
            via = None
            if q.get("indirect"):
                pid = site.split(":", 1)[0]
                opener = "powers:" + pid
                via = "powers:" + pid
            prompts.append(make_prompt(q["kind"], q["entry"], site, opener, q["pile"], q["min"], q["max"],
                                       require_confirm=q.get("require_manual_confirmation"), can_skip=q["can_skip"],
                                       filt=q["filter"], cite=q.get("cite"), via=via,
                                       notes=_join(q.get("auto_resolve"), q.get("then"), q.get("prefs"))))
        elif fname == "cards-silent-defect":
            site = q["site"]
            opener, via = self_ref, None
            m = re.match(r"power:(\w+)\.(\w+)$", site)
            if m:
                pid = ids.get(m.group(1), m.group(1))
                site = "%s:%s" % (pid, m.group(2))
                opener = via = "powers:" + pid
            else:
                site = "%s:%s" % (me, site)
            prompts.append(make_prompt(q["type"], q["cmd"], site, opener, q["source_pile"], q["min_select"], q["max_select"],
                                       filt=q["filter"], cite=q.get("cite"), via=via,
                                       notes=_join(q.get("prompt_key"), q.get("auto_resolution"), q.get("note"))))
        elif fname in ("monsters", "encounters"):
            opener = "monsters:KNOWLEDGE_DEMON"
            prompts.append(make_prompt(q["type"], "CardSelectCmd.FromChooseACardScreen", "KNOWLEDGE_DEMON:ChooseCurse", opener,
                                       "generated", q["min"], q["max"], can_skip=q["can_skip"],
                                       via=None if fname == "monsters" else opener, method="ChooseCurse",
                                       cite="Models.Monsters/KnowledgeDemon.cs:124", notes=_join(q.get("source"), q.get("context"))))
        elif fname == "powers":
            prompts.append(make_prompt(q["type"], q["cmd"], "%s:%s" % (me, q["site"]), self_ref, q["source"], q["min"], q["max"],
                                       can_skip=q["can_skip"], filt=q.get("filter"), cite=cite_of(q["site"]),
                                       notes=_join(q.get("prompt"), q.get("followup"))))
        elif fname in ("relics", "potions"):
            t = q["type"]
            site = "%s:%s" % (me, q["site"])
            flags = q.get("flags") or ""
            if t == "RewardsScreen":
                phases.append(make_phase("Rewards", "opens", q["call"], site, self_ref, _join(q["source"], flags), cite_of(q["site"])))
                continue
            if t == "RestSiteOption":
                phases.append(make_phase("RestSite", "modifies", q["site"], site, self_ref, flags, cite_of(q["site"])))
                continue
            if t == "CardRewardAlternative":
                prompts.append(make_prompt("CardReward", "CardReward.Select", site, self_ref, "generated", q["min"], q["max"],
                                           notes=_join("adds an alternative to every card reward (role: modifies)", flags),
                                           cite=cite_of(q["site"])))
                continue
            k = {"ChooseBundle": "Bundle", "SimpleGridRewards": "RewardsGrid"}.get(t, t)
            meth = _entry_method(q["call"])
            if meth in ENTRY_KIND:
                k = ENTRY_KIND[meth]
            can_skip = "canSkip=true" in flags
            cancelable = _flag(flags, "Cancelable") or ("cancelable=true" in flags.lower())
            rc = _flag(flags, "RMC")
            prompts.append(make_prompt(k, q["call"], site, self_ref, q["source"], q["min"], q["max"], require_confirm=rc,
                                       can_skip=can_skip, cancelable=bool(cancelable), notes=flags or None,
                                       cite=cite_of(q["site"])))
        elif fname == "events":
            t = q["type"]
            site = "%s:%s" % (me, q.get("option"))
            meth = _entry_method(t)
            if meth in ENTRY_KIND:
                prompts.append(make_prompt(ENTRY_KIND[meth], t, site, self_ref, q["source"], q["min"], q["max"],
                                           cancelable=q.get("cancelable", False), filt=q.get("filter"), cite=q.get("cite"),
                                           notes=_join(q.get("auto_resolve"), q.get("notes")), method=q.get("option")))
                continue
            phase = {"RewardsCmd.OfferCustom": "Rewards", "EventCombat": "EventCombat", "CrystalSphereMinigame": "CrystalSphere",
                     "AbandonRunConfirm": "AbandonRun"}.get(t)
            if phase is None and t.startswith("MerchantInventory"):
                phase = "Shop"
            if phase is None and t.startswith("PotionUse"):
                phase = "PotionUse"
            phases.append(make_phase(phase or t, "opens", t, site, self_ref,
                                     _join(q.get("source"), q.get("notes"), ("max %s" % q["max"]) if q.get("max") is not None else None),
                                     q.get("cite")))
        else:
            prompts.append(q)
    return prompts, phases, nested
