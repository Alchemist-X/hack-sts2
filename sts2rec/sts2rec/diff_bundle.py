"""Oracle-side building blocks for the sim diff gate (diff bundle contract v1).

Contract: ``sim/specs/diff-bundle.md`` (bundle schema, projection rules,
eligibility, relic whitelist). Rule citations for everything ported here
(starter decks, the A5 curse, Neow deck edits, NetCombatCardDb ids):
``sim/specs/diff-bundle-exporter.md``.

This module is pure: it turns recorder snapshots / events / actions that are
already in memory into projections, decks and decisions. Corpus walking and
file output live in :mod:`sts2rec.diff_export`.

Every failure is reported as a reason string ``"<class>"`` or
``"<class>:<detail>"`` and never guessed around: a combat with any reason is
ineligible, and the reason classes feed the coverage histogram.
"""

from __future__ import annotations

import json
import re
from collections import Counter
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

BUNDLE_VERSION = 1

# sim/specs/diff-bundle.md "遗物白名单" (combat-inert, decompile-checked).
RELIC_WHITELIST = frozenset(
    {
        "BURNING_BLOOD",
        "GOLDEN_PEARL",
        "NUTRITIOUS_OYSTER",
        "PHIAL_HOLSTER",
        "WINGED_BOOTS",
        "STONE_HUMIDIFIER",
        "LAVA_ROCK",
        "SILVER_CRUCIBLE",
        "LOST_COFFER",
        "KALEIDOSCOPE",
        "NEOWS_TALISMAN",
        "POMANDER",
        "NEW_LEAF",
        "FISHING_ROD",
        "CURSED_PEARL",
        "PRECISE_SCISSORS",
        "PRECARIOUS_SHEARS",
        "DOWSING_ROD",
        "NEOWS_BONES",
        "NEOWS_SACRIFICE",
        "HEFTY_TABLET",
        "ARCANE_SCROLL",
        "SCROLL_BOXES",
        "LEAD_PAPERWEIGHT",
        "LEAFY_POULTICE",
        "NEOWS_TORMENT",
        "LARGE_CAPSULE",
    }
)

# Explicitly excluded by the contract (reason detail only; any relic outside
# the whitelist is ineligible either way).
RELIC_EXCLUDED = {
    "BOOMING_CONCH": "combat hook",
    "SMALL_CAPSULE": "random relic not knowable",
    "SILKEN_TRESS": "enchant changes cards",
}

# Seeding by game version (contract "seeding"): v0.110.1+ seed streams with
# XXH64; v0.107.1 runs use the legacy V18 kernel seed.
SEEDING_BY_VERSION = {
    "v0.107.1": "legacy_v18",
    "v0.110.1": "xxh64",
    "v0.111.0": "xxh64",
}
OLD_SEED_PREFIX = "old"

# Starting decks in character-model order (Models.Characters/<C>.cs
# StartingDeck, v0.111.0).
STARTER_DECKS: dict[str, tuple[str, ...]] = {
    "IRONCLAD": ("STRIKE_IRONCLAD",) * 5 + ("DEFEND_IRONCLAD",) * 4 + ("BASH",),
    "SILENT": ("STRIKE_SILENT",) * 5
    + ("DEFEND_SILENT",) * 5
    + ("NEUTRALIZE", "SURVIVOR"),
    "DEFECT": ("STRIKE_DEFECT",) * 4 + ("DEFEND_DEFECT",) * 4 + ("ZAP", "DUALCAST"),
    "NECROBINDER": ("STRIKE_NECROBINDER",) * 4
    + ("DEFEND_NECROBINDER",) * 4
    + ("BODYGUARD", "UNLEASH"),
    "REGENT": ("STRIKE_REGENT",) * 4
    + ("DEFEND_REGENT",) * 4
    + ("FALLING_STAR", "VENERATE"),
}
ASCENDERS_BANE = "ASCENDERS_BANE"
ASCENDERS_BANE_LEVEL = 5  # Entities.Ascension/AscensionLevel.cs: AscendersBane = 5

BASIC_STRIKES = frozenset(f"STRIKE_{c}" for c in STARTER_DECKS)
BASIC_DEFENDS = frozenset(f"DEFEND_{c}" for c in STARTER_DECKS)

# First-combat Niche offset (contract): 6*[KALEIDOSCOPE] + [NEW_LEAF] + [NEOWS_BONES].
# KALEIDOSCOPE's term is derived, not a free constant: AfterObtained loops Cards(2) times
# (Models.Relics/Kaleidoscope.cs:53,114), each a StableShuffle on Niche of the owner's unlocked
# character card pools minus its own (:56); StableShuffle = sort + UnstableShuffle, which draws
# NextInt count-1 times (Extensions/ListExtensions.cs:10-35); the rewards themselves roll on
# PlayerRng.Rewards (Factories/CardFactory.cs:82,191). Kaleidoscope.IsAllowedAtNeow (:116-123)
# offers it only when every character is unlocked, so at Neow the pool count is
# |ModelDb.AllCharacters| - 1 (5 characters in v0.111.0, ModelDb.cs:129-136, and v0.107.1).
KALEIDOSCOPE_CARD_REWARDS = 2
ALL_CHARACTER_COUNT = 5
KALEIDOSCOPE_NICHE_DRAWS = KALEIDOSCOPE_CARD_REWARDS * ((ALL_CHARACTER_COUNT - 1) - 1)
NICHE_OFFSET_PER_RELIC = {"KALEIDOSCOPE": KALEIDOSCOPE_NICHE_DRAWS, "NEW_LEAF": 1, "NEOWS_BONES": 1}

# Neow relics that append a known number of cards to the deck on pickup.
FIXED_GAIN_RELICS = {
    "ARCANE_SCROLL": 1,
    "CURSED_PEARL": 1,
    "DOWSING_ROD": 1,
    "HEFTY_TABLET": 2,
    "LARGE_CAPSULE": 2,
    "LEAD_PAPERWEIGHT": 1,
    "NEOWS_SACRIFICE": 1,
    "NEOWS_TORMENT": 1,
    "SCROLL_BOXES": 3,
}
# Neow relics whose card gain count depends on the player's reward picks.
VARIABLE_GAIN_RELICS = frozenset({"KALEIDOSCOPE", "LOST_COFFER", "MASSIVE_SCROLL", "SILKEN_TRESS"})
# Neow relics with no deck effect at pickup.
NO_DECK_OP_RELICS = frozenset(
    {
        "BOOMING_CONCH",
        "FISHING_ROD",
        "GOLDEN_PEARL",
        "LAVA_ROCK",
        "NUTRITIOUS_OYSTER",
        "PHIAL_HOLSTER",
        "SILVER_CRUCIBLE",
        "SMALL_CAPSULE",
        "STONE_HUMIDIFIER",
        "WINGED_BOOTS",
    }
)
DECK_OP_RELICS = frozenset(
    {
        "NEOWS_TALISMAN",
        "LEAFY_POULTICE",
        "NEW_LEAF",
        "PRECISE_SCISSORS",
        "PRECARIOUS_SHEARS",
        "POMANDER",
        "NEOWS_BONES",
    }
)
REMOVAL_COUNTS = {"PRECISE_SCISSORS": 1, "PRECARIOUS_SHEARS": 2}

ATTACK_INTENT_TYPES = frozenset({"Attack", "DeathBlow"})
STATUS_CARD_INTENT_TYPE = "StatusCard"
STATUS_CARD_COUNT_KEY = "card_count"
# U+00D7 MULTIPLICATION SIGN separates damage and hit count in intent labels.
_ATTACK_LABEL_RE = re.compile("^(\\d+)(?:×(\\d+))?$")
_PLUS_SUFFIX_RE = re.compile(r"^(.+)\+$")
_ENTITY_SUFFIX_RE = re.compile(r"_\d+$")

PHASE_PLAYER_PLAY = "player_play"
DECISION_ACTION_KINDS = frozenset({"play_card", "end_turn"})
# Logged by the game after every player turn end; the decision itself is the end_turn
# action recorded before it (a turn end without one: reason turn_end_unrecorded).
TURN_END_FOLLOW_UP_KIND = "ready_to_begin_enemy_turn"
EXCLUDED_ACTION_KINDS = frozenset({TURN_END_FOLLOW_UP_KIND})
# The recorder's player_choice ``context_model`` is NOT the model that asked for the
# choice: it is the last model pushed on any PlayerChoiceContext (mod/src/HumanFunnels.cs,
# a static set by a PushModel postfix and never popped), and Hooks/Hook.cs:268-275 pushes
# every hook listener, the MultiplayerScalingModel singleton included
# (Models.Singleton/MultiplayerScalingModel.cs:19), which never asks for a choice. So this
# label marks no automatic choice: real in-combat discards / card selections carry it.
# Every player_choice inside the combat window is a decision the bundle cannot express.
STICKY_CHOICE_CONTEXT_LABEL = "SINGLETON.MULTIPLAYER_SCALING_MODEL"
# Event entries that carry no game-state change (recorder bookkeeping).
NON_STATE_EVENT_ENTRIES = frozenset({"recorder_config", "checksum"})


def strip_prefix(value: Any, prefix: str) -> str:
    return str(value).removeprefix(prefix)


def card_key(card_id: str, upgraded: bool) -> str:
    """Contract pile encoding: upgraded cards carry a "+" suffix."""
    return f"{card_id}+" if upgraded else card_id


def reason_class(reason: str) -> str:
    return reason.split(":", 1)[0]


def seeding_for(game_version: str, seed: str) -> tuple[str | None, list[str]]:
    if seed.startswith(OLD_SEED_PREFIX):
        return None, ["seed_old_prefix"]
    seeding = SEEDING_BY_VERSION.get(game_version)
    if seeding is None:
        return None, [f"seeding_unknown_version:{game_version}"]
    return seeding, []


def niche_offset(relics: Iterable[str]) -> int:
    owned = set(relics)
    return sum(n for relic, n in NICHE_OFFSET_PER_RELIC.items() if relic in owned)


def relic_reasons(relics: Iterable[str]) -> list[str]:
    reasons = []
    for relic in relics:
        if relic in RELIC_WHITELIST:
            continue
        if relic in RELIC_EXCLUDED:
            reasons.append(f"relic_excluded:{relic}")
        else:
            reasons.append(f"relic_not_whitelisted:{relic}")
    return reasons


# --------------------------------------------------------------------------
# Sim registry (what the C# simulator has ported)
# --------------------------------------------------------------------------


@dataclass(frozen=True)
class SimRegistry:
    """Ported content of the simulator, as seen by the eligibility filter."""

    encounters: frozenset[str]
    cards: frozenset[str]
    upgradable_cards: frozenset[str]
    characters: frozenset[str]
    source: str

    def to_json(self) -> dict[str, Any]:
        return {
            "source": self.source,
            "characters": sorted(self.characters),
            "encounters": sorted(self.encounters),
            "cards": sorted(self.cards),
            "upgradable_cards": sorted(self.upgradable_cards),
        }

    @classmethod
    def from_json(cls, raw: Mapping[str, Any], *, source: str) -> SimRegistry:
        return cls(
            encounters=frozenset(raw.get("encounters", ())),
            cards=frozenset(raw.get("cards", ())),
            upgradable_cards=frozenset(raw.get("upgradable_cards", ())),
            characters=frozenset(raw.get("characters", ())),
            source=source,
        )


_CS_CLASS_RE = re.compile(
    r"(?P<attrs>(?:\[[^\[\]]*\]\s*)*)"
    r"(?P<mods>(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*)"
    r"class\s+(?P<name>\w+)(?:<[^>]*>)?\s*:\s*(?P<base>\w+)"
)
_CS_BASE_CALL_RE = re.compile(r":\s*base\s*\(\s*(?P<arg>\"[^\"]*\"|[A-Za-z_][\w.]*)")
_CS_CONST_RE = re.compile(r"const\s+string\s+(?P<name>\w+)\s*=\s*\"(?P<value>[^\"]*)\"")
_CS_ON_UPGRADE_RE = re.compile(r"override\s+void\s+OnUpgrade\s*\(")
_CS_CHARACTER_ID_RE = re.compile(r"const\s+string\s+\w*CharacterId\s*=\s*\"(?P<value>[A-Z0-9_]+)\"")
_SIM_DEF_BASES = {"CardDef": "cards", "EncounterDef": "encounters", "MonsterDef": "monsters"}


def _class_body(text: str, start: int) -> str:
    """Brace-matched body of the class whose declaration ends at ``start``."""
    open_at = text.find("{", start)
    if open_at < 0:
        return ""
    depth = 0
    for index in range(open_at, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[open_at : index + 1]
    return text[open_at:]


def scan_sim_registry(core_dir: str | Path) -> SimRegistry:
    """Read the ported card / encounter ids from the sim's C# sources.

    The sim registries are reflection-built from concrete ``CardDef`` /
    ``EncounterDef`` subclasses, each of which states its id as a string
    literal (sim/CONTRACTS.md section 1). This mirrors that discovery from
    source: classes deriving (transitively) from the def bases, minus
    ``abstract`` and ``[SimExclude]`` classes; a card counts as upgradable
    when its class overrides ``OnUpgrade`` (the upgrade values are ported).
    """
    root = Path(core_dir)
    classes: dict[str, dict[str, Any]] = {}
    characters: set[str] = set()
    for path in sorted(root.rglob("*.cs")):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        characters.update(m.group("value") for m in _CS_CHARACTER_ID_RE.finditer(text))
        file_consts = {m.group("name"): m.group("value") for m in _CS_CONST_RE.finditer(text)}
        for match in _CS_CLASS_RE.finditer(text):
            body = _class_body(text, match.end())
            consts = {**file_consts, **{m.group("name"): m.group("value") for m in _CS_CONST_RE.finditer(body)}}
            class_id = None
            base_call = _CS_BASE_CALL_RE.search(body)
            if base_call is not None:
                arg = base_call.group("arg")
                if arg.startswith('"'):
                    class_id = arg.strip('"')
                else:
                    class_id = consts.get(arg.split(".")[-1])
            classes[match.group("name")] = {
                "base": match.group("base"),
                "abstract": "abstract" in match.group("mods").split(),
                "excluded": "SimExclude" in match.group("attrs"),
                "id": class_id,
                "on_upgrade": bool(_CS_ON_UPGRADE_RE.search(body)),
            }

    def kind_of(name: str) -> str | None:
        seen = set()
        while name in classes and name not in seen:
            seen.add(name)
            base = classes[name]["base"]
            if base in _SIM_DEF_BASES:
                return _SIM_DEF_BASES[base]
            name = base
        return None

    def inherits_upgrade(name: str) -> bool:
        seen = set()
        while name in classes and name not in seen:
            seen.add(name)
            if classes[name]["on_upgrade"]:
                return True
            name = classes[name]["base"]
        return False

    found: dict[str, set[str]] = {"cards": set(), "encounters": set(), "monsters": set()}
    upgradable: set[str] = set()
    for name, info in classes.items():
        kind = kind_of(name)
        if kind is None or info["abstract"] or info["excluded"] or not info["id"]:
            continue
        found[kind].add(info["id"])
        if kind == "cards" and inherits_upgrade(name):
            upgradable.add(info["id"])
    return SimRegistry(
        encounters=frozenset(found["encounters"]),
        cards=frozenset(found["cards"]),
        upgradable_cards=frozenset(upgradable),
        characters=frozenset(characters),
        source=f"scan:{root}",
    )


def sim_coverage_reasons(
    registry: SimRegistry,
    *,
    character: str,
    encounter_id: str | None,
    deck: Sequence[tuple[str, bool]] | None,
) -> list[str]:
    reasons = []
    if character not in registry.characters:
        reasons.append(f"character_not_ported:{character}")
    if encounter_id is not None and encounter_id not in registry.encounters:
        reasons.append(f"encounter_not_ported:{encounter_id}")
    for card_id, upgraded in deck or ():
        if card_id not in registry.cards:
            reasons.append(f"card_not_ported:{card_id}")
        elif upgraded and card_id not in registry.upgradable_cards:
            reasons.append(f"card_upgrade_not_ported:{card_id}")
    return list(dict.fromkeys(reasons))


# --------------------------------------------------------------------------
# Localized pile names -> card ids
# --------------------------------------------------------------------------


@dataclass
class CardNameMap:
    """zh-localized card name -> (card id, upgraded), learned from hand cards.

    Hand entries carry both the localized ``name`` and the model ``id``; draw /
    discard / exhaust entries carry only the name. Names are keyed per
    character first (every character's Strike is "打击"), then globally.
    A name resolves only when exactly one (id, upgraded) pair was ever seen
    for it; a trailing "+" on an otherwise unseen name falls back to the
    upgraded form of the base name. Anything else is unresolved, never guessed.
    """

    by_character: dict[tuple[str, str], Counter] = field(default_factory=dict)
    global_names: dict[str, Counter] = field(default_factory=dict)

    def observe(self, character: str, hand: Iterable[Mapping[str, Any]]) -> None:
        for card in hand:
            name = card.get("name")
            card_id = card.get("id")
            if not isinstance(name, str) or not isinstance(card_id, str) or not name:
                continue
            pair = (card_id, bool(card.get("is_upgraded")))
            self.by_character.setdefault((character, name), Counter())[pair] += 1
            self.global_names.setdefault(name, Counter())[pair] += 1

    def merge(self, other: CardNameMap) -> None:
        for key, counter in other.by_character.items():
            self.by_character.setdefault(key, Counter()).update(counter)
        for key, counter in other.global_names.items():
            self.global_names.setdefault(key, Counter()).update(counter)

    def _lookup(self, character: str, name: str) -> tuple[str, bool] | None | str:
        own = self.by_character.get((character, name))
        if own:
            return next(iter(own)) if len(own) == 1 else "ambiguous"
        shared = self.global_names.get(name)
        if shared:
            return next(iter(shared)) if len(shared) == 1 else "ambiguous"
        return None

    def resolve(self, character: str, name: str) -> tuple[str, bool] | None:
        found = self._lookup(character, name)
        if isinstance(found, tuple):
            return found
        if found is None:
            plus = _PLUS_SUFFIX_RE.match(name)
            if plus is not None:
                base = self._lookup(character, plus.group(1))
                if isinstance(base, tuple) and not base[1]:
                    return (base[0], True)
        return None

    def to_json(self) -> dict[str, Any]:
        by_character: dict[str, dict[str, list[list[Any]]]] = {}
        for (character, name), counter in sorted(self.by_character.items()):
            by_character.setdefault(character, {})[name] = [
                [card_id, upgraded, count] for (card_id, upgraded), count in sorted(counter.items())
            ]
        return {
            "note": "zh name -> [card id, upgraded, observations] learned from hand entries",
            "by_character": by_character,
            "global": {
                name: [[card_id, upgraded, count] for (card_id, upgraded), count in sorted(counter.items())]
                for name, counter in sorted(self.global_names.items())
            },
        }


# --------------------------------------------------------------------------
# Projection (contract "projection")
# --------------------------------------------------------------------------


def parse_cost(raw: Any) -> int | str | None:
    if isinstance(raw, bool):
        return None
    if isinstance(raw, int):
        return raw
    text = str(raw).strip()
    if text == "X":
        return "X"
    try:
        return int(text)
    except ValueError:
        return None


def parse_attack_label(label: Any) -> tuple[int, int] | None:
    match = _ATTACK_LABEL_RE.match(str(label if label is not None else "").strip())
    if match is None:
        return None
    return int(match.group(1)), int(match.group(2) or 1)


def monster_id_from_entity(entity_id: Any) -> str | None:
    if not isinstance(entity_id, str) or not entity_id:
        return None
    return _ENTITY_SUFFIX_RE.sub("", entity_id)


def _int(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _powers(raw: Any, where: str, issues: list[str]) -> list[dict[str, Any]]:
    powers = []
    for power in raw or ():
        if not isinstance(power, Mapping):
            continue
        power_id = power.get("id")
        amount = _int(power.get("amount"))
        if not isinstance(power_id, str) or amount is None:
            issues.append(f"power_unparsed:{where}:{power_id}")
            continue
        powers.append({"id": power_id, "amount": amount})
    return powers


def _intents(raw: Any, where: str, issues: list[str]) -> list[dict[str, Any]]:
    intents = []
    for intent in raw or ():
        if not isinstance(intent, Mapping):
            continue
        intent_type = str(intent.get("type"))
        projected: dict[str, Any] = {"type": intent_type}
        if intent_type in ATTACK_INTENT_TYPES:
            parsed = parse_attack_label(intent.get("label"))
            if parsed is None:
                issues.append(f"intent_label_unparsed:{where}:{intent.get('label')!r}")
            else:
                projected["attack_value"], projected["attack_times"] = parsed
        elif intent_type == STATUS_CARD_INTENT_TYPE:
            # Soft extra: a StatusIntent's label is its card count; the sim
            # projector emits the same value under this key.
            count = _int(str(intent.get("label") or "").strip())
            if count is not None:
                projected[STATUS_CARD_COUNT_KEY] = count
        intents.append(projected)
    return intents


def _pile_keys(
    entries: Any,
    *,
    character: str,
    names: CardNameMap,
    issues: list[str],
) -> list[str]:
    keys = []
    for entry in entries or ():
        name = entry.get("name") if isinstance(entry, Mapping) else None
        resolved = names.resolve(character, name) if isinstance(name, str) else None
        if resolved is None:
            issues.append(f"unresolved_pile_name:{name}")
            continue
        keys.append(card_key(*resolved))
    return keys


def is_battle_state(state: Any) -> bool:
    return (
        isinstance(state, Mapping)
        and isinstance(state.get("battle"), Mapping)
        and isinstance(state.get("player"), Mapping)
    )


def project_state(
    state: Mapping[str, Any],
    *,
    character: str,
    names: CardNameMap,
    monster_ids: Mapping[int, str] | None = None,
) -> tuple[dict[str, Any], list[str]]:
    """Contract projection of one combat snapshot, plus any problems found.

    Problems (unresolved pile names, unparsed intents, a non-play phase, ...)
    are returned as reason strings; the projection is still built from what
    could be read so a caller can inspect it.
    """
    issues: list[str] = []
    battle = state["battle"]
    player = state["player"]
    turn_side = battle.get("turn")
    in_play = bool(battle.get("is_play_phase"))
    if turn_side == "player" and in_play:
        phase = PHASE_PLAYER_PLAY
    elif turn_side == "player":
        phase = "player_not_play"
    else:
        phase = f"{turn_side}_turn"
    if phase != PHASE_PLAYER_PLAY:
        issues.append(f"pre_state_not_player_play:{phase}")
    player_turn = _int(battle.get("player_turn_number"))
    if player_turn is None:
        issues.append("turn_unparsed")

    hand = []
    for card in player.get("hand") or ():
        cost = parse_cost(card.get("cost"))
        if cost is None or not isinstance(card.get("id"), str):
            issues.append(f"hand_card_unparsed:{card.get('id')}:{card.get('cost')!r}")
            continue
        hand.append(
            {
                "id": card["id"],
                "upgraded": bool(card.get("is_upgraded")),
                "cost": cost,
                "can_play": bool(card.get("can_play")),
            }
        )

    draw = _pile_keys(player.get("draw_pile"), character=character, names=names, issues=issues)
    discard = _pile_keys(player.get("discard_pile"), character=character, names=names, issues=issues)
    exhaust = _pile_keys(player.get("exhaust_pile"), character=character, names=names, issues=issues)
    counts = {
        "draw_count": _int(player.get("draw_pile_count")),
        "discard_count": _int(player.get("discard_pile_count")),
        "exhaust_count": _int(player.get("exhaust_pile_count")),
    }
    for key, listed in (("draw_count", player.get("draw_pile")), ("discard_count", player.get("discard_pile")), ("exhaust_count", player.get("exhaust_pile"))):
        if counts[key] is None:
            issues.append(f"pile_count_unparsed:{key}")
        elif isinstance(listed, list) and len(listed) != counts[key]:
            issues.append(f"pile_count_mismatch:{key}")

    scalars = {}
    for key in ("hp", "max_hp", "block", "energy", "max_energy"):
        scalars[key] = _int(player.get(key))
        if scalars[key] is None:
            issues.append(f"player_field_unparsed:{key}")

    enemies = []
    for enemy in battle.get("enemies") or ():
        if not isinstance(enemy, Mapping):
            continue
        combat_id = _int(enemy.get("combat_id"))
        hp = _int(enemy.get("hp"))
        if combat_id is None or hp is None:
            issues.append("enemy_unparsed")
            continue
        if hp <= 0:
            continue
        from_events = (monster_ids or {}).get(combat_id)
        from_entity = monster_id_from_entity(enemy.get("entity_id"))
        if from_events and from_entity and from_events != from_entity:
            issues.append(f"monster_id_conflict:{combat_id}:{from_events}:{from_entity}")
        monster_id = from_events or from_entity
        if monster_id is None:
            issues.append(f"monster_id_unknown:{combat_id}")
            monster_id = ""
        enemies.append(
            {
                "combat_id": combat_id,
                "monster_id": monster_id,
                "hp": hp,
                "max_hp": _int(enemy.get("max_hp")),
                "block": _int(enemy.get("block")),
                "powers": _powers(enemy.get("status"), f"enemy{combat_id}", issues),
                "intents": _intents(enemy.get("intents"), f"enemy{combat_id}", issues),
            }
        )
    enemies.sort(key=lambda e: e["combat_id"])

    projection = {
        "turn": {"player_turn": player_turn, "phase": phase},
        "player": {
            **scalars,
            "hand": hand,
            **counts,
            "draw_multiset": sorted(draw),
            "discard": discard,
            "exhaust": exhaust,
            "powers": _powers(player.get("status"), "player", issues),
        },
        "enemies": enemies,
    }
    return projection, list(dict.fromkeys(issues))


def snapshot_deck_multiset(
    state: Mapping[str, Any], *, character: str, names: CardNameMap
) -> tuple[Counter, list[str]]:
    """Every card the player owns in combat (hand ids + resolved pile names)."""
    issues: list[str] = []
    player = state["player"]
    keys = [card_key(str(c.get("id")), bool(c.get("is_upgraded"))) for c in player.get("hand") or ()]
    for pile in ("draw_pile", "discard_pile", "exhaust_pile"):
        keys.extend(_pile_keys(player.get(pile), character=character, names=names, issues=issues))
    return Counter(keys), issues


# --------------------------------------------------------------------------
# Deck reconstruction (contract "deck": starter order + A5 bane + Neow edits)
# --------------------------------------------------------------------------


def base_deck(character: str, ascension: int) -> list[tuple[str, bool]] | None:
    starter = STARTER_DECKS.get(character)
    if starter is None:
        return None
    deck = [(card_id, False) for card_id in starter]
    if ascension >= ASCENDERS_BANE_LEVEL:
        deck.append((ASCENDERS_BANE, False))
    return deck


def _neow_lists(stats: Mapping[str, Any]) -> dict[str, list[Any]]:
    picks = [
        strip_prefix(choice.get("choice"), "RELIC.")
        for choice in stats.get("relic_choices") or ()
        if isinstance(choice, Mapping) and choice.get("was_picked")
    ]
    gains = []
    for card in stats.get("cards_gained") or ():
        gains.append(
            {
                "id": strip_prefix(card.get("id"), "CARD."),
                "enchanted": bool(card.get("enchantment")),
            }
        )
    transforms = [
        (
            strip_prefix((t.get("original_card") or {}).get("id"), "CARD."),
            strip_prefix((t.get("final_card") or {}).get("id"), "CARD."),
        )
        for t in stats.get("cards_transformed") or ()
        if isinstance(t, Mapping)
    ]
    removals = [strip_prefix(r.get("id"), "CARD.") for r in stats.get("cards_removed") or () if isinstance(r, Mapping)]
    upgrades = [strip_prefix(u, "CARD.") for u in stats.get("upgraded_cards") or ()]
    return {"picks": picks, "gains": gains, "transforms": transforms, "removals": removals, "upgrades": upgrades}


def _pick_one(
    deck: list[tuple[str, bool]],
    candidates: list[int],
    apply,
    what: str,
    issues: list[str],
) -> int | None:
    """Choose one of several player-chosen target positions.

    The recorded history names the card only by id, so the choice is exact
    only when every candidate gives the same resulting deck order.
    """
    if not candidates:
        issues.append(f"neow_target_missing:{what}")
        return None
    outcomes = {tuple(apply(list(deck), index)) for index in candidates}
    if len(outcomes) > 1:
        issues.append(f"deck_order_ambiguous:{what}")
    return candidates[0]


def _remove_at(deck: list[tuple[str, bool]], index: int) -> list[tuple[str, bool]]:
    del deck[index]
    return deck


def _upgrade_at(deck: list[tuple[str, bool]], index: int) -> list[tuple[str, bool]]:
    deck[index] = (deck[index][0], True)
    return deck


def reconstruct_deck(
    character: str,
    ascension: int,
    neow_stats: Mapping[str, Any] | None,
) -> tuple[list[tuple[str, bool]] | None, list[str]]:
    """Deck at first-combat entry, in the game's Deck order.

    Starter deck in character-model order, ``ASCENDERS_BANE`` appended at A5+,
    then the floor-1 Neow edits in pickup order: deck additions append at the
    bottom (``CardPileCmd.Add`` default ``CardPilePosition.Bottom``), upgrades
    are in place, removals delete in place, and a deck transform deletes the
    original in place and appends the replacement (``CardCmd.Transform``).
    """
    deck = base_deck(character, ascension)
    if deck is None:
        return None, [f"character_unknown:{character}"]
    if neow_stats is None:
        return deck, ["neow_summary_missing"]
    lists = _neow_lists(neow_stats)
    issues: list[str] = []
    picks = lists["picks"]
    gains = list(lists["gains"])
    transforms = list(lists["transforms"])
    removals = list(lists["removals"])
    upgrades = list(lists["upgrades"])
    if any(g["enchanted"] for g in gains):
        issues.append("neow_card_enchanted")

    for relic in picks:
        if relic not in DECK_OP_RELICS | NO_DECK_OP_RELICS | VARIABLE_GAIN_RELICS | set(FIXED_GAIN_RELICS):
            issues.append(f"neow_relic_unmodelled:{relic}")

    bones = picks.count("NEOWS_BONES")
    fixed = sum(FIXED_GAIN_RELICS.get(relic, 0) for relic in picks)
    variable = [relic for relic in picks if relic in VARIABLE_GAIN_RELICS]
    remaining = len(gains) - fixed - bones
    gain_counts: dict[int, int] = {}
    for position, relic in enumerate(picks):
        if relic in FIXED_GAIN_RELICS:
            gain_counts[position] = FIXED_GAIN_RELICS[relic]
    if variable:
        first_variable = picks.index(variable[0])
        gain_counts[first_variable] = remaining
        for position, relic in enumerate(picks):
            if relic in VARIABLE_GAIN_RELICS and position != first_variable:
                gain_counts[position] = 0
        if len(variable) > 1:
            last_variable = max(i for i, r in enumerate(picks) if r in VARIABLE_GAIN_RELICS)
            between = picks[first_variable + 1 : last_variable]
            if any(r in DECK_OP_RELICS - {"NEOWS_BONES"} for r in between):
                issues.append("deck_order_ambiguous:neow_gain_split")
    elif remaining != 0:
        issues.append(f"neow_gain_count_mismatch:{len(gains)}")

    def take_gains(count: int) -> None:
        for _ in range(max(count, 0)):
            if not gains:
                issues.append("neow_gain_count_mismatch:short")
                return
            deck.append((gains.pop(0)["id"], False))

    for position, relic in enumerate(picks):
        if position in gain_counts:
            take_gains(gain_counts[position])
        if relic == "NEOWS_TALISMAN":
            for family in (BASIC_STRIKES, BASIC_DEFENDS):
                targets = [i for i, (cid, _) in enumerate(deck) if cid in family]
                if not targets:
                    issues.append("neow_target_missing:talisman")
                    continue
                index = targets[-1]
                deck[index] = (deck[index][0], True)
                if deck[index][0] in upgrades:
                    upgrades.remove(deck[index][0])
                else:
                    issues.append(f"neow_upgrade_unrecorded:{deck[index][0]}")
        elif relic == "LEAFY_POULTICE":
            originals = []
            for family in (BASIC_STRIKES, BASIC_DEFENDS):
                targets = [i for i, (cid, _) in enumerate(deck) if cid in family]
                if targets:
                    originals.append(targets[0])
            finals = []
            for index in sorted(originals):
                original_id = deck[index][0]
                match = next((t for t in transforms if t[0] == original_id), None)
                if match is None:
                    issues.append(f"neow_transform_unrecorded:{original_id}")
                    continue
                transforms.remove(match)
                finals.append(match[1])
            for index in sorted(originals, reverse=True):
                del deck[index]
            deck.extend((final, False) for final in finals)
        elif relic == "NEW_LEAF":
            if not transforms:
                issues.append("neow_transform_unrecorded:new_leaf")
                continue
            original_id, final_id = transforms.pop(0)
            candidates = [i for i, (cid, _) in enumerate(deck) if cid == original_id]
            index = _pick_one(deck, candidates, _remove_at, f"transform:{original_id}", issues)
            if index is not None:
                del deck[index]
                deck.append((final_id, False))
        elif relic in REMOVAL_COUNTS:
            for _ in range(REMOVAL_COUNTS[relic]):
                if not removals:
                    issues.append(f"neow_removal_unrecorded:{relic}")
                    break
                target = removals.pop(0)
                candidates = [i for i, (cid, _) in enumerate(deck) if cid == target]
                index = _pick_one(deck, candidates, _remove_at, f"remove:{target}", issues)
                if index is not None:
                    del deck[index]
        elif relic == "POMANDER":
            if not upgrades:
                issues.append("neow_upgrade_unrecorded:pomander")
                continue
            target = upgrades.pop(0)
            candidates = [i for i, (cid, up) in enumerate(deck) if cid == target and not up]
            index = _pick_one(deck, candidates, _upgrade_at, f"upgrade:{target}", issues)
            if index is not None:
                deck[index] = (target, True)
    # NeowsBones.AfterObtained adds its curse after the offered relics resolve.
    take_gains(bones)
    if gains or transforms or removals or upgrades:
        issues.append("neow_changes_unattributed")
    return deck, list(dict.fromkeys(issues))


def deck_multiset(deck: Sequence[tuple[str, bool]]) -> Counter:
    return Counter(card_key(card_id, upgraded) for card_id, upgraded in deck)


def multiset_delta(expected: Counter, observed: Counter) -> str:
    missing = expected - observed
    extra = observed - expected
    parts = []
    if missing:
        parts.append("missing=" + ",".join(f"{k}x{v}" for k, v in sorted(missing.items())))
    if extra:
        parts.append("extra=" + ",".join(f"{k}x{v}" for k, v in sorted(extra.items())))
    return ";".join(parts)


# --------------------------------------------------------------------------
# Decisions
# --------------------------------------------------------------------------


def _hand_key(card: Mapping[str, Any]) -> tuple[Any, ...]:
    return (card.get("id"), bool(card.get("is_upgraded")), str(card.get("cost")))


# Hand-entry fields that do not describe the card instance: its position, and the rendered
# description, which carries the UI's damage preview of the card being targeted (corpus:
# 1786880759-X20BJH1S4R seq 110, two plain Strikes read "6" and "9" damage against a
# Vulnerable enemy). Every other recorded field (name, keywords = hover tips with
# enchantments / afflictions / added keywords, star cost, playability, ...) takes part.
_NON_INSTANCE_HAND_FIELDS = frozenset({"index", "description"})


def _instance_view(card: Mapping[str, Any]) -> tuple[tuple[str, str], ...]:
    """Everything the snapshot shows about one hand card instance."""
    return tuple(
        sorted(
            (str(field), json.dumps(value, sort_keys=True, ensure_ascii=False, default=str))
            for field, value in card.items()
            if field not in _NON_INSTANCE_HAND_FIELDS
        )
    )


def resolve_hand_index(
    pre_hand: Sequence[Mapping[str, Any]],
    card_id: str,
    post_hand: Sequence[Mapping[str, Any]] | None,
) -> tuple[int | None, str | None]:
    """Hand position of the played card instance in the pre-action hand.

    ``combat_card_index`` is a NetCombatCardDb id (ids are handed out at
    combat setup in AllPiles order, then to new cards), not a hand position;
    snapshots carry no instance ids. The position is therefore resolved from
    the visible hands: the candidates are the hand cards with the played
    model id; copies whose removal leaves a hand equal in every recorded
    instance field (``_instance_view``) are interchangeable (pick the first);
    otherwise the post-action snapshot, taken once the card has left the
    hand, must single out one removal: the whole instance view first, then
    (id, upgraded, cost) exactly, without cost, and as a prefix; a match on
    the shorter key counts only when the copies it cannot tell apart are
    interchangeable. Returns (index, None) or (None, reason).
    """
    candidates = [i for i, card in enumerate(pre_hand) if card.get("id") == card_id]
    if not candidates:
        return None, f"played_card_not_in_pre_hand:{card_id}"
    views = [_instance_view(card) for card in pre_hand]

    def rest(items: Sequence[Any], index: int) -> tuple[Any, ...]:
        return tuple(items[:index]) + tuple(items[index + 1 :])

    by_view: dict[tuple[Any, ...], list[int]] = {}
    for index in candidates:
        by_view.setdefault(rest(views, index), []).append(index)
    if len(by_view) == 1:
        return candidates[0], None
    if post_hand is not None:
        post_views = tuple(_instance_view(card) for card in post_hand)
        exact = [indices for remaining, indices in by_view.items() if remaining == post_views]
        if len(exact) == 1:
            return exact[0][0], None
        # Visible key match (id, upgraded, cost), then without cost (the played
        # card can change costs), then as a prefix (it can draw).
        keys = [_hand_key(card) for card in pre_hand]
        outcomes: dict[tuple[Any, ...], list[int]] = {}
        for index in candidates:
            outcomes.setdefault(rest(keys, index), []).append(index)
        full_post = [_hand_key(card) for card in post_hand]
        loose_post = [key[:2] for key in full_post]
        tests = (
            lambda outcome: list(outcome) == full_post,
            lambda outcome: [key[:2] for key in outcome] == loose_post,
            lambda outcome: loose_post[: len(outcome)] == [key[:2] for key in outcome],
        )
        for test in tests:
            consistent = [indices for outcome, indices in outcomes.items() if test(outcome)]
            if len(consistent) == 1:
                group = consistent[0]
                if len({rest(views, index) for index in group}) == 1:
                    return group[0], None
                break  # copies the visible key cannot tell apart differ in another field
            if consistent:
                break
    return None, f"hand_index_ambiguous:{card_id}"


# Event entries the roll-forward can apply to a projection (see roll_forward).
ROLL_FORWARD_ENTRIES = frozenset(
    {
        "damage_received",
        "creature_attacked",
        "block_gained",
        "power_received",
        "card_play_finished",
        "card_exhausted",
    }
)


# Powers whose change cannot alter a displayed attack intent: an enemy's
# Vulnerable only scales damage it receives; the player's Strength and
# Dexterity only scale the player's own damage and block.
ENEMY_POWERS_NEUTRAL_TO_OWN_INTENTS = frozenset({"VULNERABLE_POWER"})
PLAYER_POWERS_NEUTRAL_TO_INTENTS = frozenset({"STRENGTH_POWER", "DEXTERITY_POWER"})


def _has_attack_intent(enemy: Mapping[str, Any]) -> bool:
    return any(intent.get("type") in ATTACK_INTENT_TYPES for intent in enemy.get("intents") or ())


# Pending entries that never move a card into or out of the hand.
HAND_SAFE_ENTRIES = frozenset(
    {
        "damage_received",
        "creature_attacked",
        "block_gained",
        "power_received",
        "energy_spent",
        "orb_channeled",
        "stars_modified",
        "summoned",
    }
)


def gap_keeps_hand(
    events: Sequence[tuple[str, Mapping[str, Any]]],
    *,
    played_card: tuple[str, bool] | None,
) -> bool:
    """True when no event between a snapshot and the next action can have
    changed the hand, so the snapshot hand is the hand the action was taken
    from (its ``hand_index`` stays valid even if the rest is unobservable).
    The previous card's own ``card_play_finished`` / ``card_exhausted`` only
    move it out of the (unprojected) play pile, unless it returns to hand.
    """
    exhaust_expected = False
    for entry, data in events:
        if entry in NON_STATE_EVENT_ENTRIES or entry in HAND_SAFE_ENTRIES:
            continue
        data = data if isinstance(data, Mapping) else {}
        if entry == "card_play_finished":
            card_play = data.get("card_play") if isinstance(data.get("card_play"), Mapping) else {}
            card = card_play.get("card") if isinstance(card_play.get("card"), Mapping) else {}
            if (
                played_card is not None
                and strip_prefix(card.get("id"), "CARD.") == played_card[0]
                and card_play.get("result_pile") != "Hand"
            ):
                exhaust_expected = card_play.get("result_pile") == "Exhaust"
                continue
            return False
        if entry == "card_exhausted":
            card = data.get("card") if isinstance(data.get("card"), Mapping) else {}
            if exhaust_expected and played_card is not None and strip_prefix(card.get("id"), "CARD.") == played_card[0]:
                exhaust_expected = False
                continue
            return False
        return False
    return True


def event_creatures(node: Any) -> Iterable[Mapping[str, Any]]:
    """Creature references (combat_id + hp/max_hp/block) inside an event payload."""
    if isinstance(node, Mapping):
        if isinstance(node.get("combat_id"), int) and "hp" in node and "is_player" in node:
            yield node
        for value in node.values():
            yield from event_creatures(value)
    elif isinstance(node, list):
        for value in node:
            yield from event_creatures(value)


def visible_card_total(projection: Mapping[str, Any]) -> int | None:
    """Cards in hand + draw + discard + exhaust (the play pile is not projected)."""
    player = projection["player"]
    counts = [player.get("draw_count"), player.get("discard_count"), player.get("exhaust_count")]
    if any(not isinstance(c, int) for c in counts):
        return None
    return len(player["hand"]) + sum(counts)


def roll_forward(
    projection: Mapping[str, Any],
    events: Sequence[tuple[str, Mapping[str, Any]]],
    *,
    played_card: tuple[str, bool] | None,
    visible_powers: frozenset[str] | set[str],
    played_in_limbo: bool | None = None,
    played_destination: str | None = None,
) -> tuple[dict[str, Any] | None, str | None]:
    """Bring a mid-resolution snapshot projection to the decision point.

    The recorder's post-action snapshot is taken as soon as a played card
    leaves the hand, before its effects resolve, and that snapshot is the
    ``state_seq`` of the next decision whenever no other snapshot follows.
    The events recorded between the snapshot and the next action are the
    rest of that resolution; this applies them:

    - every creature reference in an event carries that creature's hp /
      max_hp / block after the event (last one wins);
    - ``power_received`` adds its signed amount to the receiving creature's
      power (powers never seen in any corpus snapshot are invisible and
      skipped; a power reaching 0 is removed);
    - the previous decision's card is moved from the (unprojected) play pile
      to its ``card_play_finished`` ``result_pile`` (Discard / Exhaust;
      None = a power card, gone) when ``played_in_limbo`` says the snapshot
      still lacks it. The caller derives that from card counts, not from the
      event order: the recorder can write ``card_play_finished`` /
      ``card_exhausted`` after a snapshot that already shows the move;
    - enemies at 0 hp leave the projection (dead enemies are dropped).

    Any other event (draws, discards, summons, orbs, generated cards, a power
    that can change an attack intent's damage preview, ...) makes the state
    at the decision unrecoverable: returns (None, reason).
    """
    out: dict[str, Any] = {
        "turn": dict(projection["turn"]),
        "player": {
            **projection["player"],
            "hand": [dict(card) for card in projection["player"]["hand"]],
            "draw_multiset": list(projection["player"]["draw_multiset"]),
            "discard": list(projection["player"]["discard"]),
            "exhaust": list(projection["player"]["exhaust"]),
            "powers": [dict(p) for p in projection["player"]["powers"]],
        },
        "enemies": [
            {
                **enemy,
                "powers": [dict(p) for p in enemy["powers"]],
                "intents": [dict(i) for i in enemy["intents"]],
            }
            for enemy in projection["enemies"]
        ],
    }
    enemies = {enemy["combat_id"]: enemy for enemy in out["enemies"]}
    seen_finish = seen_exhaust = False
    for entry, data in events:
        if entry in NON_STATE_EVENT_ENTRIES:
            continue
        if entry not in ROLL_FORWARD_ENTRIES or not isinstance(data, Mapping):
            return None, f"pending_{entry}"
        if entry in ("card_play_finished", "card_exhausted"):
            # Only the previous decision's own card may finish here; where it
            # ends up is decided below from the card counts, because the
            # recorder can write these history events after a snapshot that
            # already shows the pile move.
            card_play = data.get("card_play") if isinstance(data.get("card_play"), Mapping) else {}
            source = card_play if entry == "card_play_finished" else data
            card = source.get("card") if isinstance(source.get("card"), Mapping) else {}
            repeat = seen_finish if entry == "card_play_finished" else seen_exhaust
            if (
                repeat
                or played_card is None
                or card_play.get("is_auto_play")
                or strip_prefix(card.get("id"), "CARD.") != played_card[0]
            ):
                return None, f"pending_{entry}_unmatched"
            if played_in_limbo is None:
                return None, "pending_pile_move_unresolved"
            if entry == "card_play_finished":
                seen_finish = True
            else:
                seen_exhaust = True
        if entry == "power_received":
            actor = data.get("actor") if isinstance(data.get("actor"), Mapping) else {}
            power_id = strip_prefix(data.get("power"), "POWER.")
            delta = _int(data.get("amount"))
            if delta is None:
                return None, "pending_power_unparsed"
            holder = None
            if actor.get("is_player") is True:
                holder = out["player"]
                affects_intents = power_id not in PLAYER_POWERS_NEUTRAL_TO_INTENTS and any(
                    _has_attack_intent(enemy) for enemy in out["enemies"]
                )
            elif isinstance(actor.get("combat_id"), int):
                holder = enemies.get(actor["combat_id"])
                affects_intents = (
                    holder is not None
                    and power_id not in ENEMY_POWERS_NEUTRAL_TO_OWN_INTENTS
                    and _has_attack_intent(holder)
                )
            else:
                affects_intents = False
            if delta != 0 and affects_intents:
                # Intent labels are damage previews (Hook.ModifyDamage against
                # the player); a power that can change them cannot be replayed
                # here without the damage pipeline.
                return None, f"pending_power_changes_intent:{power_id}"
            if holder is not None and power_id in visible_powers:
                powers = holder["powers"]
                existing = next((p for p in powers if p["id"] == power_id), None)
                if existing is None:
                    if delta != 0:
                        powers.append({"id": power_id, "amount": delta})
                else:
                    existing["amount"] += delta
                    if existing["amount"] == 0:
                        powers.remove(existing)
        for creature in event_creatures(data):
            target = out["player"] if creature.get("is_player") is True else enemies.get(creature["combat_id"])
            if target is None:
                continue
            for key in ("hp", "max_hp", "block"):
                value = _int(creature.get(key))
                if value is not None:
                    target[key] = value
    if played_in_limbo:
        if played_card is None:
            return None, "pending_pile_move_unresolved"
        if played_destination == "Discard":
            out["player"]["discard"].append(card_key(*played_card))
            out["player"]["discard_count"] += 1
        elif played_destination == "Exhaust":
            out["player"]["exhaust"].append(card_key(*played_card))
            out["player"]["exhaust_count"] += 1
        elif played_destination != "None":
            return None, f"pending_card_play_finished_{played_destination}"
    out["enemies"] = [enemy for enemy in out["enemies"] if (enemy.get("hp") or 0) > 0]
    return out, None


def extract_monster_ids(events: Iterable[Mapping[str, Any]]) -> dict[int, str]:
    """combat_id -> monster model id from creature references in events."""
    found: dict[int, str] = {}

    def visit(node: Any) -> None:
        if isinstance(node, Mapping):
            model = node.get("model_id")
            combat_id = node.get("combat_id")
            if (
                isinstance(model, str)
                and model.startswith("MONSTER.")
                and node.get("is_player") is False
                and isinstance(combat_id, int)
            ):
                found.setdefault(combat_id, model[len("MONSTER.") :])
            for value in node.values():
                visit(value)
        elif isinstance(node, list):
            for value in node:
                visit(value)

    for event in events:
        visit(event)
    return found
