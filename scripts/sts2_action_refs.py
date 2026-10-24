#!/usr/bin/env python3
"""Stable, validated action references for the live harness.

Live STS2MCP actions address everything by *current screen position*: hand
``card_index``, enemy ``entity_id`` (renumbered when an earlier enemy of the
same type dies), shop ``index`` (while unaffordable items are hidden from the
legal-action list), reward ``index`` (a skipped card reward stays at index 0).
A model that chains decisions or reads an older observation therefore sends a
valid-looking but wrong index.  This module lets the caller name things by
stable identity instead and resolves the identity against the *fresh* state
immediately before each request:

* cards:   display name (``"Bash+"``), card id (``"BASH"``) or model id, with an
           optional ``#k`` occurrence suffix when non-identical copies exist;
* enemies: ``combat_id`` (stable for the whole combat) as ``2``, ``"cid:2"`` or
           ``"Nibbit@2"``; an ``entity_id`` from the observed state is mapped to
           its combat_id at the start of an ``act`` call, never re-guessed later;
* potions: slot (``0``/``"slot:0"``) or potion id (``"FIRE_POTION"``);
* shop / rewards / treasure / rest / map: item ids, reward kinds, relic ids,
           option ids, and ``col,row`` map coordinates.

The resolver contains no gameplay policy.  It never chooses among options; it
only translates an explicit reference to the single matching legal action, and
fails loudly (``ActionRefError``) when the reference is missing, ambiguous or
no longer legal.  The wire action always comes from ``derive_legal_actions``,
so nothing outside the visible legal set is ever sent.
"""

from __future__ import annotations

import json
import re
import sys
from copy import deepcopy
from pathlib import Path
from typing import Any, Iterable

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT / "sts2rec") not in sys.path:
    sys.path.insert(0, str(ROOT / "sts2rec"))
from sts2rec.legal_actions import COMBAT_TYPES, derive_legal_actions  # noqa: E402


class ActionRefError(ValueError):
    """A reference could not be resolved to exactly one legal action."""

    def __init__(self, message: str, *, available: Iterable[Any] = ()) -> None:
        super().__init__(message)
        unique: list[Any] = []
        for item in available:  # identical copies are one choice
            if item not in unique:
                unique.append(item)
        self.available = unique

    def __str__(self) -> str:  # pragma: no cover - formatting only
        base = super().__str__()
        if not self.available:
            return base
        return base + " | available: " + json.dumps(self.available, ensure_ascii=False)


# ---------------------------------------------------------------- normalizing
_MARKUP = re.compile(r"\[[^\]]*\]")
_UPGRADE = re.compile(r"^(?P<base>.*?)\s*(?:\+(?P<level>\d+)|(?P<plus>\++))\s*$")
_QUOTES = str.maketrans({"\u2019": "'", "\u2018": "'", "\u201c": '"', "\u201d": '"'})


def normalize_text(value: Any) -> str:
    """Case/markup/whitespace-insensitive text used for every guard comparison.

    Both the caller's expectation and the game text pass through this, so a
    guard never fails merely because one side was lowercased or carried BBCode
    such as ``[gold]`` or an energy icon tag.
    """
    text = _MARKUP.sub(" ", str(value or "")).translate(_QUOTES)
    return " ".join(text.casefold().split())


def split_upgrade(name: Any) -> tuple[str, int]:
    """``"Bash+"`` -> ("bash", 1); ``"Bash++"`` -> ("bash", 1); ``"Searing Blow+3"`` -> (.., 3).

    Repeated ``+`` is a display artefact (name already contains ``+`` and a
    renderer appended another), never a second upgrade level.
    """
    text = normalize_text(name)
    match = _UPGRADE.match(text)
    if not match:
        return text, 0
    level = int(match.group("level")) if match.group("level") else 1
    return match.group("base").strip(), level


def card_label(card: dict[str, Any]) -> str:
    """Display name with exactly one upgrade marker (fixes the cosmetic ``++``)."""
    raw = str(card.get("name") or card.get("card_name") or card.get("id") or "?").strip()
    match = _UPGRADE.match(raw)
    base = match.group("base").strip() if match else raw
    level = int(match.group("level")) if match and match.group("level") else (1 if match else 0)
    if card.get("is_upgraded") and level == 0:
        level = 1
    if level == 0:
        return base
    return f"{base}+" if level == 1 else f"{base}+{level}"


def _card_identity(card: dict[str, Any]) -> str:
    keep = {k: card.get(k) for k in (
        "id", "model_id", "is_upgraded", "cost", "star_cost", "description",
        "enchantment", "affliction", "can_play", "target_type", "type")}
    keep["label"] = card_label(card)
    return json.dumps(keep, sort_keys=True, ensure_ascii=False, default=str)


def card_refs(cards: list[dict[str, Any]]) -> list[str]:
    """Reference string for each card; ``#k`` only where same-named copies differ."""
    labels = [card_label(card) for card in cards]
    refs = []
    for position, card in enumerate(cards):
        same = [c for c, label in zip(cards, labels) if label == labels[position]]
        if len(same) > 1 and len({_card_identity(c) for c in same}) > 1:
            occurrence = sum(1 for c, label in zip(cards[: position + 1], labels) if label == labels[position])
            refs.append(f"{labels[position]}#{occurrence}")
        else:
            refs.append(labels[position])
    return refs


def _parse_occurrence(ref: str) -> tuple[str, int | None]:
    match = re.match(r"^(.*?)#(\d+)$", ref.strip())
    if match:
        return match.group(1).strip(), int(match.group(2))
    return ref.strip(), None


def match_card(cards: list[dict[str, Any]], ref: Any, *, what: str = "card") -> tuple[dict[str, Any], list[str]]:
    """Return the unique card named by ``ref`` and any resolution notes."""
    notes: list[str] = []
    if isinstance(ref, bool) or isinstance(ref, int):
        raise ActionRefError(
            f"{what} reference {ref!r} is a bare position; name the card (e.g. 'Bash+', 'STRIKE_IRONCLAD#2')",
            available=card_refs(cards))
    occurrence = None
    if isinstance(ref, dict):
        query = ref.get("name") or ref.get("id") or ref.get("model_id")
        occurrence = ref.get("occurrence")
    else:
        query, occurrence = _parse_occurrence(str(ref))
    if not query:
        raise ActionRefError(f"empty {what} reference", available=card_refs(cards))
    folded = str(query).strip().casefold()
    by_id = [c for c in cards if folded in {str(c.get("id") or "").casefold(), str(c.get("model_id") or "").casefold(),
                                            str(c.get("card_id") or "").casefold()}]
    q_base, q_level = split_upgrade(query)
    exact = [c for c in cards if split_upgrade(card_label(c)) == (q_base, q_level)]
    if by_id:
        matches = by_id
    elif exact:
        matches = exact
    else:
        loose = [c for c in cards if split_upgrade(card_label(c))[0] == q_base]
        if not loose:
            raise ActionRefError(f"no {what} matches {query!r}", available=card_refs(cards))
        notes.append(f"{what} {query!r} matched {card_label(loose[0])!r} ignoring the upgrade marker")
        matches = loose
    if occurrence is not None:
        occurrence = int(occurrence)
        identical = len({_card_identity(c) for c in matches}) == 1
        if occurrence == 0:
            # ``#k`` is 1-based; ``#0`` can only mean "the first copy".
            notes.append(f"{what} {query!r}#0 read as the first copy ({what} refs use 1-based #k)")
            occurrence = 1
        if occurrence < 0 or (occurrence > len(matches) and not identical):
            raise ActionRefError(f"{what} {query!r}#{occurrence}: only {len(matches)} match(es)",
                                 available=card_refs(cards))
        if occurrence > len(matches):
            # Identical copies are interchangeable: a #k that outlived an earlier
            # play of the same card still names the same choice.
            notes.append(f"{what} {query!r}#{occurrence}: only {len(matches)} identical copy(ies) left; "
                         "used the first")
            occurrence = 1
        return matches[occurrence - 1], notes
    if len({_card_identity(c) for c in matches}) > 1:
        raise ActionRefError(f"{what} {query!r} is ambiguous: copies differ; add #k", available=card_refs(cards))
    return matches[0], notes


# ------------------------------------------------------------------- enemies
def alive_enemies(state: dict[str, Any]) -> list[dict[str, Any]]:
    battle = state.get("battle") if isinstance(state.get("battle"), dict) else {}
    return [e for e in battle.get("enemies", []) or [] if isinstance(e, dict)
            and int(e.get("hp", 0) or 0) > 0 and e.get("is_targetable", True)]


def enemy_ref(enemy: dict[str, Any]) -> str:
    return f"{enemy.get('name') or enemy.get('id') or enemy.get('entity_id')}@{enemy.get('combat_id')}"


def entity_map(state: dict[str, Any]) -> dict[str, int]:
    """entity_id -> combat_id for the state the caller actually observed."""
    battle = state.get("battle") if isinstance(state.get("battle"), dict) else {}
    return {str(e["entity_id"]): e["combat_id"] for e in battle.get("enemies", []) or []
            if isinstance(e, dict) and e.get("entity_id") is not None and e.get("combat_id") is not None}


def match_enemy(state: dict[str, Any], ref: Any, *, observed_entities: dict[str, int] | None = None) -> dict[str, Any]:
    enemies = alive_enemies(state)
    refs = [enemy_ref(e) for e in enemies]
    if ref is None or ref == "":
        raise ActionRefError("target required", available=refs)
    combat_id: int | None = None
    name: str | None = None
    if isinstance(ref, bool):
        raise ActionRefError(f"invalid target {ref!r}", available=refs)
    if isinstance(ref, int):
        combat_id = ref
    else:
        text = str(ref).strip()
        if text.isdigit():
            combat_id = int(text)
        elif text.casefold().startswith("cid:") and text[4:].strip().isdigit():
            combat_id = int(text[4:].strip())
        elif "@" in text and text.rsplit("@", 1)[1].strip().isdigit():
            name, cid = text.rsplit("@", 1)
            combat_id = int(cid)
        elif observed_entities and text in observed_entities:
            combat_id = int(observed_entities[text])
        else:
            hits = [e for e in enemies if str(e.get("entity_id")) == text]
            if not hits:
                folded = normalize_text(text)
                hits = [e for e in enemies if folded in {normalize_text(e.get("name")), normalize_text(e.get("id"))}]
                if len(hits) > 1:
                    raise ActionRefError(f"target {text!r} is ambiguous; use name@combat_id", available=refs)
            if not hits:
                raise ActionRefError(f"no living targetable enemy matches {text!r}", available=refs)
            return hits[0]
    hits = [e for e in enemies if e.get("combat_id") == combat_id]
    if not hits:
        raise ActionRefError(f"target combat_id {combat_id} is dead, untargetable or absent", available=refs)
    if name and normalize_text(name) not in {normalize_text(hits[0].get("name")), normalize_text(hits[0].get("id")),
                                              normalize_text(hits[0].get("entity_id"))}:
        raise ActionRefError(f"target {ref!r}: combat_id {combat_id} is {enemy_ref(hits[0])}", available=refs)
    return hits[0]


# ------------------------------------------------------------------- potions
def potions(state: dict[str, Any]) -> list[dict[str, Any]]:
    player = state.get("player") if isinstance(state.get("player"), dict) else {}
    return [p for p in player.get("potions", []) or [] if isinstance(p, dict)]


def potion_slots_full(state: dict[str, Any]) -> bool:
    player = state.get("player") if isinstance(state.get("player"), dict) else {}
    limit = player.get("max_potion_slots")
    return isinstance(limit, int) and limit > 0 and len(potions(state)) >= limit


def potion_ref(potion: dict[str, Any]) -> str:
    return f"slot:{potion.get('slot')}"


def match_potion(state: dict[str, Any], ref: Any) -> dict[str, Any]:
    """Potions are addressed by slot or by id (``model_id`` style); never by name fragments."""
    held = potions(state)
    refs = [f"slot:{p.get('slot')}={p.get('id')}" for p in held]
    if isinstance(ref, int) and not isinstance(ref, bool):
        slot = ref
    elif isinstance(ref, str) and (ref.strip().isdigit() or ref.casefold().startswith("slot:")):
        slot = int(ref.split(":", 1)[-1])
    else:
        folded = str(ref or "").strip().casefold()
        hits = [p for p in held if folded in {str(p.get("id") or "").casefold(), str(p.get("model_id") or "").casefold(),
                                              f"potion.{str(p.get('id') or '').casefold()}"}]
        if not hits:
            hits = [p for p in held if normalize_text(p.get("name")) == normalize_text(ref)]
        if not hits:
            raise ActionRefError(f"no potion matches {ref!r}", available=refs)
        return hits[0]
    hits = [p for p in held if p.get("slot") == slot]
    if not hits:
        raise ActionRefError(f"potion slot {slot} is empty", available=refs)
    return hits[0]


# --------------------------------------------------------------- other items
def shop_item_ref(item: dict[str, Any]) -> str:
    category = item.get("category")
    for key in ("card_id", "relic_id", "potion_id"):
        if item.get(key):
            return f"{category}:{item[key]}"
    return str(category)


def reward_ref(item: dict[str, Any]) -> str:
    kind = str(item.get("type") or "?")
    for key in ("potion_id", "relic_id", "card_id"):
        if item.get(key):
            return f"{kind}:{item[key]}"
    return kind


def _ref_with_occurrence(refs: list[str]) -> list[str]:
    out = []
    for position, ref in enumerate(refs):
        if refs.count(ref) > 1:
            out.append(f"{ref}#{refs[: position + 1].count(ref)}")
        else:
            out.append(ref)
    return out


def _without_index(item: dict[str, Any]) -> str:
    return json.dumps({k: v for k, v in item.items() if k != "index"}, sort_keys=True, default=str)


def _occurrence_fallback(items: list[dict[str, Any]], refs: list[str], text: str, what: str,
                         notes: list[str] | None) -> list[dict[str, Any]]:
    """``kind#k`` whose numbering moved (an earlier item was claimed) or a 0-based ``#0``.

    Only resolves when the answer is unambiguous: ``#0`` means the first such
    item, and an out-of-range ``#k`` resolves only when every remaining item of
    that kind is identical (so any of them is the same choice).
    """
    match = re.match(r"^(.*?)#(\d+)$", text)
    if not match:
        return []
    base, k = match.group(1).strip().casefold(), int(match.group(2))
    same = [item for item, key in zip(items, refs) if key.casefold() == base]
    if not same:
        return []
    if k == 0:
        if notes is not None:
            notes.append(f"{what} {text!r}: #0 read as the first one (refs use 1-based #k)")
        return same[:1]
    if k > len(same) and len({_without_index(i) for i in same}) == 1:
        if notes is not None:
            notes.append(f"{what} {text!r}: numbering shifted after an earlier claim; "
                         f"{len(same)} identical one(s) left, used the first")
        return same[:1]
    return []


def _match_keyed(items: list[dict[str, Any]], refs: list[str], ref: Any, what: str,
                 names: Iterable[str] = (), notes: list[str] | None = None) -> dict[str, Any]:
    unique = _ref_with_occurrence(refs)
    text = str(ref).strip()
    folded = text.casefold()
    hits = [item for item, key, ukey in zip(items, refs, unique) if folded in {key.casefold(), ukey.casefold()}]
    if not hits:
        # Bare ids ("SHRUG_IT_OFF") and exact display names, normalized on both sides.
        hits = [item for item, key in zip(items, refs) if key.split(":", 1)[-1].casefold() == folded]
    if not hits:
        # A bare kind ("potion", "relic") when only one item of that kind exists.
        hits = [item for item, key in zip(items, refs) if key.split(":", 1)[0].casefold() == folded]
    if not hits:
        query = normalize_text(text)
        hits = [item for item in items if any(normalize_text(item.get(n)) == query for n in names if item.get(n))]
    if not hits:
        hits = _occurrence_fallback(items, refs, text, what, notes)
    if not hits:
        raise ActionRefError(f"no {what} matches {text!r}", available=unique)
    if len(hits) > 1 and len({json.dumps(h, sort_keys=True, default=str) for h in
                             ({k: v for k, v in x.items() if k != "index"} for x in hits)}) > 1:
        raise ActionRefError(f"{what} {text!r} is ambiguous; use one of the specific refs", available=unique)
    return hits[0]


# ----------------------------------------------------------- legal + refs view
def _block(state: dict[str, Any], key: str) -> dict[str, Any]:
    value = state.get(key)
    return value if isinstance(value, dict) else {}


def _hand(state: dict[str, Any]) -> list[dict[str, Any]]:
    player = _block(state, "player")
    return [c for c in player.get("hand", []) or [] if isinstance(c, dict)]


def blocked_actions(state: dict[str, Any]) -> list[dict[str, Any]]:
    """Visible options that the game will refuse right now, with the reason.

    Listed in observations so a model never wastes a call (or misreads the
    legal-action numbering) on them.
    """
    blocked = []
    st = state.get("state_type")
    if st == "rewards" and potion_slots_full(state):
        for item in _block(state, "rewards").get("items", []) or []:
            if isinstance(item, dict) and item.get("type") == "potion":
                blocked.append({"action": "claim_reward", "ref": reward_ref(item), "reason": "potion_slots_full",
                                "hint": "discard_potion a held potion first, or proceed to leave it"})
    if st in {"shop", "fake_merchant"}:
        outer = _block(state, st)
        block = outer.get("shop") if st == "fake_merchant" and isinstance(outer.get("shop"), dict) else outer
        for item in block.get("items", []) or []:
            if not isinstance(item, dict) or not item.get("is_stocked", False):
                continue
            if not item.get("can_afford", False):
                blocked.append({"action": "shop_purchase", "ref": shop_item_ref(item), "price": item.get("price"),
                                "reason": "cannot_afford"})
            elif item.get("category") == "potion" and potion_slots_full(state):
                blocked.append({"action": "shop_purchase", "ref": shop_item_ref(item), "reason": "potion_slots_full"})
    if st in COMBAT_TYPES:
        hand = _hand(state)
        for card, ref in zip(hand, card_refs(hand)):
            if not card.get("can_play", False):
                entry = {"action": "play_card", "ref": ref,
                         "reason": card.get("unplayable_reason") or "can_play=false"}
                if card.get("cost") is not None:
                    entry["cost"] = card.get("cost")
                if entry not in blocked:  # identical copies are one entry
                    blocked.append(entry)
    return blocked


def legal_actions(state: dict[str, Any]) -> list[dict[str, Any]]:
    """derive_legal_actions minus options the game is known to refuse."""
    blocked = {(b["action"], b["ref"]) for b in blocked_actions(state)}
    out = []
    for action in derive_legal_actions(state):
        item = action.get("candidate") or {}
        if action["action"] == "claim_reward" and ("claim_reward", reward_ref(item)) in blocked:
            continue
        if action["action"] == "shop_purchase" and ("shop_purchase", shop_item_ref(item)) in blocked:
            continue
        out.append(action)
    # Discarding is legal on any non-combat screen (STS2MCP has no combat check)
    # and is the only way to make room before claiming/buying a potion.
    if state.get("state_type") in OUT_OF_COMBAT_DISCARD and not any(a["action"] == "discard_potion" for a in out):
        for potion in potions(state):
            out.append({"action": "discard_potion", "slot": potion.get("slot"), "candidate": deepcopy(potion)})
    return out


OUT_OF_COMBAT_DISCARD = frozenset({"rewards", "card_reward", "shop", "fake_merchant", "map", "event", "rest_site", "treasure"})


def action_ref(state: dict[str, Any], action: dict[str, Any]) -> dict[str, Any]:
    """A stable reference that ``resolve`` maps back to ``action`` on a fresh state."""
    name = action["action"]
    st = state.get("state_type")
    item = action.get("candidate") or {}
    if name == "play_card":
        refs = card_refs(_hand(state))
        position = next(i for i, c in enumerate(_hand(state)) if c.get("index") == action.get("card_index"))
        ref: dict[str, Any] = {"action": name, "card": refs[position]}
        if "target" in action:
            ref["target"] = enemy_ref(action.get("target_candidate") or {})
        return ref
    if name in {"use_potion", "discard_potion"}:
        ref = {"action": name, "potion": f"slot:{action.get('slot')}", "potion_id": item.get("id")}
        if "target" in action:
            ref["target"] = enemy_ref(action.get("target_candidate") or {})
        return ref
    if name == "combat_select_card":
        cards = [c for c in _block(state, "hand_select").get("cards", []) or [] if isinstance(c, dict)]
        position = next(i for i, c in enumerate(cards) if c.get("index") == action.get("card_index"))
        return {"action": name, "card": card_refs(cards)[position]}
    if name == "select_card":
        cards = [c for c in _block(state, "card_select").get("cards", []) or [] if isinstance(c, dict)]
        position = next(i for i, c in enumerate(cards) if c.get("index") == action.get("index"))
        return {"action": name, "card": card_refs(cards)[position]}
    if name == "select_card_reward":
        cards = [c for c in _block(state, "card_reward").get("cards", []) or [] if isinstance(c, dict)]
        position = next(i for i, c in enumerate(cards) if c.get("index") == action.get("card_index"))
        return {"action": name, "card": card_refs(cards)[position]}
    if name == "shop_purchase":
        outer = _block(state, st)
        block = outer.get("shop") if st == "fake_merchant" and isinstance(outer.get("shop"), dict) else outer
        items = [i for i in block.get("items", []) or [] if isinstance(i, dict)]
        refs = _ref_with_occurrence([shop_item_ref(i) for i in items])
        return {"action": name, "item": refs[items.index(next(i for i in items if i.get("index") == action.get("index")))]}
    if name == "claim_reward":
        items = [i for i in _block(state, "rewards").get("items", []) or [] if isinstance(i, dict)]
        refs = _ref_with_occurrence([reward_ref(i) for i in items])
        return {"action": name, "reward": refs[items.index(next(i for i in items if i.get("index") == action.get("index")))]}
    if name == "choose_map_node":
        return {"action": name, "node": f"{item.get('col')},{item.get('row')}", "type": item.get("type")}
    if name in {"claim_treasure_relic", "select_relic"}:
        return {"action": name, "relic": item.get("id") or item.get("name")}
    if name == "choose_rest_option":
        return {"action": name, "option": item.get("id") or item.get("name")}
    if name == "choose_event_option":
        return {"action": name, "index": action.get("index"), "expect": item.get("title")}
    if name == "menu_select":
        return {"action": name, "option": action.get("option")}
    plain = {k: v for k, v in action.items() if k not in {"candidate", "target_candidate"}}
    if item:
        plain["expect"] = item.get("name") or item.get("title") or item.get("id")
    return plain


def legal_digest(actions: Iterable[dict[str, Any]] | None) -> str:
    """Short digest of a listing's legal-action refs, in order.

    Two listings with the same digest offer the same choices at the same
    positions, so a decision read from one is still valid against the other.
    """
    import hashlib  # noqa: PLC0415 - keep module import cheap

    refs_only = [a.get("ref") if isinstance(a, dict) else a for a in (actions or [])]
    blob = json.dumps(refs_only, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(blob.encode("utf-8")).hexdigest()[:16]


def observe_actions(state: dict[str, Any]) -> list[dict[str, Any]]:
    """Legal actions shown to the controller: wire form plus a stable ``ref``."""
    out, seen = [], set()
    for action in legal_actions(state):
        wire = {k: v for k, v in action.items() if k not in {"candidate", "target_candidate"}}
        try:
            ref = action_ref(state, action)
        except (StopIteration, ValueError):  # malformed snapshot; still show wire form
            ref = None
        key = json.dumps(ref, sort_keys=True, ensure_ascii=False) if ref is not None else None
        if key is not None and key in seen:
            continue  # identical copies (e.g. two plain Strikes) resolve to the same play
        if key is not None:
            seen.add(key)
        out.append({**wire, "ref": ref})
    return out


# ------------------------------------------------------------------ guards
def expect_matches(expected: Any, candidate: dict[str, Any] | None) -> bool:
    """Normalized guard: does ``expected`` name/describe this candidate?

    Compares casefolded, markup-free text on *both* sides; accepts the card's
    display label with or without the upgrade marker, its id/model_id, or a
    substring of its title/description.
    """
    if expected in (None, ""):
        return True
    if not isinstance(candidate, dict):
        return False
    query = normalize_text(expected)
    q_base, q_level = split_upgrade(expected)
    names = [candidate.get(k) for k in ("name", "title", "card_name", "relic_name", "potion_name", "id",
                                        "model_id", "card_id", "relic_id", "potion_id", "option", "type")]
    for name in filter(None, names):
        if normalize_text(name) == query:
            return True
        base, level = split_upgrade(name)
        if base == q_base and (q_level == 0 or level == q_level or (candidate.get("is_upgraded") and q_level == 1)):
            return True
    if candidate.get("is_upgraded") and split_upgrade(card_label(candidate)) == (q_base, q_level):
        return True
    texts = [candidate.get(k) for k in ("description", "card_description", "relic_description", "potion_description")]
    return len(query) >= 3 and any(query in normalize_text(t) for t in texts if t)


# ------------------------------------------------------------------ anchors
def card_anchor(card: dict[str, Any]) -> str:
    """Content identity of a card that survives other plays (no index/can_play/cost)."""
    keep = {k: card.get(k) for k in ("id", "model_id", "is_upgraded", "description", "enchantment",
                                     "affliction", "target_type", "type")}
    keep["label"] = card_label(card)
    return json.dumps(keep, sort_keys=True, ensure_ascii=False, default=str)


def anchor_request(state: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
    """Pin a positional-looking ref (``Name#k``, ``card#3``) to the item it names *now*.

    Called once per request on the state the caller observed, before anything
    is sent.  Later steps of the same chain then find that exact item by
    content, so ``#k`` numbering that shifts after an earlier play/claim cannot
    redirect them.  Requests that do not resolve are returned unchanged (the
    real resolution reports the error at its own step).
    """
    request = dict(request)
    name = request.get("action")
    try:
        if name == "play_card" and isinstance(request.get("card"), (str, dict)):
            card, _ = match_card(_hand(state), request["card"])
            request["_card_anchor"] = card_anchor(card)
        elif name == "claim_reward" and request.get("reward") is not None:
            items = [i for i in _block(state, "rewards").get("items", []) or [] if isinstance(i, dict)]
            item = _match_keyed(items, [reward_ref(i) for i in items], request["reward"], "reward",
                                ("potion_name", "relic_name", "description"), [])
            request["_reward_anchor"] = _without_index(item)
    except ActionRefError:
        pass
    return request


# --------------------------------------------------------------- resolution
SHORTHANDS = {
    "end": {"action": "end_turn"}, "end_turn": {"action": "end_turn"},
    "proceed": {"action": "proceed"}, "skip": {"action": "skip"},
    "confirm": {"action": "confirm"}, "cancel": {"action": "cancel"},
    # Treasure has a single relic in normal chests; the resolver refuses the
    # shorthand when more than one relic is offered (no choice is made for you).
    "claim_treasure_relic": {"action": "claim_treasure_relic"}, "claim_treasure": {"action": "claim_treasure_relic"},
    "treasure": {"action": "claim_treasure_relic"}, "take_relic": {"action": "claim_treasure_relic"},
}


def parse_request(payload: Any) -> list[dict[str, Any]]:
    """Accept one action (dict or shorthand string) or a list of them."""
    if isinstance(payload, str):
        text = payload.strip()
        if text.startswith("[") or text.startswith("{"):
            payload = json.loads(text)
        elif text.casefold() in SHORTHANDS:
            payload = dict(SHORTHANDS[text.casefold()])
        else:
            raise ActionRefError(f"unknown action shorthand {text!r}", available=sorted(SHORTHANDS))
    items = payload if isinstance(payload, list) else [payload]
    out = []
    for item in items:
        if isinstance(item, str):
            out.extend(parse_request(item))
        elif isinstance(item, dict) and isinstance(item.get("action"), str):
            out.append(dict(item))
        else:
            raise ActionRefError(f"action must be an object with a string 'action': {item!r}")
    if not out:
        raise ActionRefError("empty action request")
    return out


_INDEX_FIELDS = {"play_card": "card_index", "combat_select_card": "card_index", "select_card_reward": "card_index",
                 "select_card": "index", "shop_purchase": "index", "claim_reward": "index",
                 "choose_map_node": "index", "claim_treasure_relic": "index", "select_relic": "index",
                 "choose_rest_option": "index", "choose_event_option": "index", "select_bundle": "index"}


def _pick(legal: list[dict[str, Any]], name: str, **params: Any) -> dict[str, Any]:
    hits = [a for a in legal if a["action"] == name and all(a.get(k) == v for k, v in params.items())]
    if not hits:
        raise ActionRefError(f"{name} {params or ''} is not a legal action now",
                             available=[{k: v for k, v in a.items() if k not in {"candidate", "target_candidate"}}
                                        for a in legal])
    return hits[0]


def resolve(state: dict[str, Any], request: dict[str, Any], *, allow_index: bool = True,
            observed_entities: dict[str, int] | None = None) -> dict[str, Any]:
    """Resolve one request against ``state``.

    Returns ``{"wire": <MCP action>, "legal": <matching legal action>, "notes": [...]}``.
    ``allow_index`` is False for every step after the first in a chained
    request: positional indices are only meaningful for the observation the
    caller saw, so chained steps must use stable references.
    """
    st = state.get("state_type")
    legal = legal_actions(state)
    request = dict(request)
    name = request.pop("action")
    expect = request.pop("expect", None)
    request.pop("ref", None)
    notes: list[str] = []
    if name not in {"play_card"}:
        request.pop("_card_anchor", None)
    if name != "claim_reward":
        request.pop("_reward_anchor", None)

    # Context-dependent shorthands -------------------------------------------
    if name == "skip":
        name = {"card_reward": "skip_card_reward", "relic_select": "skip_relic_selection",
                "card_select": "skip_card_selection"}.get(st, name)
    if name == "confirm":
        name = {"hand_select": "combat_confirm_selection", "card_select": "confirm_selection",
                "bundle_select": "confirm_bundle_selection"}.get(st, name)
    if name == "cancel":
        name = {"card_select": "cancel_selection", "bundle_select": "cancel_bundle_selection"}.get(st, name)
    if name == "select":
        name = {"hand_select": "combat_select_card", "card_select": "select_card",
                "card_reward": "select_card_reward"}.get(st, name)
    if name == "end_turn" and st not in COMBAT_TYPES:
        raise ActionRefError(f"end_turn requested but screen is {st!r}")
    if name == "end_turn" and not _block(state, "battle").get("is_play_phase"):
        raise ActionRefError("end_turn requested outside the player's play phase (enemy turn still resolving)")

    index_field = _INDEX_FIELDS.get(name)
    if index_field and index_field in request and not allow_index:
        raise ActionRefError(
            f"chained step {name} uses positional {index_field}={request[index_field]}; positions shift after "
            "every action, so chained steps must use stable refs (card/target/item/reward/node/relic/option)")

    chosen: dict[str, Any]
    if name in {"play_card", "combat_select_card", "select_card", "select_card_reward"} and "card" in request:
        cards = {"play_card": _hand(state),
                 "combat_select_card": _block(state, "hand_select").get("cards", []),
                 "select_card": _block(state, "card_select").get("cards", []),
                 "select_card_reward": _block(state, "card_reward").get("cards", [])}[name] or []
        cards = [c for c in cards if isinstance(c, dict)]
        if name == "select_card":
            # Selecting is a toggle: naming "Strike" twice must pick two
            # different copies, never deselect the one already chosen.
            chosen_indices = set(_block(state, "card_select").get("selected_indices") or [])
            cards = [c for c in cards if not c.get("is_selected") and c.get("index") not in chosen_indices]
        card_ref = request.pop("card")
        anchor = request.pop("_card_anchor", None)
        anchored = [c for c in cards if anchor is not None and card_anchor(c) == anchor]
        if anchored:
            # Chained plays: the copy named on the observed hand, found again by
            # content after earlier plays renumbered "Name#k".
            card, card_notes = anchored[0], []
            if card_refs(cards)[cards.index(card)] != card_ref:
                card_notes.append(f"card {card_ref!r} re-resolved by content after earlier plays shifted #k")
        else:
            card, card_notes = match_card(cards, card_ref)
        notes += card_notes
        field = "index" if name == "select_card" else "card_index"
        params = {field: card.get("index")}
        if name == "play_card" and not card.get("can_play", False):
            reason = card.get("unplayable_reason") or "can_play=false"
            raise ActionRefError(f"{card_label(card)} cannot be played now: {reason}")
        target = request.pop("target", None)
        options = [a for a in legal if a["action"] == name and a.get(field) == card.get("index")]
        if not options:
            raise ActionRefError(f"{name} {card_label(card)} is not legal now")
        if any("target" in a for a in options):
            enemy = match_enemy(state, target, observed_entities=observed_entities)
            chosen = next(a for a in options if (a.get("target_candidate") or {}).get("combat_id") == enemy.get("combat_id"))
        else:
            if target not in (None, ""):
                notes.append(f"{card_label(card)} takes no target; ignored target {target!r}")
            chosen = options[0]
    elif name in {"use_potion", "discard_potion"} and ("potion" in request or "slot" in request):
        potion = match_potion(state, request.pop("potion", request.pop("slot", None)))
        if request.get("potion_id") and str(request.pop("potion_id")).casefold() != str(potion.get("id")).casefold():
            raise ActionRefError(f"slot {potion.get('slot')} now holds {potion.get('id')}, not the expected potion")
        request.pop("potion_id", None)
        options = [a for a in legal if a["action"] == name and a.get("slot") == potion.get("slot")]
        if not options:
            why = "outside the play phase" if st in COMBAT_TYPES else f"on screen {st!r}"
            if name == "use_potion" and not potion.get("can_use_in_combat", False):
                why = "can_use_in_combat=false"
            raise ActionRefError(f"{name} {potion.get('id')} (slot {potion.get('slot')}) is not legal {why}")
        target = request.pop("target", None)
        if any("target" in a for a in options):
            enemy = match_enemy(state, target, observed_entities=observed_entities)
            chosen = next(a for a in options if (a.get("target_candidate") or {}).get("combat_id") == enemy.get("combat_id"))
        else:
            chosen = options[0]
    elif name == "shop_purchase" and "item" in request:
        outer = _block(state, st)
        block = outer.get("shop") if st == "fake_merchant" and isinstance(outer.get("shop"), dict) else outer
        items = [i for i in block.get("items", []) or [] if isinstance(i, dict) and i.get("is_stocked", False)]
        item = _match_keyed(items, [shop_item_ref(i) for i in items], request.pop("item"), "shop item",
                            ("card_name", "relic_name", "potion_name"))
        blocked = [b for b in blocked_actions(state) if b["action"] == name and b["ref"] == shop_item_ref(item)]
        if blocked:
            raise ActionRefError(f"cannot buy {shop_item_ref(item)}: {blocked[0]['reason']}")
        chosen = _pick(legal, name, index=item.get("index"))
    elif name == "claim_treasure_relic" and not ({"relic", "index"} & set(request)):
        relics = [r for r in _block(state, "treasure").get("relics", []) or [] if isinstance(r, dict)]
        if len(relics) != 1:
            raise ActionRefError(f"claim_treasure_relic without a relic needs exactly one offered relic; "
                                 f"{len(relics)} offered, name one", available=[str(r.get("id")) for r in relics])
        chosen = _pick(legal, name, index=relics[0].get("index"))
        notes.append(f"claim_treasure_relic: the only offered relic {relics[0].get('id')}")
    elif name == "claim_reward" and "reward" in request:
        items = [i for i in _block(state, "rewards").get("items", []) or [] if isinstance(i, dict)]
        anchor = request.pop("_reward_anchor", None)
        ref = request.pop("reward")
        anchored = [i for i in items if anchor is not None and _without_index(i) == anchor]
        if anchored:
            # Chained claims: the reward the caller named on the observed screen,
            # found again by content after earlier claims renumbered the list.
            item = anchored[0]
            if reward_ref(item) != ref:
                notes.append(f"reward {ref!r} re-resolved by content after earlier claims shifted the list")
        else:
            item = _match_keyed(items, [reward_ref(i) for i in items], ref, "reward",
                                ("potion_name", "relic_name", "description"), notes)
        blocked = [b for b in blocked_actions(state) if b["action"] == name and b["ref"] == reward_ref(item)]
        if blocked:
            raise ActionRefError(f"cannot claim {reward_ref(item)}: {blocked[0]['reason']}; {blocked[0]['hint']}")
        chosen = _pick(legal, name, index=item.get("index"))
    elif name == "choose_map_node" and "node" in request:
        node = request.pop("node")
        if isinstance(node, dict):
            col, row = node.get("col"), node.get("row")
        else:
            parts = str(node).replace("@", ",").split(",")
            col, row = int(parts[-2]), int(parts[-1])
        options = [a for a in legal if a["action"] == name and (a.get("candidate") or {}).get("col") == int(col)
                   and (a.get("candidate") or {}).get("row") == int(row)]
        if not options:
            raise ActionRefError(f"map node {col},{row} is not a next option (map may not have refreshed yet)",
                                 available=[f"{a['candidate'].get('col')},{a['candidate'].get('row')}"
                                            for a in legal if a["action"] == name])
        expected_type = request.pop("type", None)
        if expected_type and normalize_text(expected_type) != normalize_text(options[0]["candidate"].get("type")):
            raise ActionRefError(f"map node {col},{row} is {options[0]['candidate'].get('type')}, not {expected_type}")
        chosen = options[0]
    elif name in {"claim_treasure_relic", "select_relic"} and "relic" in request:
        key = "treasure" if name == "claim_treasure_relic" else "relic_select"
        relics = [r for r in _block(state, key).get("relics", []) or [] if isinstance(r, dict)]
        relic = _match_keyed(relics, [str(r.get("id")) for r in relics], request.pop("relic"), "relic", ("name",))
        chosen = _pick(legal, name, index=relic.get("index"))
    elif name == "choose_rest_option" and "option" in request:
        options = [o for o in _block(state, "rest_site").get("options", []) or [] if isinstance(o, dict)]
        option = _match_keyed(options, [str(o.get("id")) for o in options], request.pop("option"), "rest option", ("name",))
        chosen = _pick(legal, name, index=option.get("index"))
    elif name == "choose_event_option" and "option" in request and "index" not in request:
        options = [o for o in _block(state, "event").get("options", []) or [] if isinstance(o, dict)
                   and not o.get("is_locked") and not o.get("was_chosen")]
        option = _match_keyed(options, [normalize_text(o.get("title")) for o in options], request.pop("option"),
                              "event option", ("title",))
        chosen = _pick(legal, name, index=option.get("index"))
    else:
        # Raw wire form.  Translate a stale-prone entity_id into the observed combat_id.
        if "target" in request:
            enemy = match_enemy(state, request["target"], observed_entities=observed_entities)
            request["target"] = enemy.get("entity_id")
        candidates = [a for a in legal if a["action"] == name]
        if not candidates:
            raise ActionRefError(f"{name} is not a legal action on screen {st!r}",
                                 available=[{k: v for k, v in a.items() if k not in {"candidate", "target_candidate"}}
                                            for a in legal])
        chosen = _pick(legal, name, **request)
        request = {}

    leftovers = {k: v for k, v in request.items() if k not in {"confirm"}}
    if leftovers:
        raise ActionRefError(f"unrecognised fields for {name}: {sorted(leftovers)}")
    candidate = chosen.get("candidate")
    if expect is not None and not expect_matches(expect, candidate):
        raise ActionRefError(f"guard: expected {expect!r} but the resolved option is "
                             f"{(candidate or {}).get('name') or (candidate or {}).get('title') or candidate!r}")
    wire = {k: v for k, v in chosen.items() if k not in {"candidate", "target_candidate"}}
    if "target_candidate" in chosen and chosen["target_candidate"].get("combat_id") is not None:
        # combat_id is stable for the whole fight; STS2MCP resolves a numeric
        # string as combat_id, so the request cannot hit a renumbered entity.
        wire["target"] = str(chosen["target_candidate"]["combat_id"])
    return {"wire": wire, "legal": deepcopy(chosen), "notes": notes}


def describe(resolved: dict[str, Any]) -> str:
    legal = resolved["legal"]
    item = legal.get("candidate") or {}
    label = card_label(item) if item.get("name") or item.get("card_name") else (
        item.get("title") or item.get("id") or item.get("type") or "")
    target = legal.get("target_candidate")
    text = f"{legal['action']} {label}".strip()
    if target:
        text += f" -> {enemy_ref(target)}"
    return text
