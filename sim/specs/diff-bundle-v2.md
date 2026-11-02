# Diff bundle v2 exporter — `sts2rec export-bundles-v2`

2026-11-01, W4 exporter-v2. Contract: `docs/sim-port/design-diff-gate.md` §4.2 (screen projections), §5 (schema v2,
action union, eligibility v2, X1–X13). Code: `sts2rec/sts2rec/diff_export_v2.py` (new), `sts2rec/sts2rec/cli.py`
(subcommand only). Tests: `sts2rec/tests/test_diff_export_v2.py` (synthetic corpora). v1 (`export-bundles`,
`diff_export.py`, `diff_bundle.py`) is not modified; v2 imports its pure helpers, so v1 output stays byte-identical.

```
cd sts2rec && uv run sts2rec export-bundles-v2 OUT_DIR --sim-core ../sim/Sts2Sim.Core \
    [--root SESSIONS] [--sim-registry FILE] [--versions v0.111.0,v0.110.1|all] [--pre-state settled|unobserved|snapshot] [--mcr-readable]
```

Outputs: `OUT_DIR/<start_time>-<seed>-<character>.json` (one run each), `reasons.json` (index: `summary`, per-run and
per-segment verdicts, the registry used), `name_map.json`. OUT_DIR may not overlap the corpus, nor sit in the repo
outside its git-ignored `tmp/`.

## Rule index (one line each)

| id | rule | where |
| --- | --- | --- |
| X1.1 | lineage = v1 `group_lineages` (start_time, seed, character); id `<int start_time>-<seed>-<character>` | `lineage_id` |
| X1.2 | room window = `room_entered` → next `room_entered` of the same part (or part end); part order | `room_windows` |
| X1.3 | window key (act 1-based, floor, room_type, room_model_id); attempt k = k-th window with that key holding a battle snapshot (combat) or k-th window (other rooms) | `build_lineage_bundle` |
| X1.4 | combat window without a battle snapshot = post-combat resume (save on the rewards screen): `room` segment, phase `post_combat_resume`, attempt = last combat attempt | same |
| X1.5 | combat end = v1 `_combat_end`; when none, the first run-level action after the last battle snapshot bounds it (`run_level_action`) | `build_combat_segment` |
| X1.6 | combat window → `combat` segment + `room` segment (phase `post_combat`, only if run-level actions follow) + `map` segment; other rooms → `room` + `map`; map segment = the window's `vote_for_map_coord` / `vote_to_move_to_next_act` | `build_lineage_bundle` |
| X1.7 | QuickSL / multi-part chains: windows are cut per part; a retry in a later part is the next attempt of the same key; actions before a part's first room entry are reported in `notes` (0 in the v0.110.1/v0.111.0 corpus) | `room_windows` |
| X3.1 | combat `start`: `encounter_id`, `player {hp, max_hp}`, `deck`, `deck_multiset`, `relics`, `potions [{slot,id}]`, `max_potion_slots`, `gold` from the first battle snapshot; `rng_offsets` (v1 Niche recipe + v1 relic whitelist) only for the run's first combat at act 1 floor 2 when `game_version == rules_version` (v0.111.0); otherwise `anchor` | `build_combat_segment` |
| X3.2 | first-combat deck = v1 `reconstruct_deck` (starter + A5 bane + Neow edits); no Neow summary → starter deck only if the snapshot multiset equals it (v1 rule) | same |
| X3.3 | later decks = `DeckTracker` over the floor summaries: gains append (`current_upgrade_level` > 0 = upgraded), removals/transforms delete in place (transform final appended), `upgraded_cards` in place; a copy chosen by id only whose candidates give different orders drops the order (multiset kept); gains + transforms on one floor drop the order | `DeckTracker` |
| X3.4 | a repeated summary (same rooms and deck-delta fields as the previous one; the act-boss summary is logged again after the act transition) is applied once | `SUMMARY_DECK_KEYS` |
| X3.5 | cross-check vs the snapshot multiset: same ids, other upgrade flags → `deck_upgrade_mismatch` (deck kept: in-combat upgrades vary per combat); otherwise `deck_reconstruction_failed:<delta>`, deck null, tracker reset to the observed multiset unless a card was generated before the snapshot; known multiset without order → `recorder_gap:deck_order` | same |
| X3.6 | `native/replay.mcr` = the last combat won inside its part (`Combat/CombatManager.cs:1763`): anchor `mcr_initial` (ref, sha256, bytes) attached to that combat segment; later combats: `anchor_unreadable:mcr_decoder_missing` (until `--mcr-readable`) or `anchor_missing` | `attach_mcr_anchors` |
| X3.7 | R1 saves (`native/saves/<seq>.save` + `run_saved` events) are indexed as `room_entry` / `combat_exit` anchors when present (none in the corpus yet) | `index_anchors` |
| X4.1 | `play_card` / `end_turn` / turn-end follow-ups: v1 `extract_decisions` unchanged (hand index, settled roll-forward, unobserved) on the combat's v1 action kinds | `build_combat_segment` |
| X4.2 | combat projection v2 = v1 projection + `player.potions`, `player.relics [{id,counter}]`, `orbs [{id,passive,evoke}]` + `orb_slots`, `stars`, `pets [{id,alive,hp,max_hp,block}]` when the snapshot has them | `combat_projection_extras` |
| X4.3 | `use_potion` {slot, potion_id, target_combat_id, in_combat}; `discard_potion` (potion id from the snapshot slot); pre_state null when events lie between snapshot and action | same |
| X4.4 | in-combat `choose` {context combat, kind hand/grid/choose_one/bundle/relic/index/cards, choice_type, picks [{index, card_id, upgraded, combat_card_index: null}], skipped}; candidates from the hand/card select snapshot → `pre_state.select` and `oracle.candidates` | `build_choose` |
| X4.5 | hand select: picked cards leave `hand_select.cards`; walk back to the earliest contiguous hand-select snapshot; if picks are already selected, candidates = the player hand filtered to the selectable (id, upgraded) in hand order (exact when every hand copy is selectable), else `recorder_gap:choice_identity` | `prompt_open_state` |
| X4.6 | picks named by id only: first unused matching candidate; copies differing in any recorded instance field → `recorder_gap:choice_identity`; no candidates → resolved by the tracked deck when it holds one (id, upgraded) variant (`resolved_by: deck_multiset`, index null), else gap | `resolve_card_picks` |
| X4.7 | `undo_end_turn` → `{type: undo_end_turn}`; with a v1-skipped duplicate end_turn after it → `unsupported_action:end_turn_after_undo` (none in v0.110.1/v0.111.0) | same |
| X5.1 | run-level pre_state = `{screen, player, <screen block>}`; player = hp, max_hp, gold, relics (id, counter), potions (slot, id), max_potion_slots | `project_run_screen` |
| X5.2 | map: `current`, `next_options [{col,row,type}]`; `nodes` (+ `bosses` when recorded) on the first map decision of each act; `map_move {col,row}`, `next_act` | `project_map` |
| X5.3 | rewards: items (index, type, gold_amount, potion_id, relic_id, cards when R4 present); `reward_take {set_index, reward_type, item_id}`, `rewards_skip` | `project_rewards` |
| X5.4 | card reward: `Index` choice followed by `reward_taken` card/special_card → `reward_take` then `card_reward_pick {index, card_id}` (`oracle.recorded_order: after_pick`); card id from the card_reward snapshot, else the floor summary's picked offer; other `Index` on rewards/card_reward → `card_reward_skip {index}`; offers not live → `recorder_gap:card_reward_offer`; `oracle.offered` = the summary's offer ids | `build_run_decisions` |
| X5.5 | rest: `rest_option {option_id}` then its deck `choose` (recorded before it: `recorded_order: before_opener`); shop: `shop_remove {price}` then its `choose`; the opener's pre_state is the state before its choice (`oracle.pre_state_seq`; its own hook fires after payment, MerchantCardRemovalEntry.OnTryPurchaseWrapper; W4.1); `shop_buy {category, item_id, price, slot}` matched on category + price + sale flag in the shop snapshot (`shop_purchase_unresolved` / `_ambiguous`) | same |
| X5.6 | event: `event_option {text_key (= title_key minus .title), index}` (index from the option text_key or a unique title match, looking back to the last event snapshot with options; none → `recorder_gap:event_option_key`); `event_proceed {text_key, index}` | `_event_option_index` |
| X5.7 | treasure: `treasure_open`, `treasure_pick {index, relic_id, skipped}`; out-of-combat `use_potion` / `discard_potion` with `in_combat: false`; other choices → `choose` with context by room (reward, shop, rest, event, deck_op) | same |
| X5.8 | `floor_summary` = the window's last summary {seq, map_point_type, rooms, player_stats[0]} on the room segment (else the combat segment) | `_floor_summary` |
| X8.1 | segment eligible iff no reason; run reasons (`seed_old_prefix`, `seeding_unknown_version`, `lineage_version_mixed`, `game_mode_modifiers*`, `stream_unreadable`, `provenance_mixed`) are copied into every segment | `build_lineage_bundle` |
| X8.2 | sim content: v1 classes (`character_not_ported`, `encounter_not_ported`, `card_not_ported`, `card_upgrade_not_ported`) + `content_not_ported:{relic,potion,power,monster,event}:<id>`; registry scanned from concrete `RelicModel`/`PotionModel`/`PowerModel`/`EventModel`/`AncientEventModel`/`MonsterDef` subclasses (`Id =>` const or `base(...)` literal), a class body with `NotImplementedException` = not ported | `scan_content_registry` |
| X8.3 | run layer: `run_layer_not_ported:<area>` by the presence of `Run/Map/StandardActMap.cs` (map), `Run/Events/Ancients/Neow.cs` (neow), `Run/Events/EventModel.cs` (events), `Run/Rewards/RewardsSet.cs` (rewards), `Run/Rooms/MerchantRoom.cs` (shop), `RestSiteRoom.cs` (rest), `TreasureRoom.cs` (treasure); room and map segments carry `anchor_missing` until R1 anchors exist | `RUN_LAYER_SOURCES` |
| X8.4 | histogram class = first field, plus the area for `content_not_ported`, `run_layer_not_ported`, `recorder_gap`, `anchor_unreadable` | `reason_class_v2` |
| X9.1 | `source.provenance {driver human/mcp/mixed (STS2_MCP in a part manifest's mods), mods, recorder_version, untested}` | same |
| X9.2 | per-decision `oracle {action_seq, state_seq, part, source_hook, status, human?, settled?, pending_events?, ...v1 fields}` + `run_index` (lineage-wide decision counter) | `_oracle` |

## Fields beyond the §5 schema (additive; consumers may ignore)

Segment: `phase` (`post_combat`, `post_combat_resume`, null), `part`, `window {start_seq, end_seq}`, `counts`
(combat: unsettled / rolled_forward / unobserved), `notes`. Combat `start`: `encounter_id`, `deck_multiset`,
`max_potion_slots`, `gold`. Room `start`: `player`, `deck_multiset`, `deck`. Outcome: `evidence`, `end_seq` (combat).
Anchor: `bytes`. Bundle: `notes`. Decision `oracle`: `recorded_order`, `offered`, `candidates`, `select_state`,
`options_state_seq`, `unobserved_reason`, `status`. Action: `choose.choice_type`, pick `resolved_by`;
`pre_state.select` (select screens; alone when the select snapshot has no battle block).

## Corpus run 2026-11-01 (v0.111.0 + v0.110.1, `tmp/diff-v2`, 44 MB)

19 runs (11 v0.111.0, 8 v0.110.1); 1,664 segments: 427 combat (275 attempt 1, 152 retries), 641 room, 596 map;
11,724 decisions (combat 9,319, rewards 729, map 596, card_reward 358, event 283, select 151, rest_site 129,
treasure 85, shop 74). Eligible today: 2 combat segments / 31 decisions (the two G0G2JMFEYHRS first-combat attempts,
identical to v1). Combats blocked only by an anchor: 93 `anchor_missing`, 27 `anchor_unreadable:mcr_decoder_missing`.
Reason classes (segments): anchor_missing 1,542, recorder_gap:card_reward_offer 292, recorder_gap:event_option_key 150,
content_not_ported:relic 148, recorder_gap:deck_order 123, anchor_unreadable:mcr_decoder_missing 111,
pre_state_hand_unsettled 103, card_upgrade_not_ported 66, deck_upgrade_mismatch 45, content_not_ported:event 35,
recorder_gap:choice_identity 32, no_decisions 25, seed_old_prefix 23, content_not_ported:monster 22,
content_not_ported:power 13, encounter_not_ported 13, card_not_ported 10, relic_not_whitelisted 9, others ≤ 6.
v1 cross-check: all 11 v0.111.0 first-combat attempts have the same play_card/end_turn decisions, pre_states (minus
the v2 player additions), deck, offsets and outcome as `export-bundles`; v0.110.1 first combats differ only by design
(no recipe offsets off the rules version → `anchor_missing`).

## Open points

- X2 (anchor index with `anchor_mismatch`), X6/X7 (legacy), X10–X13 not done; R1/R2 recorder anchors do not exist yet,
  so no room/map segment can be eligible.
- Card-reward skip detection is by position (an `Index` on the rewards screen not followed by a card `reward_taken`);
  the Neow `NEOWS_BONES` `Index` sequences are classified the same way, unverified.
- `unsupported_action:event_option` (3): event options inside an encounter window (event → combat in one room).
- `deck_upgrade_mismatch` (45) is attributed to in-combat upgrades before the first snapshot; not verified per relic.
- Act-boss summary dedupe compares with the previous summary only; two consecutive identical rest-site smiths of one
  card id would be merged (not seen in the corpus).
