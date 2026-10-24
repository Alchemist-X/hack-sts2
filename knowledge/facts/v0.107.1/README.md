# STS2 fact DB — game v0.107.1

Structured, version-tagged **facts** (not strategy) for Slay the Spire 2, meant as priors for the LLM agent.
Every entry carries `sources[]` (URL + fetch date). Values are normalized/derived by us; no game assets or raw
extracted game files are committed.

**Authority order: game v0.107.1 (sts2.dll + localization, read-only) > slaythespire.wiki.gg > spire-codex.com.**
When a wiki/codex value disagrees, the game value is kept in the main fields, the other value under the entry's
`conflicts[]` (with `"authoritative": "game"`), and the item is also listed in `conflicts.json`.

## Files

| file | contents |
|---|---|
| `cards.json` | 585 cards (all characters + colorless/status/curse/event/token). cost (`int`, `"X"`, `"unplayable"`), upgraded cost, type, rarity, target, keywords, normalized description + upgraded description, vars, upgrade deltas |
| `relics.json` | 298 relics: rarity, pools, description, vars |
| `potions.json` | 65 potions: rarity, usage, target, description |
| `enemies.json` | 114 monsters: HP (+ascension HP, multi-form HP), starting powers, 330 moves (intents, effects with base/ascension values, follow-up), branch logic, readable `pattern` (incl. interrupts); 88 encounters; acts → encounter lists |
| `bosses.json` | 12 boss + 12 elite encounters with embedded monsters, patterns, and wiki pattern notes (e.g. Queen, Torch Head Amalgam) |
| `events.json` | 66 events incl. 8 Ancients: appearance condition (normalized pseudo-expression), vars, options (title + effect text), wiki options |
| `keywords.json` | 14 card keywords, 260 powers (buff/debuff, stacking), 7 afflictions, 22 enchantments, 12 status cards, curse cards, ascension levels |
| `conflicts.json` | all game-vs-wiki/codex disagreements |
| `stats.json` | coverage counts |
| `schema.json` | JSON Schema (draft 2020-12) for every file / entry type |

Description notation: `[E]` = energy icon, `N[E]` = N energy, `[Star]` = star icon, `X` = value computed at runtime
(listed in `dynamic_vars`). Ascension values appear as `{"base": .., "asc": .., "asc_level": ..}`.

## Coverage (vs game entity lists)

| category | game entries | with wiki match | with codex match | wiki-only (not in game) |
|---|---|---|---|---|
| cards | 585 (Ironclad 87) | 576 | 577 | 19 |
| relics | 298 | 296 | — (used only for cards) | 2 |
| potions | 65 | 63 | — | 1 |
| enemies | 114 (330 moves, 88 encounters) | 104 | — | 6 |
| events (incl. ancients) | 66 (8) | 65 | — | 0 |

Unmatched game entries are internal/test models (MOCK_*, DEPRECATED_*, BIG_DUMMY, TEN_HP_MONSTER, the Adversary MK
test monsters, …) plus a few with no wiki page (SCARE, ARCHITECT, BYRDPIP, OSTY, PAELS_LEGION).
Wiki-only names (likely removed/renamed in 0.107.1 or from a newer patch): cards Abundance, Blade Symphony, Blaze,
Cacophony, Concoct, Constellation, Dowsing, Fade, Hibernate, Imitation Learning, Midnight, One for All, Outrage,
Plot, Sidestep, Soulbound, The Ball, Tutor, Underworld; relics Dowsing Rod, Neow's Sacrifice; potion Ambergris;
enemies Doormaker, Knight Gang, Mysterious Knight (Test Subject phases are covered by `TEST_SUBJECT.hp.forms`).

## Sources (all fetched 2026-10-23)

- **Game** v0.107.1 at `$STS2_GAME_INSTALL_DIR` (read-only): repo exporter output `knowledge/source/v0.107.1/*.jsonl`,
  decompiled `sts2.dll` (git-ignored, `artifacts/knowledge/decompiled-v0.107.1/`), English localization from the PCK.
- **slaythespire.wiki.gg** (MediaWiki `api.php`): Cargo tables Cards, Relics, Potions, Enemies, Encounters, Intents,
  Keywords, Powers; Lua data modules `Module:Enemies/StS2 data/*`, Events, Ancients, Ascension; boss/elite pages
  (Pattern / Notes sections). ~1 req/s, cached. robots.txt Content-Signal says `ai-train=no, use=reference`:
  wiki text is used only as *reference priors* with attribution (URL per entry); prose is truncated to ≤300 chars.
- **spire-codex.com** API (`sts2_tested` = v0.103.3): cards, monsters, encounters, events, powers… used as a
  secondary cross-check (agrees with the game almost everywhere: 1 card conflict).
- Not used: Bilibili wiki (blocked), Fandom (stale/no StS2 structured data).

## Conflicts: 158 (game authoritative)

| bucket | count |
|---|---|
| cards vs wiki | 111 (description numbers 45, upgraded numbers 45, rarity 12, cost 7, type 2, upgraded cost 1) |
| cards vs codex | 1 |
| relics vs wiki | 30 (description numbers 20, rarity 10) |
| potions vs wiki | 4 (mostly rarity taxonomy Event/Token vs wiki "Special") |
| enemies vs wiki | 11 (HP 7, move damage 4) |
| events vs wiki | 1 |

Examples (game → wiki):
- Enemies: AEONGLASS Ebb damage 26 (A9 32) → 22 (26) (codex agrees with game); TORCH_HEAD_AMALGAM Tackle A-damage
  19 → 22 (wiki splits Strong/Weak Tackle, game titles all "Tackle"); VANTOM Dismember 26 → 27; OWL_MAGISTRATE HP
  231/247 → 234/243; SLIMED_BERSERKER 261/281 → 266/276; SCROLL_OF_BITING 30-37 / 33-39 → 31-38 / 32-39; AXEBOT
  asc HP 76-86 → 78-86.
- Cards: DEMON_FORM Strength 2(+3) → 3(+4); DEVASTATE 30/40 → 35/45; COLOSSUS 5/8 → 4/7; BEACON_OF_HOPE cost 1 → 2;
  OUTBREAK Power/1/Uncommon → Skill/3/Rare; ACCELERANT Rare → Uncommon; EIDOLON, EXPECT_A_FIGHT, EXPERTISE,
  WELL_LAID_PLANS have reworked text on the wiki.
- Relics: SIGNET_RING 999 → 888; SERE_TALON / DISTINGUISHED_CAPE effects swapped; FAKE_* relics are Event rarity with
  weaker values in game vs the original relic on the wiki; DIAMOND_DIADEM, BEAUTIFUL_BRACELET numbers differ.
- Events: ENDLESS_CONVEYOR — game exposes one option per conveyor item, wiki has a single "Grab Something Off the Belt".

The pattern suggests the wiki tracks a different (likely newer) patch than 0.107.1 for part of its pages.

## Known gaps / caveats

- 23 cards have runtime-computed vars (shown as `X`, listed in `dynamic_vars`); some enemy moves apply `Strength X`
  (e.g. Queen's Burn Bright for Me buff is computed); event values that are rolled at runtime are `X`.
- Some move titles are derived from the move id when no localized title exists (`title_derived_from_id: true`).
- Enemy AI is exported as a move graph (`next`, `branches`, readable `pattern` incl. death/interrupt overrides); random-branch
  weights are included where statically known, otherwise only the candidate set.
- Wiki prose (pattern notes, event outcomes) is truncated and unstructured; game options/vars are authoritative.
- spire-codex tracks v0.103.3, not 0.107.1.
- Not validated against a live game run yet (lookup was tested on recorded live-state fixtures).
- One trivial false positive remains: LIQUID_MEMORIES (wiki "0[E]" number vs none in game text).

## Live harness lookup

```
python3 scripts/facts/lookup.py --state path/to/state.json      # facts for everything visible in a live state
python3 scripts/facts/lookup.py --id QUEEN --id BASH --id SLIPPERY_BRIDGE
python3 scripts/facts/lookup.py --state s.json --json            # structured
```
Library: `FactDB.load("0.107.1").facts_for_state(state, max_chars=6000)`.
Integration point (not wired, `scripts/sts2_live.py` is being edited by another worker): in `snapshot()`, next to
`result['decision_context']`, add `result['wiki_facts'] = FactDB.load(version).facts_for_state(state, max_chars=4000)`.

## Regenerate

```
bash scripts/facts/run_all.sh --version 0.107.1            # fetch (cached) + extract + build
bash scripts/facts/run_all.sh --version 0.107.1 --refresh  # re-download wiki/codex
bash scripts/facts/run_all.sh --version 0.107.1 --offline  # rebuild from cache + game data only
```
Steps: `fetch_wiki.py` → `fetch_codex.py` → `game_truth.py` (reads game data read-only, writes git-ignored
`artifacts/facts-cache/v<ver>/game_truth.json`) → `build_facts.py` (writes this directory). For a new game version,
export `knowledge/source/v<ver>/`, decompile `sts2.dll` into `artifacts/knowledge/decompiled-v<ver>/`, then run with
`--version <ver>`.
