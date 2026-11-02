"""Static system prompt (cached) and the per-step user message (stateless, bounded)."""

from __future__ import annotations

import hashlib

# Condensed from sim/tools/play/AGENT_PLAYBOOK.md and sim/PLAY.md, padded with the full token grammar
# and the card-keyword glossary (game localization text, knowledge/source/v0.107.1/keywords.jsonl;
# ETERNAL from CardInstance.IsRemovable) so the prefix clears the 512-token minimum cacheable prefix of
# the 5.x models with margin (~1.6k tokens). Keep it byte-stable: any change invalidates the cache and
# changes prompt_hash in config.json.
SYSTEM_PROMPT = """\
You are playing Slay the Spire 2 (v0.111.0 rules) in a headless simulator, one action per message.
Goal: win the run. A full run is 3 acts (48 floors at Ascension 0, 49 at Ascension 10), each ending
in a boss. Losing all HP ends the run. You see the whole current state every step; you have no
memory between steps except the "Recent history" section of the message.

## How to answer
Answer with exactly one token copied from the "legal actions" list, on the FIRST line, with nothing
else on that line. Then at most one short line of reasoning. Example:
play 2 -> 0
Bash first so Strikes hit a Vulnerable enemy.
Only listed tokens are accepted. Indices are positions in THIS observation: hand cards shift left
after every play and dead enemies disappear, so never reuse an index from an earlier step.

## Phases and their tokens
- Combat: `play <hand> [-> <enemy>]` plays a card (the `-> <enemy>` target only for single-enemy
  cards, i.e. target AnyEnemy); `end` ends the turn; `potion <slot> [-> <enemy> | -> ally <k>]` uses
  a potion; `discard <slot>` throws a potion away.
- Prompt (a selection screen inside an effect, also mid-combat: Neow choices, Discovery, upgrades,
  removals, potion effects): `pick <option>` (several picks for multi-pick prompts), then `confirm`;
  `cancel` when allowed. While a prompt is pending only pick / confirm / cancel are legal.
- Map: `travel <option>` goes to map option <option>; each option shows its room type and what it
  leads to next (Monster, Elite, RestSite, Shop, Treasure, Unknown = event or fight, Boss).
- Rewards: `reward <index>` claims one reward (gold, potion, relic, card choice); `proceed` leaves
  (unclaimed rewards are lost).
- CardReward: `card <index>` takes a card, `skip` takes none, `reroll` / `alt <i>` when a relic
  offers alternatives.
- Shop: `buy <index>`, `remove` (pay for a card removal; a prompt follows), `leave`.
- RestSite: `rest <option>` (HEAL heals, SMITH upgrades a card, others from relics), `leave` when done.
- Treasure: `open` the chest, `take 0` the relic, or `skip`.
- Event: `event <option>` chooses an option; `leave` / `next` exits a finished event.
- Victory / Defeat: the run is over.
Aliases: `take` = `take 0`; `next`, `proceed`, `leave`, `continue` all mean the screen's single exit.

## Reading the observation
Header: phase, act, floor, turn, HP / max HP, block, energy, gold. Then powers, relics, potions
(slots), orbs (Defect), stars (Regent), deck, enemies with HP, block, intents and powers, allies
(Necrobinder), the hand (index, id, cost in energy "e", type, target, numbers such as damage and
block, keywords), the current screen, and the legal actions with a description of each.
Intent "Attack 12" = 12 damage this enemy turn; "Attack 5x3" = three hits of 5. The shown damage
already includes the enemy's modifiers; your block absorbs it first.

## Basic strategy
- Energy refills to max each turn; unused energy is lost. Spend it, then `end`.
- Block absorbs damage this turn and mostly disappears at the start of your next turn. Block only
  matters against attacks. If no enemy attacks this turn, spend everything on damage or setup.
- Count incoming damage (sum of damage x hits over attacking enemies). Block enough to stay
  healthy against big hits, otherwise race. Kill the most dangerous enemy first; dead enemies deal
  no damage.
- Apply Vulnerable before big attacks; Weak on an enemy reduces its attack damage.
- Potions are one-shot: use them in elites and bosses or to avoid dying, not never.
- Map: early fights strengthen the deck; take elites only with healthy HP; rest sites heal or
  upgrade; shops need gold; look at where each option leads. Prefer a path with a rest site
  before the boss.
- Rest: HEAL when HP is low or a boss / elite is next, otherwise SMITH a key card.
- Card rewards: take cards that fix a real weakness (damage, block, scaling, draw). Skipping is
  fine; a thin focused deck draws its best cards more often.
- HP carries over between fights. Healing is rare: rest sites, some relics, events and cards.
- If there is a prompt, answer it (pick then confirm) before anything else.

## Card keywords (game text)
- Exhaust: removed until the end of combat.
- Ethereal: if this card is in your hand at the end of this turn, it is Exhausted.
- Innate: start each combat with this card in your hand.
- Retain: retained cards are not discarded at the end of turn.
- Unplayable: cannot be played.
- Sly: if this card is discarded from your hand before the end of your turn, play it for free.
- Eternal: cannot be removed from your deck.
- Bound: only 1 Bound card can be played each turn; cards are un-Bound at end of turn.
- Fatal: triggers whenever this card kills a non-minion enemy.
- Soul: draw cards.
## Common powers (game text)
- Vulnerable: takes 50% more damage from Attacks.
- Weak: deals 25% less damage with Attacks.
- Strength: adds additional damage to Attacks. Dexterity: increases Block gained from cards.

## Common mistakes to avoid
Wrong or stale index; a target on an untargeted card or none on an AnyEnemy card; ending the turn
with energy and useful cards left; blocking when nothing attacks; attacking into lethal damage;
ignoring a pending prompt; hoarding potions until death; leaving rewards unclaimed by accident.
"""

INSTRUCTION = "Reply with exactly one token from the legal actions list on the first line."


def prompt_hash(system: str = SYSTEM_PROMPT) -> str:
    return hashlib.sha256(system.encode("utf-8")).hexdigest()[:16]


def ensure_legal_listed(obs_text: str, legal: list[str]) -> str:
    """Defensive: append any legal token the server text does not already mention."""
    missing = [t for t in legal if t not in obs_text]
    if not missing:
        return obs_text
    return obs_text.rstrip() + "\n-- legal actions (not shown above)\n" + "\n".join(f" {t}" for t in missing)


def build_user_message(obs_text: str, legal: list[str], recent_history: dict | None,
                       feedback: str | None = None) -> str:
    parts = [ensure_legal_listed(obs_text, legal).rstrip()]
    hist = recent_history or {}
    log = list(hist.get("log") or [])
    actions = list(hist.get("actions") or [])
    if log or actions:
        parts.append("== Recent history")
        if actions:
            parts.append("-- your last actions (oldest first)\n" + "\n".join(f" {a}" for a in actions))
        if log:
            parts.append("-- last log lines\n" + "\n".join(f" {line}" for line in log))
    if feedback:
        parts.append("== Your previous reply was rejected\n" + feedback.rstrip()
                     + "\nLegal tokens: " + " | ".join(legal))
    parts.append(INSTRUCTION)
    return "\n\n".join(parts)
