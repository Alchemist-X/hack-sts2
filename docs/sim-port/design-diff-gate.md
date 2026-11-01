# Differential gate at full scale — design

2026-11-01 · branch `claude/sim-source-recreation-9392df` · status: design, nothing implemented.
Inputs: `sim/specs/diff-bundle.md`, `diff-bundle-exporter.md`, `diff-harness.md`, `review-fixes-2026-10-30.md`,
the status log in `docs/p0-sim-port-plan-2026-10-28.md`, the recorder (`mod/src/**`), the exporter
(`sts2rec/sts2rec/**`), the harness (`sim/Sts2Sim.Diff/**`), the read-only corpus and both decompile trees.
Data: `sim/specs/inventory/corpus-inventory.json` (every number below comes from it; the scan scripts are
listed in its `method.scripts`). Citations: decompile `<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.`
prefix dropped (v0.111.0 tree); `v107:` marks the v0.107.1 tree; recorder and tool citations are repo paths.

## 结论

1. **语料**：134 局 / 1,508 段 / 1,887 场唯一战斗（3,038 次尝试）/ 61,433 个战斗内决策 + 16,100 个局层决策。
   v0.107.1 占段数 87%、战斗内决策 86%，内容覆盖是 v0.111.0 的 2–3 倍；v0.111.0 只有 10 局、173 场战斗。
   v1 闸只看每局首战（131 场）——**93% 的战斗卡在"非首战没有 RNG 对齐"而不是内容**。recorder 从不拷贝
   `current_run.save`；现有的 `native/replay.mcr` 覆盖 708 场战斗（含初始 SerializableRun），但是二进制。
2. **版本**：两树机械 diff ⇒ 1,571 个共有内容文件中约 230 个规则分叉（≈85% 精度），另有 39 个单版本独有类、
   约 30–40 个引擎点；完整 GameVersion 分支 ≈ 260–300 个分支点。保守估计 v0.107.1 语料在 v0.111 规则下只有
   ≤15% 决策可直接比对。推荐 **(iii) 非对称**：sim 只实现 v0.111.0 规则；版本只经 RNG 播种与注册表过滤两个缝进入；
   旧语料经"版本污染过滤"做回归资产；按需（有上限）加分支；主干 oracle 增长靠 v0.111.0 新语料（人类 + MCP 自走农场）。
3. **对齐**：顺序 = recorder 被动拷贝存档（R1，S）→ sim 存档导入 + 锚点开战（M，全规模战斗闸）→ `.mcr` 解码
   （L，解锁现有 708 场）→ 锚点式 RunSim 局层差分 → 无锚点整局种子重放（终验）→ 旧语料计数器搜索（L，最后、可选）。
4. **局层差分**：三个比对点——锚点（存档 JSON ↔ sim `ToSave()`，含全部 RNG 计数器）、每个局层决策的屏幕投影、
   每层的 `floor_summary`（游戏自己的 `MapPointHistoryEntry`）。recorder 缺：存档、主牌组、奖励卡/遗物 offer、事件选项
   key、选择的实例身份、结算后快照时机、RNG 计数器；全部可被动补（R0–R11）。
5. **bundle v2**：每局一个容器、按房间分段，战斗段保持 v1 形状（v1 字节级不变、harness 把 v1 读成单段 v2）。
6. **门**：G0（现有）→ G1 锚点战斗闸 → G2 锚点局层闸 → G3 种子整局重放；按内容区覆盖计数 + 100%/99.9% 一致率 +
   分歧 11 类分诊 + 每批 ratchet；无游戏自检（克隆/哈希、存档往返、合法动作闭包、不变量、夜间随机整局 soak）。
   spire-mentor 只作次级 oracle（线索，不作验收）。

## 1. Corpus inventory

Scan of `~/Library/Application Support/Sts2Recorder/sessions/` on 2026-11-01 (read only; the newest part was still
being written). Definitions are in `corpus-inventory.json` → `method`.

### 1.1 By game version

| version | seeding | lineages | parts | win / loss / abandoned / incomplete | unique combats (Monster / Elite / Boss) | attempts | in-combat decisions | run-level decisions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| v0.107.1 | `legacy_v18` | 116 | 1,313 | 30 / 45 / 36 / 5 | 1,626 (1,150 / 288 / 188) | 2,631 | 52,621 | 13,809 |
| v0.110.1 | `xxh64` | 8 | 79 | 1 / 4 / 2 / 1 | 88 (70 / 9 / 9) | 151 | 3,016 | 713 |
| v0.111.0 | `xxh64` | 10 | 116 | 7 / 1 / 1 / 1 | 173 (107 / 42 / 24) | 256 | 5,796 | 1,578 |
| all | | 134 | 1,508 | 38 / 50 / 39 / 7 | 1,887 (1,327 / 339 / 221) | 3,038 | 61,433 | 16,100 |

Characters × ascension (lineages): v0.107.1 Defect 43 (A1–A10, A10: 22), Necrobinder 35 (A1–A10), Regent 22
(A5–A10, A10: 8), Silent 9 (A1–A4), Ironclad 7 (A0: 1, A10: 6); v0.110.1 Necrobinder 8 (A9: 7, A10: 1);
v0.111.0 Silent 8 (A4–A8), Ironclad 2 (A10). Per-cell table: `by_version_character_ascension`; per-lineage rows:
`lineages`.

Depth: max floor reached — 1: 3, 3–5: 1, 6–10: 15, 11–17: 30, 18: 2, 19–25: 11, 26–34: 19, 36–51: 53 lineages;
last act reached — act 1: 49, act 2: 32, act 3: 53. Unique combats per act — act 1: 995 (717 / 176 / 102),
act 2: 546 (378 / 102 / 66), act 3: 346 (232 / 61 / 53). First combats: 131; later combats: 1,756 (93 %).
Attempts per unique combat (save & quit or QuickSL reloads; an official reload restarts from the room-entry save): 1: 1,320,
2: 331, 3: 117, 4: 45, ≥5: 74.

### 1.2 Decisions by screen

| screen | recorder action kinds | all versions | v0.111.0 |
| --- | --- | --- | --- |
| play_card | `play_card` | 46,330 | 4,374 |
| end_turn | `end_turn`, `undo_end_turn` | 9,672 | 785 |
| choice, in combat | `player_choice` (CombatCard 3,117, Index 719) | 3,836 | 511 |
| potion, in combat | `use_potion`, `discard_potion` | 1,595 | 126 |
| map | `vote_for_map_coord`, `vote_to_move_to_next_act` | 4,052 | 386 |
| reward | `reward_taken`, `rewards_skipped`, `pick_relic`, `open_chest` | 5,312 | 523 |
| choice, out of combat | `player_choice` (Index 2,199 = mostly card-reward picks, DeckCard 948) | 3,147 | 329 |
| event | `event_option`, `event_proceed` | 2,136 | 187 |
| rest | `rest_site_option` | 714 | 87 |
| shop | `shop_purchase`, `shop_card_removal` | 618 | 49 |
| potion, out of combat | `use_potion`, `discard_potion` | 121 | 17 |

Follow-ups, not decisions: `move_to_map_coord` 3,844, `ready_to_begin_enemy_turn` 9,570. No action record has
status `cancelled`. Per-act split: `decisions_by_screen_and_act`.

### 1.3 Native artifacts and recorder state

- `native/` exists in 769 parts. **No part holds a copy of `current_run.save`**: the recorder archives only
  `run_history.run` (on `RunHistorySaveManager.SaveHistory`) and `replays/latest.mcr` (on
  `CombatReplayWriter.WriteReplay`), both through `TrajectorySession.ArchiveNative` with `overwrite: true`
  (`mod/src/RunLifecycle.cs:353-401`, `mod/src/Core/TrajectorySession.cs:227-238`).
- `replay.mcr`: 715 parts (header version v0.107.1 611, v0.110.1 41, v0.111.0 63), mean 14.5 KB, max 146 KB. Each is
  the last combat won inside its part (the game writes it on the win path, `Combat/CombatManager.cs:1763`; the
  recorder session is already closed when `RunManager.CleanUp` writes it, `Runs/RunManager.cs:4653-4656`), so they
  cover **708 unique combats (37.5 %)**, 604 of them v0.107.1. Content (`Multiplayer.Replay/CombatReplay.cs:33-76`):
  version, commit, `modelIdHash`, choice/reward/action/hook ids, `serializableRun` = `RunManager.ToSave(null)` taken by
  `RecordInitialState` right after the map-point save and before the room roll (`Runs/RunManager.cs:615-618` save,
  `:669-672` `RecordInitialState`, `:681-683` `RollRoomTypeFor` + `CreateRoom`, `:703` `EnterRoom`), every GameAction as a NetAction, hook
  actions, player-choice results, and per-checksum `NetFullCombatState` (creatures, piles in order, potions, relics,
  orbs, run and player RNG sets; `Entities.Multiplayer/NetFullCombatState.cs:133-161,412-420`). It is a bit-packed
  `PacketWriter` stream whose model ids are `ModelIdSerializationCache` net ids (ContentSorter order over
  `ModelDb.All`, verified by `modelIdHash`; `Multiplayer.Serialization/ModelIdSerializationCache.cs:87-140`).
- `run_history.run`: 127 lineages (schema 9 for v0.107.1 ×111, schema 10 ×16; 38 wins, 50 losses, 39 abandoned):
  final deck/relics/potions and the full `map_point_history` (card/relic/potion/event/ancient choices with offers,
  rest choices, deck deltas, gold and HP flows). `sts2rec/sts2rec/native_run.py` accepts schema 8–9 only; the
  exporter reads the file raw.
- Recorder 0.2.0 in every part. `RecorderMod.KnownGoodVersions = ["v0.107.1"]` (`mod/src/RecorderMod.cs:31`), so all
  v0.110.1 / v0.111.0 parts carry `untested: true`: the hook-map drift check (CLAUDE.md, `docs/hook-map.md`) has not
  been redone for v0.111.0. Game commits: v0.107.1 `59260271`, v0.110.1 `db5d3552`, v0.111.0 `41cef1ea`. Extra mods:
  QuickSL in 24 lineages (421 parts), STS2_MCP in 1 lineage (2 parts). No degraded hooks. `checksum` events exist in
  13 v0.107.1 parts only (780 events).

### 1.4 Content reachable from the corpus

| area | v0.111.0 game total | seen, all versions | seen, v0.111.0 |
| --- | --- | --- | --- |
| cards | 595 | 532 | 166 |
| powers | 265 | 223 | 103 |
| relics | 298 | 239 | 98 |
| potions | 64 | 60 | 38 |
| monsters | 119 | 107 | 87 |
| encounters | 89 | 85 | 65 |
| events | 66 | 66 | 42 |

Game totals: concrete, non-mock, non-deprecated models (`sim/specs/inventory/summary.json`).

"Seen" = id appears in a hand, a played card, a select/reward list, a relic or potion list, an enemy entity id, a
power status or `power_received`, or a `room_entered` model id. These are upper bounds of what replay can touch.

Consequences: (1) the current gate is alignment-limited, not content-limited — first combats are 7 % of combats;
(2) the v0.111.0 corpus alone is too small to certify a full port (173 combats, ~7.4 k decisions); (3) run-level
decisions are 21 % of all decisions and are not gated at all today.

## 2. Version strategy

### 2.1 Mechanical diff of the two trees

The trees cannot be diffed as text: the v0.111.0 tree renders every async/iterator method as a compiler state
machine (`MoveNext`), the v0.107.1 tree restores `async` bodies (Bash.cs: 151 vs 43 lines for the same rules).
Method (`tmp/sim-port/diff-gate/tree_diff.py`, `classify_diff.py`): pair files by (namespace, file name); strip
comments, attributes, `using` lines and state-machine plumbing; per file compare (a) the multiset of numeric literals
outside presentation contexts, 0/±1 dropped, plus `Name => constant` properties, (b) the multiset of rule enums
(`CardType`, `CardRarity`, `TargetType`, `CardKeyword`, `PowerStackType`, `AscensionLevel`, `ValueProp`, `PileType`, …),
(c) the set of called members minus presentation/API-noise names. A file is *rule-diverging* when any of the three
differs. Manual review of the flagged card files: ~85–90 % are real changes; residual noise is lambda rendering of
0-valued constants and renamed APIs.

Files: v0.111.0 3,537, v0.107.1 3,424, common 3,405, only v0.111.0 132, only v0.107.1 19.

| area | common | only v0.111.0 | only v0.107.1 | value changed | enum changed | hook/command calls changed | rule-diverging (estimate) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| cards | 577 | 19 | 1 (Scare) | 55 | 24 | 31 | 86 |
| powers | 258 | 10 | 2 (DiamondDiadem, Outbreak) | 6 | 1 | 23 | 25 |
| monsters | 121 | 0 | 0 | 20 | 4 | 8 | 24 |
| relics | 298 | 2 (DowsingRod, NeowsSacrifice) | 0 | 12 | 8 | 16 | 23 |
| potions | 64 | 1 (Ambergris) | 0 | 0 | 23 | 0 | 23 (all `TargetType.Self` → `AnyPlayer`) |
| events | 68 | 0 | 0 | 8 | 2 | 13 | 19 |
| card pools | 13 | 1 | 0 | 9 | 1 | 9 | 9 (pool membership) |
| characters | 8 | 0 | 0 | 1 | 0 | 6 | 6 (animator noise) |
| encounters | 88 | 3 | 0 | 5 | 3 | 2 | 5 |
| orbs | 5 | 0 | 0 | 0 | 4 | 5 | 5 (trigger API: `Trigger` → `TriggerPassive`/`ActivatePassive`/`ActivateEvoke`) |
| modifiers, enchantments, relic/potion pools | 59 | 0 | 0 | — | — | — | 5 |
| acts, afflictions | 12 | 0 | 0 | 0 | 0 | 0 | 0 |
| **total** | **1,571** | **36** | **3** | 119 | 72 | 118 | **230** (112 constant/enum-only, 118 with call changes) |

Examples (constants only): Devastate damage 30 → 35; Accelerant rarity Rare → Uncommon; Hyperbeam 28/+8 → 24/+6;
TimesUp loses Exhaust; Regalite block 2 → 4; SealOfGold 5 → 3; SignetRing 999 → 888; Entomancer HP 155 → 165;
the Cook rest option's max-HP constant 9 → 5 with a new card selection. Hook-timing changes: BoneTea
`AfterSideTurnStart` → `AfterPlayerTurnStart`, RegenPower `AfterSideTurnEnd` → `BeforeSideTurnEndEarly`, Fiddle
`ModifyHandDrawLate` → `ModifyHandDraw`, JugglingPower `AfterCardPlayed` → `BeforeCardPlayed`, HandDrill
`AfterDamageGiven` → `AfterBlockBroken`. Full per-file lists: `corpus-inventory.json` →
`version_diff_v0_107_1_vs_v0_111_0.content_files`.

Engine level (rules namespaces, 86 noisy flags; `engine_summary`, `engine_files`): Hook dispatchers 146 → 148
(`AfterTurnEnd`/`BeforeTurnEnd` only in v107, `AfterSideTurnEnd`/`BeforeSideTurnEnd`/`BeforeCombatRewardOffered`/
`ModifyCardPlayResultLocation` only in v0.111.0); `AbstractModel` virtuals 181 → 183; new combat types
(`Combat/CombatTurnState.cs`, `PendingLossState.cs`, `EndTurnSignal.cs`, `CombatId.cs`); `CardPileCmd`, `CardCmd`,
`CardSelectCmd`, `CreatureCmd` flagged; seed hashing XXH64 (`SerializableRunV18ToV19`) and seed length 10 → 12
(`Helpers/SeedHelper.cs:13`); save schema: v107 migrations end at V15→V16, v0.111.0 is schema 20.

Decompile gaps: 13 v0.111.0 files hold `RuntimeHelpers.InitializeArray(… LdMemberToken …)`, i.e. unreadable static
array initializers; rules-relevant: `Entities.Merchant/MerchantInventory.cs` (shop card-type layout),
`Map/GoldenPathActMap.cs`, `Models.Cards/AscendersBane.cs` and `Folly.cs` (keywords), `Models.Events/ColossalFlower.cs`,
`TinkerTime.cs`, `Models.Monsters/KnowledgeDemon.cs`, `Models.Relics/GlassEye.cs`, `Entities.Cards/CardKeywordOrder.cs`.
The v107 tree renders all of them (e.g. ColossalFlower `_prizeCosts = {35, 75, 135}`) — cross-reference only.

### 2.2 What the legacy corpus is worth under v0.111.0 rules

`tmp/sim-port/diff-gate/taint.py`: replay every v0.107.1 combat window under v0.111.0 rules and stop at the first
decision where any card played so far, relic owned, monster present, power seen, orb channeled or potion used is
rule-diverging or v107-only. Conservative (no triage): 2,567 windows / 48,001 decisions → 335 untainted combats
(13 %, 4,175 decisions) + 2,898 decisions before the first taint = **≤ 7,073 comparable decisions (14.7 %)**. Top
taint sources: `LIGHTNING_ORB` 925 combats (orb API refactor, possibly behaviour-neutral), monsters 913, relics 639
(`PENDULUM` 123, `UNSETTLING_LAMP` 98, `POLLINOUS_CORE` 84). Known false positive: `NIBBIT` (the four agreeing
v0.107.1 bundles include `NIBBITS_WEAK`). Triage of refactor-only flags raises the yield by an unknown amount.

### 2.3 Options

| option | cost | yield | risk |
| --- | --- | --- | --- |
| (i) `GameVersion` switch with per-version rule branches | ≈ 190–200 real content branches (112 mechanical constant/enum swaps, ~118 behaviour ports of v107 members) + 39 version-exclusive classes (registry gating) + ~30–40 engine points ⇒ **≈ 260–300 branch points**, each needing legacy evidence | the whole legacy corpus: ~52.6 k in-combat + 13.8 k run-level decisions, 3× content coverage | doubles the verification surface; branches are dead weight for the A10 target (v0.111.0); every future game update adds a version (the playbook's "history never expires" turns into permanent branches) |
| (ii) v0.111.0 only + fresh corpus | zero branches; a self-play farm | unbounded v0.111.0 data; exact anchors with the §4 recorder additions | deep content (act 3, bosses, rare events) needs a competent or steered policy; the legacy corpus is lost |
| (iii) both | see recommendation | | |

**Recommendation: (iii), asymmetric.**

1. The sim implements v0.111.0 rules only. A `GameVersion` value (`V0_107_1`, `V0_110_1`, `V0_111_0`) reaches the sim
   through two seams: the RNG seeding scheme (`RngSeedingScheme`, exists) and a content-registry filter for the 39
   version-exclusive classes. No per-version rule branch by default.
2. The legacy corpus stays a regression and coverage asset through a **version-taint filter**: a triaged list (one
   entry per flagged file: `changed` / `refactor_only` / `unknown`, with the evidence) generated from the mechanical diff.
   The exporter compares a v0.107.1 segment up to the first decision touching a `changed`/`unknown` entity and marks the
   rest `unobserved` with reason `version_tainted:<id>`. Engine-level flags are triaged first because they taint
   everything (orb trigger API, hook renames, combat turn state).
3. On-demand branches, capped (proposal: ≤ 30): only for a `changed` entity whose legacy coverage cannot be replaced
   by farm data after a coverage push; each branch ships with a legacy bundle that proves it.
4. The primary oracle growth is v0.111.0: human play with the recorder additions of §4, plus an MCP self-play farm on
   headless instances (`scripts/headless_provision.sh`; isolated `STS2_RECORDER_OUTPUT_ROOT` so farm traces never mix
   with the human corpus; `scripts/mcp_mod.sh on` / `off` bracketing per CLAUDE.md). The farm's policy is
   coverage-guided (prefer unexercised cards/relics/events/paths; checkpoint restore through the eval API to branch from
   deep states); any decision sequence is a valid oracle trace, so the policy only needs reach, not strength.
5. v0.110.1 (8 lineages, no decompile) is a near-version: eligible through anchors only; a divergence that does not
   reproduce on a v0.111.0 trace is classified `version_drift`.

## 3. RNG and state alignment for every combat

### 3.1 What a combat start depends on

- 12 run streams and 3 player streams, each with a counter, persisting across the whole run. Names as stored in the
  save (`rng.rngs`): `up_front`, `shuffle`, `unknown_map_point`, `combat_card_generation`, `combat_potion_generation`,
  `combat_card_selection`, `combat_energy_costs`, `combat_targets`, `monster_ai`, `niche`, `combat_orbs`,
  `treasure_room_relics`; player: `rewards`, `shops`, `transformations`. A live v0.111.0 save at floor 18 reads
  `shuffle` 283, `up_front` 413, `niche` 18, `monster_ai` 7, `rewards` 136.
- Consumers (property-access scan outside `Nodes`): `shuffle`, `monster_ai`, `combat_targets`, `combat_card_selection`,
  `combat_energy_costs`, `combat_orbs` — combat code only; `combat_card_generation`, `combat_potion_generation`,
  `niche` — combat code and relics (15 relic files read `niche`), so they also move between combats; `up_front`,
  `unknown_map_point`, `treasure_room_relics`, `rewards`, `shops`, `transformations` — run layer.
- Private seeded generators with no counter history: the encounter `Rng` and per-monster `Rng`
  (`sim/specs/rng-encounter-private.md`), and the per-event `Rng` seeded from run seed + player slot + hash of the
  event id (`Models/EventModel.cs:91`).
- `Rng.Chaotic` is time-seeded (`Random/Rng.cs:20,283`); its uses are cosmetic (skins, VFX, screen shake, card-add
  timing; `Models/MonsterModel.cs:396-403` returns it only for canonical models). Two uses need a check: the Trial
  event's `EntrantNumber` variable and YummyCookie's cached random character.
- Non-RNG inputs: master deck in deck order (the opening shuffle permutes it), upgrades/enchantments/per-card props,
  relics with props, potions by slot, HP/max HP, gold, max energy, orb slots, ascension, and the encounter id (pre-rolled
  per act in the save's room sets: `normal_encounter_ids` + `normal_encounters_visited`, etc.).

### 3.2 (a) Import the game's own save at room entry

- The game writes `current_run.save` (plain JSON, ~80 KB, schema 20; `Saves.Managers/RunSaveManager.cs:226,241`) at
  three points: every map-point entry, after `ExitCurrentRooms` and before the room type is rolled and the room created
  (`Runs/RunManager.cs:4134` → `:574`, `:615-618`, then `:681-683`); combat end, after `MarkPreFinished` and before the rewards are offered
  (`Combat/CombatManager.cs:1747-1748`, rewards at `Rooms/CombatRoom.cs:554-556`); and after an ancient event (Neow)
  finishes (`Rooms/EventRoom.cs:415-417`). Loading re-enters without saving (`Runs/RunManager.cs:2363`), which is why
  retries restart with identical counters.
- The save holds everything a combat or a room needs: all 15 stream states (counter + `s0..s3`), the four
  unknown-map-point odds and the player's card-rarity/potion odds, shared and player relic grab bags, per-act room sets,
  deck (`id`, `current_upgrade_level`, `enchantment`, `props`), relics with `props`, potions, gold, HP, max energy, orb
  slots, `events_seen`, `visited_map_coords`, `map_point_history`, `pre_finished_room`, `num_reloads`.
- Recorder today: never copies it, although it already subscribes `SaveManager.Saved`
  (`mod/src/RunLifecycle.cs:231-262`), which forwards `RunSaveManager.Saved` (`Saves/SaveManager.cs:1028-1036`); the
  event is invoked after `WriteFileAsync` completes (`Saves.Managers/RunSaveManager.cs:186`).
- Passive extension (R1, §4.3): in that handler copy the profile-scoped `saves/current_run.save` to
  `native/saves/<seq:06d>.save` and log a `run_saved` event. Copy only; the original is never touched.
- Sim side: `RunState.FromSave(json)`; for the combat gate a subset is enough (RNG sets — `RunRngSetSnapshot` already
  loads real saves bit-exactly — plus deck, relics, potions, HP, gold, energy), then the room-entry and combat-setup
  code of the port.
- Two anchors per combat: entry (map-point save) and exit (pre-finished save). The exit anchor checks every stream's
  counter after the combat, including consumption with no visible effect.
- Existing corpus: the 708 `.mcr` initial states are the same anchor in binary form (`RecordInitialState` runs between
  the map-point save and the room roll, so it equals that save's state); decoding them needs a per-version
  `PacketReader` port and the net-id table (§1.3).

Verdict: exact for every combat that has a save, no search, no deck reconstruction; the only route that reaches 100 %
of new data. Legacy only through `.mcr`.

### 3.3 (b) Counter-alignment search from observed shuffles and intents

- Chain combats inside a lineage: when combat k−1 was replayed, the combat-only streams start combat k at the sim's
  end-state counters of k−1. Unknown counters (run-level consumption, or k−1 not replayable) become hypotheses
  `n ∈ [n_prev, n_prev + W]`, expanded lazily when the stream is first drawn.
- Evidence: `shuffle` — the opening hand and every `card_drawn` event (true draw order, by card id); `niche` — enemy
  max HP rolls and niche-reading relic effects; `monster_ai` — first intents; `combat_targets` — targets in
  `damage_received`; card generation — ids in `card_generated`. A beam of cloned sims (clone 2.5 µs) is pruned at each
  observation; accept only a unique survivor before the first compared decision, else `alignment_ambiguous`.
- Prerequisite: the deck order at combat k, reconstructed over all floors from `floor_summary` deltas (the exporter's
  Neow rules, generalised) and cross-checked against the combat's visible piles.
- Limits: event card references carry no upgrade flag (`mod/src/JsonDescribe.cs:73-84`); a stream first consumed
  without a visible effect stays unaligned; a wrong deck order looks like an alignment failure; legacy data is also
  version-tainted (§2.2).

Verdict: L effort, legacy-only value, yield unknown until prototyped and bounded by the taint filter.

### 3.4 (c) Replay the whole run in a RunSim from the seed and the recorded run-level decisions

- Every combat start is derived, not aligned, and the run layer is tested too (map generation, room rolls, encounter
  lists, Neow, events, rewards and odds, shops, treasure and grab bags, rest sites, transformations, run-level relic
  and potion hooks).
- Unanchored it is fragile: one early divergence poisons everything after it. Anchored (R1) it becomes room-by-room:
  run room k from anchor k, compare the next anchor field by field (§4.1).
- Legacy runs need v107 run-layer rules — excluded by §2.3.

### 3.5 Order of implementation

1. R1 + R2 in the recorder, before the farm starts, so every v0.111.0 trace has anchors (S).
2. Save importer (combat subset) + combat-at-anchor in the harness → the full-scale combat gate G1 (M).
3. `.mcr` decoder → 708 exact anchors in the existing corpus (604 v0.107.1, 41 v0.110.1, 63 v0.111.0) and exact
   action/choice streams for those combats (L).
4. Anchored RunSim diff (G2), area by area as the run layer lands (M per area + harness M).
5. Unanchored seed replay (G3) as end-to-end acceptance on v0.111.0 runs (S once G2 is green).
6. (b) for legacy combats without `.mcr`, only if the taint-filtered yield justifies it (L).

## 4. Run-level differential

### 4.1 Three comparison points

1. **Anchor diff** at every save: the save JSON against the sim's `RunState.ToSave()` (same schema), with an ignore list
   (`save_time`, `run_time`, `platform_type`, `map_drawings`, profile `unlock_state`/`discovered_*`). Hard on RNG
   counters and kernel state, odds, grab bags, room-set visited counters, deck with props, relics with props, potions,
   gold, HP, `events_seen`. It localises run-layer RNG mistakes to the room where they happen.
2. **Screen projection** before every run-level decision (§4.2).
3. **Floor summary** at every room exit: the recorder's `floor_summary` event is the game's own `MapPointHistoryEntry`
   (`player_stats`: card/relic/potion/event/ancient choices with offers, rest choices, deck deltas, gold and HP flows,
   `rooms[].model_id`/`monster_ids`/`turns_taken`). The RunSim produces the same entry; compared hard. It exists for
   100 % of the corpus today; the game's own `.run` history files (also outside the recorder, see
   `design-run-layer.md` §7.5) carry the same entries without per-decision data.

### 4.2 Screen projections

| screen | projected fields | recorder source | class | gap |
| --- | --- | --- | --- | --- |
| map (each decision; full graph once per act) | nodes (col, row, type, children), boss ids, current position, next options | `PassiveStateBuilder.Map.cs` | hard | — |
| room entry | room type after the unknown roll, encounter/event id | `room_entered` event | hard | — |
| Neow / ancients | offered options (TextKey), chosen | floor_summary `ancient_choice` (after the fact); live state has localized titles only | hard (post hoc) | R5 |
| event | event id, options (key, locked, proceed), page; outcome deltas | event state (localized titles, no keys, `PassiveStateBuilder.Rooms.cs:34-90`), `event_option.title_key`, floor_summary `event_choices` | hard after R5; before R5 option count + locked flags hard, titles soft | R5 |
| rewards | ordered items: type, gold amount, potion id, relic id, card offers | rewards state has gold and potion only (`PassiveStateBuilder.Selections.cs:23-60`); card offers only post hoc in `card_choices`; relic id only at claim | hard | R4 |
| card reward | offered cards (id, upgraded, enchantment), skip, reroll, alternatives | `card_reward` state captured 24 times in the whole corpus: no snapshot trigger fires while the screen is open, and the post-pick snapshot already shows the rewards screen | hard | R4 |
| shop | per slot: category, id, upgraded, price, on_sale, stocked; removal price | shop state | hard | — |
| rest site | options (id, enabled) | rest state | hard | — |
| treasure | offered relics after opening | treasure state, `pick_relic.relic_id` | hard | — |
| deck-op / relic / bundle selects | candidates (id, upgraded), min/max picks | select states (no min/max) | hard ids, soft prompt | R6 |
| player | HP, max HP, gold, relics (id, counter when shown), potions (slot, id), max slots | every state's `player` (`PassiveStateBuilder.Player.cs:25-150`) | hard | relic props: R1 |
| deck | multiset (id, upgraded, enchantment); order | not in any state; floor_summary deltas; final deck in `run_history.run` | hard multiset; order hard after R3 | R3 |
| hidden run state | 15 stream counters, odds, grab bags, room sets, card and relic props | none | hard via anchor diff | R1, R9 |

Combat projection v2 = v1 plus potions by slot, relic counters, orbs (id, passive, evoke), stars, pets (Osty HP, block,
powers), hand-card enchantments/afflictions/keywords, and, when the source provides them (MCP eval API `eval_meta.rng`,
`.mcr` checksum states, R9), RNG counters as a hard field.

### 4.3 What the recorder lacks, and the minimal passive additions

| # | addition | why | size | channel |
| --- | --- | --- | --- | --- |
| R0 | redo the hook-map drift check for v0.111.0, add it to `KnownGoodVersions` | all v0.110.1/v0.111.0 parts are `untested` | M | — |
| R1 | copy `current_run.save` on every `SaveManager.Saved` to `native/saves/<seq>.save` + `run_saved` event (seq, sha256, bytes, act, floor, room type, pre-finished); read bytes in the handler, write off the main thread, gzip at completion. Use the existing `Saved` subscription rather than a postfix on `SaveManager.SaveRun` (`design-run-layer.md` §7.4): `SaveRun` is async, so a postfix runs when it returns its `Task`, possibly before the file is written; `Saved` fires after the write (`Saves.Managers/RunSaveManager.cs:186`) | exact anchors (§3.2) | S | privileged |
| R2 | copy every `replays/latest.mcr` to `native/replays/<seq>.mcr` (no overwrite) | per-combat action log + checksum states | S | privileged |
| R3 | master deck in non-combat states and at combat setup: `[ {id, upgrade_level, enchantment {id, amount}} ]` in deck order | deck projection; opening-shuffle input | S | observation (order privileged if the UI does not show it) |
| R4 | reward offers in the rewards state: `CardReward.Cards` (id, upgraded, enchantment; populated before display, `Rewards/CardReward.cs:451,549-572`, `Rewards/RewardsSet.cs:77,94-96`), relic id, special card; the same list in `reward_taken`; reroll results | rewards projection | S | observation |
| R5 | event options: `TextKey`, relic id for relic options, page key | event / Neow projection | S | observation |
| R6 | choice identity: per pick (id, upgraded, enchantment, combat card index or deck index) and the candidate list; patch `PopModel` so `context_model` is the top of the `PlayerChoiceContext` stack | choices are model ids only (`mod/src/HumanFunnels.cs:662-760`) | M | observation |
| R7 | relic `props` in the player state | relic state between anchors | S | observation |
| R8 | settled post-action snapshot: re-take it once the action queue is idle (per-frame passive check) | 25 % of corpus decisions have a mid-resolution `state_seq` (`Snapshots.RequestDeferredSnapshot` fires one frame after the action, `mod/src/Snapshots.cs:161-173`) | M | observation |
| R9 | RNG counters (15 streams) at every action and snapshot in a privileged stream, or from the `ChecksumGenerated` tap, which today drops the `NetFullCombatState` (`mod/src/EventTap.cs:158-176`) | per-decision RNG check | S | privileged |
| R10 | Crystal Sphere cell clicks (only the resulting choices are recorded) | one event | S | observation |
| R11 | unlock-profile fields the save lacks (`DiscoveredActs`, `NumberOfRuns`, Wongo points), once at run start (`design-run-layer.md` §7.4) | Neow gating (Kaleidoscope), pools | S | privileged |

Hidden-information rule: the observation channel (`states.jsonl`) stays free of hidden state (the recorder sorts the
draw pile and omits the seed). R1, R2 and R9 go to `native/` or a new privileged stream; `text_env`, the human dataset
builders and any agent-facing tool must ignore them. Recorder changes ship through the CLAUDE.md flow
(`scripts/install_mod.sh` rebuild + reinstall, hook re-verification) and affect only new recordings.

## 5. Bundle schema v2

Principles: v1 stays byte-identical (`export-bundles` default) and so does the v1 harness path; v2 is a per-run
container of segments; a combat segment is a v1-shaped object; the harness reads a v1 file as a one-segment v2 run;
bundles stay outside the repository.

```jsonc
{
  "bundle_version": 2,
  "kind": "run",                                    // v1 files read as kind "combat"
  "source": { "lineage": "<start_time>-<seed>-<character>", "parts": ["<dir>", ...],
              "provenance": { "driver": "human" | "mcp", "mods": [...], "recorder_version": "0.2.0", "untested": false } },
  "game_version": "v0.111.0", "rules_version": "v0.111.0", "seeding": "xxh64", "seed": "PLRUV49CRFQW",
  "character": "SILENT", "ascension": 8, "game_mode": "standard", "modifiers": [],
  "anchors": [ { "id": "a12", "kind": "room_entry" | "combat_exit" | "ancient_exit" | "mcr_initial",
                 "seq": 1234, "act": 1, "floor": 7, "ref": "native/saves/001234.save", "sha256": "..." } ],
  "segments": [ {
      "id": "s7", "kind": "combat" | "room" | "map", "act": 1, "floor": 7,
      "room_type": "Monster", "room_model_id": "ENCOUNTER.X", "attempt": 1,
      "start_anchor": "a12", "end_anchor": "a13" | null,
      "start": { /* combat: the v1 top-level fields (player, deck, relics, potions, rng_offsets | anchor) */ },
      "decisions": [ { "index": 0, "run_index": 211, "screen": "combat" | "map" | "rewards" | "card_reward" | "shop"
                       | "rest_site" | "event" | "treasure" | "select",
                       "pre_state": { /* projection */ } | null,
                       "oracle": { "action_seq": 0, "state_seq": 0, "part": "...", "settled": true,
                                   "human": true, "source_hook": "...", "rng": { /* when available */ } },
                       "action": { "type": "...", ... } } ],
      "outcome": { "result": "win" | "loss" | "abandoned" | "left", "final_hp": 40 },
      "floor_summary": { /* normalised MapPointHistoryEntry */ },
      "eligibility": { "eligible": true, "reasons": [] } } ],
  "outcome": { "result": "win", "floor": 51 },
  "eligibility": { "run_reasons": [] }
}
```

Attempts (save & quit retries) are sibling segments sharing one start anchor: free counterfactual pairs.

Action union:

| type | fields | recorder source |
| --- | --- | --- |
| `play_card` | hand_index, card_id, upgraded, target_combat_id | `play_card` (v1 rules) |
| `end_turn` / `undo_end_turn` | — | `end_turn`, `undo_end_turn` |
| `use_potion` / `discard_potion` | slot, potion_id, target_combat_id, in_combat | `use_potion`, `discard_potion` |
| `choose` | context (combat, reward, rest, shop, event, deck_op), kind (hand, grid, choose_one, bundle, index), picks [{index, deck_index or combat_card_index, card_id, upgraded}], skipped | `player_choice` (+ R6) |
| `map_move` / `next_act` | col, row | `vote_for_map_coord`, `vote_to_move_to_next_act` |
| `event_option` / `event_proceed` | text_key, index | `event_option`, `event_proceed` |
| `reward_take` / `rewards_skip` | set_index, reward_type, item_id | `reward_taken`, `rewards_skipped` |
| `card_reward_pick` / `card_reward_skip` | index, card_id | `player_choice` Index + `reward_taken` card_reward |
| `treasure_open` / `treasure_pick` | index, relic_id, skipped | `open_chest`, `pick_relic` |
| `shop_buy` / `shop_remove` | category, item_id, price, slot | `shop_purchase`, `shop_card_removal` (+ `choose`) |
| `rest_option` | option_id | `rest_site_option` (+ `choose`) |
| `relic_select` | relic_id | `player_choice` on relic select |

The harness maps combat actions onto the kernel's `ActionKind` (`PlayCard`, `EndTurn`, `UsePotion`, `DiscardPotion`,
`ChoosePick`, `ChooseConfirm`; `design-combat-kernel.md`) — a `choose` becomes one `ChoosePick` per pick plus
`ChooseConfirm` where the prompt needs it — and run-level actions onto `RunActionKind` (`design-run-layer.md`).

Eligibility reasons v2 (in addition to the v1 classes): `anchor_missing`, `anchor_unreadable`, `anchor_mismatch`
(the save disagrees with the recorded state at the same seq), `alignment_ambiguous:<stream>`,
`alignment_unresolved:<stream>`, `deck_reconstruction_failed`, `version_tainted:<entity>`, `rules_version_unknown`,
`content_not_ported:<area>:<id>` (card, power, relic, potion, monster, encounter, event, enchantment, orb, affliction,
modifier), `run_layer_not_ported:<area>` (map, neow, events, rewards, shop, rest, treasure, transformations),
`recorder_gap:<field>` (card_reward_offer, relic_reward_id, event_option_key, choice_identity, deck_order,
crystal_sphere_clicks, turn_end_unrecorded), `segment_after_unclassified_divergence` (chained modes),
`provenance_mixed` (human and MCP parts in one lineage).

Exporter work items (`sts2rec`; S ≤ 1 agent-day, M 2–4, L 5–10):

| id | item | size |
| --- | --- | --- |
| X1 | lineage segmenter for every room: windows, attempts, post-combat resumes, QuickSL multi-part chains (generalises `find_first_combat`, `_combat_end`) | M |
| X2 | anchor index: `native/saves` (R1), `native/replays` (R2), `.mcr` initial states; sha256; `anchor_mismatch` check | S |
| X3 | combat segment builder for every combat (start from anchor; v1 builder reused for the decisions) | M |
| X4 | combat decisions v2: potions, in-combat `choose` with candidates from hand/card select states, `undo_end_turn` | M |
| X5 | run-level decisions and projections for all screens of §4.2 | L |
| X6 | legacy deck reconstruction over all floors from `floor_summary` deltas + pile cross-check | M |
| X7 | legacy alignment evidence (draw sequences, HP rolls, first intents, random targets, generated cards) | M |
| X8 | eligibility v2 + reasons index (run-level classes, version-taint list, recorder gaps) | M |
| X9 | provenance and per-decision oracle fields | S |
| X10 | v1 compatibility (`--v1` default output unchanged; `--v2`) | S |
| X11 | floor_summary normalisation | S |
| X12 | tests on synthetic fixtures per screen; v1 byte-identical regression | M |
| X13 | `.mcr` decoder (Python or shared with H15), per game version, verified against `modelIdHash` | L |

Harness work items (`Sts2Sim.Diff`):

| id | item | size | needs from the sim |
| --- | --- | --- | --- |
| H1 | v2 reader/writer (runs, segments, anchors, action union, provenance), v1 as one-segment v2 | M | — |
| H2a | save importer, combat subset (RNG sets exist; deck with upgrades/enchantments/props, relics with props, potions, HP, gold, energy, orb slots) | M | card/relic/potion models |
| H2b | full `RunState.FromSave` + `ToSave` projection | L | run layer |
| H3 | combat at anchor: room entry → combat setup with relic/potion hooks → first decision | M | room entry + combat setup |
| H4 | action mapping for the whole union + legality check | M | choice, potion and run-level decision API |
| H5 | projector v2 (combat additions, every run-level screen) | L | run layer |
| H6 | comparator v2: run-level field classes, anchor diff with ignore list, floor-summary diff | M | — |
| H7 | anchored replay mode: room k from anchor k, compare anchor k+1 and the floor summary | M | run layer |
| H8 | chained and seed-replay modes; resume at the next anchor after a divergence | M | RunSim |
| H9 | legacy alignment beam (lazy per-stream counter hypotheses) | L | `Rng` probe seam (first-use callback per stream) |
| H10 | coverage instrumentation: model × hook execution counters while replaying under comparison; oracle-side touched sets | M | a coverage sink in Core (no-op unless enabled) |
| H11 | report v2 + ratchet baseline and monotonic check | M | — |
| H12 | repro minimiser: shortest anchor + action prefix that still diverges, emitted as a `Test_Repro_*` skeleton | M | — |
| H13 | version handling: `rules_version`/seeding from the bundle, taint gate | S | — |
| H14 | spire-mentor sidecar (optional, §6.7) | M | — |
| H15 | `.mcr` reader in C#, if X13 is not done in Python | L | — |

## 6. Gate definition for the full port

### 6.1 Levels

| level | data | compared | status |
| --- | --- | --- | --- |
| G0 | first combats (v1) | combat decisions | live: 4/4 bundles, 69/69 decisions |
| G1 | every combat with an anchor (R1 saves, decoded `.mcr`) whose content is ported | combat decisions incl. choices and potions; outcome; exit anchor | after X1–X4, H1–H4 |
| G2 | every room with an anchor | run-level decisions, floor summaries, anchor diffs | per run-layer area |
| G3 | v0.111.0 runs from the seed | everything, unanchored | after G2 |

### 6.2 Coverage counters

Per content area (cards, powers, relics, potions, monsters, encounters, events, enchantments, orbs, afflictions,
modifiers, map generation per act, Neow options, shop, rewards, rest options, treasure, ascension levels) report, per
id: oracle-side touched decisions (played, drawn, generated, applied, owned at combat start, present, chosen, offered),
sim-side executions under comparison (H10: a model's hook or `OnPlay` ran between two compared decision points), agreed
touched decisions, distinct lineages, and first-divergence attributions. Hook-path coverage: per (model, overridden hook).

### 6.3 Thresholds

- A segment is certified when every compared decision agrees, its outcome and final HP agree, and its exit anchor agrees.
- Corpus level: decision agreement ≥ 99.9 % over all compared decisions with 100 % of divergences classified (the P0
  gate, carried forward); 100 % on v0.111.0 anchored segments of certified content.
- A content item is verified when it has ≥ 3 agreeing touched decisions in ≥ 2 lineages and every overridden hook ran
  at least once under comparison, or a waiver (directed unit test with a decompile citation + reason it is unreachable
  in corpus and farm). An area is verified when ≥ 90 % of its items are verified without waiver.
- Unobserved decisions (`pre_state: null`) never count as agreement and are reported per area; target ≤ 5 % on
  v0.111.0 data after R8.
- G2: 100 % of compared rooms agree on anchor diffs, floor summaries and screen projections. G3: ≥ 95 % of v0.111.0
  runs replay to their recorded end, the rest classified.

### 6.4 Divergence triage

| class | meaning | action |
| --- | --- | --- |
| `port_bug` | sim differs from the decompile | fix, freeze a regression test |
| `projection_gap` | the two projections measure different things (DisplayAmount overrides, visibility) | fix the projector |
| `exporter_bug` | bundle construction wrong | fix the exporter |
| `harness_bug` | setup or mapping wrong (precedent: the missing run context of `QC7ZPPWXEK`) | fix the harness |
| `recorder_gap` | field or timing missing (mid-resolution snapshot, missing offer) | replay unobserved; recorder ticket |
| `recorder_bug` | recorder wrote something wrong (precedent: sticky `context_model`) | fix the recorder; exclude affected parts |
| `anchor_error` | save or `.mcr` import, or legacy alignment, wrong | fix the importer or mark ambiguous |
| `version_drift` | legacy or near-version rule difference | exclude with reason; never counts as pass |
| `decompile_gap` | rule unreadable in v0.111.0 (LdMemberToken, missing bodies) | read IL / re-decompile; open question |
| `nondeterminism` | rule depends on `Rng.Chaotic`, timing or UI | exclude the field, document |
| `out_of_scope` | multiplayer, unsupported modifiers, mods that change rules | skip with reason |

### 6.5 Ratchet per content batch

1. Baseline before the batch: the harness writes `diff-baseline.json` (aggregates only: segment id and sha256 →
   status, agreed decisions, first divergence; per-area coverage; unobserved rate). Committed; no trajectories.
2. Port the batch (file-per-item agents, citations in `sim/specs/*.md`).
3. Re-export: the sim-registry scan widens eligibility; list the newly eligible segments.
4. Run the harness over the cumulative library (all versions, all modes).
5. Accept only if: every (segment, decision) agreed in the baseline still agrees; newly eligible segments touching
   batch items agree, or each divergence is classified, ticketed and not `port_bug`; batch items reach §6.3 minimums
   or carry waivers; the fuzz suite shows no new exception signature; unit tests pass.
6. Commit the new baseline. A later `port_bug` in an item uncertifies it in the baseline until fixed.
7. Every resolved divergence becomes a unit test built from its minimal repro (seed, start state, action prefix — not
   the raw trajectory).

### 6.6 Self-consistency tests that need no game

- F1 clone/hash: at random points `Hash(x) == Hash(Clone(x))`; stepping the clone never changes the source; equal
  action sequences give equal hash sequences.
- F2 determinism across processes, Debug/Release and machines (identical hash streams).
- F3 save round trip: `FromSave(ToSave(x))` hashes equal; `ToSave(FromSave(real save))` equals the real save modulo the
  ignore list (uses saved files, not the game).
- F4 legal-action closure: every legal action steps without exception; a sampled illegal action is rejected with the
  state unchanged; a non-terminal state always has a legal action, a terminal one none; every prompt has a legal answer.
- F5 invariants after every step: HP in [0, max], block ≥ 0, energy ≥ 0, gold ≥ 0, potions ≤ slots, card conservation
  across piles (+ generated − removed), unique creature ids, power owners consistent, RNG counters non-decreasing.
- F6 nightly soak: N random full runs (RunSim) across characters and ascensions; exception signatures deduplicated;
  step caps catch loops and soft locks.
- F7 metamorphic: interchangeable-copy hand indices give equal hashes; replay from a clone equals the original.
- F8 cross-mode: a combat built from an anchor and the same combat inside RunSim reach identical hash sequences.

### 6.7 spire-mentor as a secondary oracle

tfriedel/spire-mentor (Rust, MIT, pinned at `386d760b7bffd5d29ba50578d8b1286c9359285b` in `docs/sim-engine.md`, not
cloned here; default `V0_107_1`, v0.111.0 is a beta flag) already has an adapter, `scripts/bench/sim_engine.py`, with
the observation/ref contract of `run_bench.py`. Method: same seed and the same decision sequence (a real trace or our
sim's random policy) driven through both engines, both projected to the gate's projection, compared per decision.

Use it for long random runs and the run layer where game traces are scarce; a disagreement is a triage lead resolved by
reading the decompile or by a game trace, never a pass/fail input. Risks: it is not rule truth; both ports read the same
decompile, so agreement can be a shared misreading; its v0.111.0 support is beta; seed parity was verified only for
v0.107.1 (BENCHA0S01); its action space differs (`choose_boss_relic`, `potion_swap`, deck-op selects); its upstream
parity audit document is missing and boss-ancient offers are anchored to a v0.103.2 corpus. Verdict: worth it as a
cheap P2 cross-check after G1 runs; the gate never depends on it.

## 7. Open questions

1. The 13 v0.111.0 `LdMemberToken` gaps (shop layout, golden-path map, AscendersBane/Folly keywords, ColossalFlower,
   TinkerTime, KnowledgeDemon, GlassEye) need IL or a re-decompile with references.
2. The recorder is `untested` on v0.110.1/v0.111.0; the hook-map drift check must be redone before G1 trusts v0.111.0
   traces.
3. QuickSL (24 lineages): confirm it only drives the official save & quit / continue path and changes no rule or RNG.
4. Checksum events appear in only 13 v0.107.1 parts. In v0.111.0 `InitializeShared` enables checksums in single player
   (`Runs/RunManager.cs:3780-3781`), subscribes the post-action checksum (`:3797`) and `ChecksumTracker` raises
   `ChecksumGenerated` for each one (`Multiplayer.Game/ChecksumTracker.cs:233`), yet no v0.110.1/v0.111.0 part has a
   `checksum` event: find out why (subscription timing, threading, recorder build) before relying on the tap for R9.
5. `Rng.Chaotic` in the Trial event (`EntrantNumber`) and YummyCookie: display only, or rule input?
6. Is the potion `TargetType.Self` → `AnyPlayer` change inert in single player?
7. The eval API (`eval_meta.rng`, checkpoints) comes from a patch in the hack-sts2-cloud repository; confirm the farm's
   MCP build carries it.
8. v0.110.1 has no decompile; whether it ever gets one decides between "near version" and exclusion.
9. Is the master-deck order exactly acquisition order with in-place upgrades and transforms appended (the exporter's
   assumption), for every deck operation? R3 removes the question for new data.
10. Card-reward rerolls and `CardRewardAlternative` options: which offers does `floor_summary.card_choices` keep after a
    reroll?
