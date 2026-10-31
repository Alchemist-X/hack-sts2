# Diff bundle exporter spec — `sts2rec export-bundles` (oracle side of the diff-bundle contract)

Owner: exporter agent. Code: `sts2rec/sts2rec/diff_bundle.py` (pure rules: projection, deck,
decisions, roll-forward, sim-registry scan), `sts2rec/sts2rec/diff_export.py` (corpus walk, files),
`sts2rec/sts2rec/cli.py` (`export-bundles`), `sts2rec/sts2rec/lineage.py` (`group_lineages`).
Tests: `sts2rec/tests/test_diff_bundle.py`, `sts2rec/tests/test_diff_export.py` (synthetic fixtures
only). Contract: [`diff-bundle.md`](diff-bundle.md) v1; consumer: [`diff-harness.md`](diff-harness.md).
Decompile citations: `<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped
(game v0.111.0 tree). v0.107.1 has no local decompile; every v0.107.1 deck is cross-checked against
the recorded combat snapshot instead (§3).

```
cd sts2rec && uv run sts2rec export-bundles [OUT_DIR] [--root SESSIONS] \
    [--sim-core sim/Sts2Sim.Core | --sim-registry registry.json] \
    [--include-ineligible] [--pre-state settled|unobserved|snapshot]
```

`OUT_DIR` defaults to `$STS2REC_DIFF_BUNDLE_DIR`, else the session scratchpad `diff-bundles`. The
corpus is opened read-only; an output directory overlapping the corpus or inside the repo is
refused (exit 2). Outputs: `bundles/<game_version>/<session>.attempt<k>.json` (eligible),
`ineligible/...` (only with `--include-ineligible`, bundles that could be built), `reasons.json`
(every attempt with its verdict), `name_map.json` (the zh-name table used).

## 1. Corpus walk

| Rule | Implementation | Evidence |
| --- | --- | --- |
| Lineage = parts with equal `(start_time, seed, character)`, ordered by `part` | `lineage.group_lineages` (same key as `discover_lineage`) | recorder `-partN` dirs |
| First combat = the first `room_entered` whose `room_model_id` starts with `ENCOUNTER.`; contract act = event `act` + 1 | `find_first_combat` | contract |
| Attempts = every later `room_entered` into the same (act, floor, encounter) until the run reaches a later floor; numbered 1.. in part order | `find_first_combat` | save&quit reloads the room-entry save |
| An entry with no combat snapshot (`battle` block) before the next room is a post-combat resume (the save was made on the rewards screen), not an attempt | `post_combat_resumes_skipped` in the lineage record | corpus: e.g. a part that opens on `rewards` |
| Attempt window = (`room_entered`.seq, next `room_entered`.seq in the same part) | `_window` | |
| Combat end = first of: `game_over` state (loss), a `Combat ended...` message / rewards / card-reward state after the room's last battle snapshot (win), `run_ended` (abandoned / loss / win); else next room (win) or part end (`abandoned`). A non-battle state followed by a later battle snapshot of the room (card selection, the map view) never ends the combat, and the map is no end marker at all | `_combat_end`, `COMBAT_END_STATE_TYPES` | corpus 2026-10-30: 61 map snapshots sit between battle snapshots of one room (the player opened the map view mid-combat); the only window with a map right after its last battle snapshot is a save&quit from that view (`1792175199-3WK10CWP8S-part7` seq 376). Treating the map as an end cut 42 of 2982 combat windows early with a false `win` (19 really end at `Combat ended`, 2 at `run_ended` abandoned, 21 at a save&quit from the map view), e.g. first combat `1792615807-4RHDW1LUA9` attempt 1: 6 decisions → 20 |
| Neow = the latest `floor_summary` whose `rooms` holds `EVENT.NEOW`, before the first combat (a Neow retried after save&quit keeps the last completed pick) | `find_first_combat` | |

Non-standard `game_mode` (custom) is accepted only when a lineage's archived
`native/run_history.run` reports `modifiers: []` (reason `game_mode_modifiers_unknown` otherwise).

## 2. Bundle fields

| Field | Source |
| --- | --- |
| `game_version`, `seed`, `character`, `ascension` | the attempt part's `manifest.json` |
| `seeding` | `v0.107.1` → `legacy_v18`; `v0.110.1`, `v0.111.0` → `xxh64`; other versions → reason `seeding_unknown_version`; seed prefixed `old` → `seed_old_prefix` |
| `encounter_id` | `room_entered.data.room_model_id` minus `ENCOUNTER.` |
| `player` | first combat snapshot of the attempt (`player.hp`, `player.max_hp`) |
| `relics` | first combat snapshot `player.relics[].id`, in order; all must be on the contract whitelist |
| `rng_offsets` | `{"Niche": 6·[KALEIDOSCOPE] + [NEW_LEAF] + [NEOWS_BONES]}` over the relics owned at combat start (contract formula; always emitted, 0 allowed). The 6 is derived (`KALEIDOSCOPE_NICHE_DRAWS`): `Cards(2)` loops (`Models.Relics/Kaleidoscope.cs:53,114`), each a `StableShuffle` on `Niche` of the unlocked character pools minus the owner's (`:56`), i.e. `count - 1` `NextInt` draws (`Extensions/ListExtensions.cs:10-35`); the rewards roll on `PlayerRng.Rewards` (`Factories/CardFactory.cs:82,191`). The pool count depends on the profile's unlocks, but `Kaleidoscope.IsAllowedAtNeow` (`:116-123`) admits it only when every character is unlocked, both as a Neow option (`Models.Events/Neow.cs:303,358`) and through `NEOWS_BONES` (`Models.Relics/NeowsBones.cs:186`), so at Neow it is `|ModelDb.AllCharacters| - 1` = 4 (`Models/ModelDb.cs:129-136`; same gate and 5 characters in v0.107.1) |
| `deck` | §3 |
| `decisions` | §5 |
| `outcome` | §1 combat end |
| `final_hp` | player hp of the last in-combat snapshot, superseded by any later in-combat event that names the player (event creature references carry hp after the event); post-victory heals happen after the window |

Every decision also carries an additive `oracle` object (the consumer recognizes it, never compares it, and copies
the divergent decision's object into its repro as `oracle_provenance`, `diff-harness.md` §4/§6): `action_seq`,
`state_seq`, `settled`, `pending_events`, `combat_card_index`, `view_state_seq` when the recorded `state_seq` was the
mid-combat map view (§5; `state_seq` is then the battle snapshot used), and for unsettled decisions `rolled_forward`
or `unobserved_reason` plus `snapshot_pre_state` (the raw `state_seq` projection).

An ineligible bundle (written only with `--include-ineligible`, under `ineligible/`) also carries a top-level
`ineligible_reasons`: the attempt's reasons as in `reasons.json`. The bundle body cannot show several of them
(`deck_mismatch`, `neow_summary_missing`, `potion_used`, `unsupported_action`, `pre_state_hand_unsettled`, ...), so
the harness skips such a bundle unless it also runs with `--include-ineligible` (`diff-harness.md` §5). Eligible
bundles never carry the key. Both keys are additive to contract v1 and not yet in `diff-bundle.md`'s text.

## 3. Deck at first-combat entry

| Rule | Source |
| --- | --- |
| Starter deck in character-model order: Ironclad 5 Strike, 4 Defend, Bash; Silent 5 Strike, 5 Defend, Neutralize, Survivor; Defect 4 Strike, 4 Defend, Zap, Dualcast; Necrobinder 4 Strike, 4 Defend, Bodyguard, Unleash; Regent 4 Strike, 4 Defend, Falling Star, Venerate | `Models.Characters/Ironclad.cs:40-52`, `Silent.cs:40-54`, `Defect.cs:39-51`, `Necrobinder.cs:48-60`, `Regent.cs:44-56` |
| A5+ appends `ASCENDERS_BANE` last | `Entities.Ascension/AscensionManager.cs:33-37` (`AddInternal(.., -1, ..)`), `AscensionLevel.cs:10` (`AscendersBane` = 5) |
| Deck additions append at the bottom | `Commands/CardPileCmd.cs:307-313` (`CardPilePosition.Bottom => -1`), `Entities.Cards/CardPile.cs:248-263` (`index < 0` → `_cards.Add`); curses via `AddCurseToDeck` → `AddCursesToDeck` (`CardPileCmd.cs:4151-4155`) |
| A deck transform deletes the original in place and appends the replacement; several transforms append in original-index order | `Commands/CardCmd.cs:1373-1382` (remove, `PileIndexSort`), `:1467` (`PileType.Deck` → `AddInternal(replacement)`), vs `:1470` (combat piles insert at the old index) |
| `NEOWS_TALISMAN`: upgrades the last Basic Strike-tagged and the last Basic Defend-tagged deck card, in place | `Models.Relics/NeowsTalisman.cs:23-24,34` |
| `LEAFY_POULTICE`: transforms the first Basic Strike and the first Basic Defend (one `Transform` call) | `Models.Relics/LeafyPoultice.cs:81-82,92` |
| `NEW_LEAF`: transforms one chosen card (`TransformToRandom`, Niche) | `Models.Relics/NewLeaf.cs:65,102` |
| `PRECISE_SCISSORS` (1) / `PRECARIOUS_SHEARS` (2): remove chosen cards | `Models.Relics/PreciseScissors.cs:60,97,138`; `PrecariousShears.cs:72,104,170` |
| `POMANDER`: upgrades one chosen card | `Models.Relics/Pomander.cs:47,70` |
| `NEOWS_BONES`: offers 2 Neow relics (resolved in claim order), then appends 1 curse picked on Niche | `Models.Relics/NeowsBones.cs:82,125-128,180-181` |
| Fixed gains: `CURSED_PEARL` 1 (GREED), `LARGE_CAPSULE` 2 (the character's Basic Strike, Defend), `HEFTY_TABLET` 2 (chosen rare + INJURY), `ARCANE_SCROLL` 1, `NEOWS_TORMENT` 1, `DOWSING_ROD` 1, `NEOWS_SACRIFICE` 1, `LEAD_PAPERWEIGHT` 1, `SCROLL_BOXES` 3 | `CursedPearl.cs:62`; `LargeCapsule.cs:112,125`; `HeftyTablet.cs:94,100`; `ArcaneScroll.cs:57`; `NeowsTorment.cs:46`; `DowsingRod.cs:46`; `NeowsSacrifice.cs:81`; `LeadPaperweight.cs:93`; `ScrollBoxes.cs:124`; counts confirmed on every corpus Neow summary |
| Variable gains (reward picks): `KALEIDOSCOPE`, `LOST_COFFER`, `MASSIVE_SCROLL`, `SILKEN_TRESS` take the remaining `cards_gained` | |

The Neow edits are replayed in `relic_choices` pick order from the floor-1 `floor_summary`
(`cards_gained`, `cards_transformed`, `cards_removed`, `upgraded_cards`). A player-chosen target is
named only by id: the choice is exact when every candidate copy gives the same deck order
(interchangeable copies), otherwise reason `deck_order_ambiguous`. Leftover history entries →
`neow_changes_unattributed`; a gain-count mismatch → `neow_gain_count_mismatch`; a relic outside
the modelled set → `neow_relic_unmodelled`; an enchanted gain → `neow_card_enchanted`.

**Cross-check (every attempt):** the reconstructed multiset must equal the first combat snapshot's
cards (hand ids + resolved draw/discard/exhaust names), else `deck_mismatch:missing=..;extra=..`.
Without a Neow summary (recording started late) the starter order is used only when that snapshot
holds exactly the starter deck; otherwise `neow_summary_missing`.

## 4. Projection (oracle side)

Built from the recorder snapshot (`mod/src/StateBuilder/PassiveStateBuilder*.cs`) exactly as listed
in `diff-harness.md` §3; exporter-specific points:

- Pile names → ids (`CardNameMap`): learned from every hand entry in the corpus (hand entries carry
  `id`, `name`, `is_upgraded`), keyed by (character, full name) first (all five characters' Strike /
  Defend share "打击" / "防御"), then globally; a name resolves only to a unique (id, upgraded) pair.
  An unseen `X+` falls back to the upgraded form of `X`. Dynamic names such as `凋萎+2` (Wither,
  not upgraded) resolve directly. Unresolved → `unresolved_pile_name` (never guessed). Corpus check
  (2026-10-30): the only name collisions are Strike / Defend across characters, and every pile name
  in the corpus resolves.
- `monster_id`: combat_id → `MONSTER.<ID>` from creature references in the attempt's events, else
  `entity_id` minus its `_<n>` suffix; disagreement → `monster_id_conflict`.
- Intents: `Attack` and `DeathBlow` (`DeathBlowIntent : SingleAttackIntent`,
  `MonsterMoves.Intents/DeathBlowIntent.cs:10`) parse `N` / `N×M` (U+00D7) into `attack_value` /
  `attack_times`; `StatusCard` carries `card_count` (its label, soft); other types only `type`.
- `turn.phase`: `player_play` when `battle.turn == "player"` and `is_play_phase`; anything else is
  reason `pre_state_not_player_play`.

## 5. Decisions

`play_card` and `end_turn` actions inside the combat window, in seq order. Skipped:
`ready_to_begin_enemy_turn`, `cancelled` actions, a repeated `end_turn` for the same `turn_number`,
`discard_potion` (noted). `use_potion` or a `potion_used` event → `potion_used`; any other in-combat
action → `unsupported_action:<kind>`.

- **Every in-combat `player_choice` → `unsupported_action:player_choice`** (plus a note with its
  `choice_type`, context label and `picked`). The contract's exclusion of choices labelled
  `SINGLETON.MULTIPLAYER_SCALING_MODEL` as automatic (`diff-bundle.md`) is wrong and no longer applied: the
  recorder's `context_model` is a static set by a `PlayerChoiceContext.PushModel` postfix and never popped
  (`mod/src/HumanFunnels.cs`, `_lastChoiceContextModel`, cleared only at run start/end), and every hook call pushes
  each listener model (`Hooks/Hook.cs:268-275`), the `MultiplayerScalingModel` singleton included
  (`Models.Singleton/MultiplayerScalingModel.cs:19`, which never requests a choice). So the label names the last hook
  listener, not the requester. Corpus 2026-10-30: inside combat windows the label sits on real hand-select / card-select
  picks (e.g. `1784801115-DBUSEVBXMZ` seq 55, the second `SURVIVOR` discard under `BURST`); in first-combat
  windows that adds the reason to 5 attempts (`1785686740-UWP6NM93JF`, `1787488664-QV7LMELKUQ`,
  `1788019242-NUFWHA7VGU` ×3; all also blocked by character coverage today, so they would have become eligible with
  a missing decision once their content was ported). Post-combat reward picks carry the label too but lie after the
  combat end.
- **Turn ends.** `ready_to_begin_enemy_turn` follows every player turn end; one with no (non-cancelled) `end_turn`
  since the previous one → `turn_end_unrecorded:seq<N>`. Recorder 0.2.0 parts driven through `STS2_MCP` log turn ends
  only this way (`1784043137-CPSVMWJPUL`: 16 `play_card`, 5 follow-ups, no `end_turn`; the only first-combat attempt
  affected), and a bundle without its `end_turn` decisions would never advance the sim's turns.
- **Map view.** An action whose `state_seq` is a mid-combat map-view snapshot (no battle block; 24 `play_card` actions
  in the corpus) takes the latest battle snapshot before it as its snapshot (`oracle.state_seq`, with
  `oracle.view_state_seq` = the map snapshot); the events in between are pending events as usual.

**`hand_index`.** `combat_card_index` is a `NetCombatCardDb` id: ids are handed out at
`CombatManager.SetUpCombat` right after `PopulateCombatState` shuffles the deck into the draw pile,
in `AllPiles` order (Hand, Draw, Discard, Exhaust, Play), then to cards that enter later
(`Combat/CombatManager.cs:6605,6612`, `GameActions.Multiplayer/NetCombatCardDb.cs:40-96,172-186`,
`Entities.Players/PlayerCombatState.cs:105-115`, `Entities.Players/Player.cs:1214-1234`). It is not a
hand position (real bundles: `X20BJH1S4R` decision 0 has hand_index 0 with `combat_card_index` 1), and snapshots
carry no instance ids, so the contract's "canonical instance resolution from `combat_card_index`" is not what the
exporter does. The exporter resolves the position from the visible hands: the candidates are pre-state hand cards
with the played id; if removing any of them leaves a hand equal in every recorded instance field they are
interchangeable (first is used); otherwise the post-action snapshot (taken once the card has left the hand) must
single out one removal: whole instance view first, then (id, upgraded, cost) exactly, ignoring cost, and as a prefix,
where a match on that shorter key counts only if the copies it cannot separate are interchangeable. Else
`hand_index_ambiguous`. The instance view (`_instance_view`) is every hand-entry field except `index` and
`description`: name, keywords (hover tips: enchantments, afflictions, added keywords such as Soulbound), star cost,
playability, cost, upgrade. `description` is left out because it carries the UI's damage preview of the targeted card
(`1786880759-X20BJH1S4R` seq 110: two plain Strikes read 6 and 9 damage against a Vulnerable enemy). Corpus scan
(2026-10-30, all 58 447 same-(id, upgraded, cost) groups in battle hands): 1 119 groups differ in another instance
field (enchanted `本能` Strikes, Soulbound Defends, `凋萎+2`, ...), which the earlier (id, upgraded, cost) test could
treat as interchangeable; no hand_index of the 142 exported first-combat attempts changed. Per-copy state visible only in
the description (a per-instance damage counter) stays invisible: check such cards when they are ported.
Corpus cross-check on turn-1 plays, where the instance ids equal opening draw order: 327 exact, 63
different-but-interchangeable copies, 1 difference explained by an Innate card moved to the top.

**Mid-resolution snapshots.** The recorder's post-action snapshot is taken as soon as a played card
leaves the hand (energy spent, card in the unprojected play pile) and before its effects resolve;
when no other snapshot follows before the next action it is that action's `state_seq` (25 % of all
corpus decisions). Its enemy hp / powers / discard pile are stale. Per decision the exporter
collects the state events between the snapshot and the action (`pending_events`):

| Case | `pre_state` | Eligibility |
| --- | --- | --- |
| no pending events (and the previous card is not still in the play pile, below) | snapshot projection | — |
| pending events all replayable (`--pre-state settled`, default) | snapshot rolled forward (`diff_bundle.roll_forward`): creature hp / max_hp / block from every event creature reference (values after the event); `power_received` amounts added (powers never seen in any snapshot are invisible, skipped); the previous card moved to its `card_play_finished` `result_pile`; dead enemies dropped | — |
| a pending event that cannot be replayed (draw, discard, summon, orb, generated card, a power that can change an attacking enemy's damage preview, ...) or the previous card still resolving when the hook fired | `null` = unobserved (contract extension, `diff-harness.md` §5): replayed, not compared | ineligible only for a `play_card` whose hand may have changed (`pre_state_hand_unsettled`) |

`pre_state_hand_unsettled` therefore depends on the mode (corpus 2026-10-30, after the review fixes: 477 unsettled
decisions of 1 918): `settled` 9 attempts (402 rolled forward, 75 unobserved), `unobserved` 10 (all 477 unobserved),
`snapshot` 0 (raw snapshots kept, nothing flagged). The eligible set is the same 4 attempts in all three modes.

Where the previous card sits is decided by card counts, never by event order: the recorder can write
`card_play_finished` / `card_exhausted` / `card_drawn` after a snapshot that already shows the move
(observed: a draw visible one seq before its `card_drawn`). Hand + draw + discard + exhaust is one
less than at the previous (trusted) decision while the card is in the play pile, equal once it has
moved; anything else, or a `card_generated` in between, leaves the move unresolved (unobserved).
An intent's `attack_value` is a damage preview, so a power change on the player (other than
Strength / Dexterity) or on an attacking enemy (other than Vulnerable) is not replayed.

Validation against the corpus (2026-10-30): for 66 rolled-forward `play_card` pre_states whose next
snapshot shows only that card's start (energy spent, card out of hand), every other projected field
equals the next snapshot; the remaining 2 differences are that next snapshot already showing the new
card's own draw before its event (recorder lag), not a roll-forward error.

Harness smoke run (2026-10-30, the in-progress `Sts2Sim.Diff` on the 4 eligible corpus bundles,
sim registry with all 8 act-1 weak encounters): 3 bundles agree on every compared decision
(including all 20 rolled-forward pre_states) and on outcome / final hp; `1788970367-QC7ZPPWXEK`
(`SLIMES_WEAK`, `legacy_v18`) diverged at decision 0 on the spawned slimes (`LEAF_SLIME_S` vs
`TWIG_SLIME_S`): the harness built the combat without the run context (`diff-harness.md` §2), not an exporter field.
After the integration fix: 4/4 bundles agree, 69/69 decisions (24 rolled forward, 45 settled), outcome and
`final_hp` 4/4; the `--pre-state unobserved` export agrees on its 45 compared decisions and 4/4 outcomes.

## 6. Eligibility and the reasons index

An attempt is eligible iff it has no reason. Reason classes (`reasons.json` →
`summary.reason_classes`, attempts per class; `sole_reason_classes`; `blocked_only_by_sim_coverage` =
attempts failing only on sim content):

| Class | Meaning |
| --- | --- |
| `character_not_ported`, `encounter_not_ported`, `card_not_ported`, `card_upgrade_not_ported` | sim coverage, from the sim registry (scanned from `sim/Sts2Sim.Core`: concrete `CardDef` / `EncounterDef` subclasses minus `abstract` / `[SimExclude]`, id from the `base(...)` literal or const; a card is upgradable when its class chain overrides `OnUpgrade`; characters from `const string *CharacterId`), or `--sim-registry` JSON |
| `relic_not_whitelisted`, `relic_excluded` | contract relic whitelist |
| `seed_old_prefix`, `seeding_unknown_version`, `lineage_version_mixed` | seeding |
| `game_mode_modifiers_unknown`, `game_mode_modifiers` | custom runs |
| `potion_used`, `unsupported_action` | combat content the bundle cannot express |
| `neow_summary_missing`, `deck_mismatch`, `deck_order_ambiguous`, `neow_*`, `character_unknown` | deck reconstruction |
| `unresolved_pile_name`, `intent_label_unparsed`, `power_unparsed`, `hand_card_unparsed`, `monster_id_*`, `pile_count_*`, `player_field_unparsed` | projection |
| `pre_state_missing`, `pre_state_shared`, `pre_state_not_player_play`, `pre_state_hand_unsettled`, `played_card_not_in_pre_hand`, `hand_index_ambiguous`, `target_not_in_pre_state`, `turn_end_unrecorded`, `no_decisions` | decisions |
| `first_combat_not_floor_2`, `attempt_not_from_combat_start`, `stream_unreadable` | corpus integrity |

## 7. Open points

- v0.107.1 deck order relies on the v0.111.0 starter decks and Neow rules (no v0.107.1 decompile);
  the multiset cross-check passes for every eligible v0.107.1 attempt. The order is now checked through the opening
  hand (2026-10-30, `diff-harness.md` §7): for 136 of the 140 exported attempts with an opening `pre_state` (all
  characters, both seedings, 124 v0.107.1) the recorded hand is the first draws of `UnstableShuffle(bundle deck order)`
  on the bundle's `Shuffle` stream; the other four are three Innate cards moved to the top (the rest of the hand
  matching) and `1784043137-CPSVMWJPUL` (Neow not recorded, already ineligible).
- `rng_offsets` emits only `Niche` (contract formula); `PHIAL_HOLSTER` draws on
  `CombatPotionGeneration` at pickup, a stream the first-combat sim does not use.
- The roll-forward cannot recompute damage previews; unobserved decisions still replay.
- Recorder: `player_choice.context_model` should be the top of the `PlayerChoiceContext` model stack (patch
  `PopModel` too, `GameActions.Multiplayer/PlayerChoiceContext.cs:12-44`); until then the exporter treats the label as
  unreliable (§5). Choices are not expressible in bundle v1 either way.
- `sts2rec/sts2rec/human_dataset.py` (outside the exporter) still uses `combat_card_index` as a hand-position
  discriminator between copies (`:300-310`), which §5 shows it is not.
- Contract text (`diff-bundle.md`, shared) still names the MULTIPLAYER_SCALING choice exclusion, the
  `combat_card_index` hand resolution and the six-argument `Create`, and omits `pre_state: null`, rolled-forward
  pre_states, `decisions[].oracle`, `ineligible_reasons`; replacement text: `review-fixes-2026-10-30.md` §3.

Review fixes 2026-10-30 (`review-fixes-2026-10-30.md`): corpus export after them: 142 attempts, 4 eligible (the same
four, byte-identical bundles), 1 918 decisions; reason classes changed only by `unsupported_action` 18 → 23 and the new
`turn_end_unrecorded` 1; `1792615807-4RHDW1LUA9` attempt 1 now ends at `Combat ended` (20 decisions, one through the
map view) instead of at its map view.
