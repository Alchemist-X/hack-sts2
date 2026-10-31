# Review fixes 2026-10-30 — integrate report + adversarial findings

Fix stage after the P0 integration and its adversarial review. Rule truth: decompile tree (game v0.111.0),
cited as `<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped; `v107:` = the v0.107.1
tree (`artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/`). Corpus = the recorder sessions root,
read only. This file records what was changed and why, and (§3) the replacement text for the shared docs
(`diff-bundle.md`, `monsters.md`, `CONTRACTS.md`), which this stage does not edit.

## 1. Findings → classification → action

| # | Sev. | Finding | Classification | Action (where) |
| --- | --- | --- | --- | --- |
| 1 | major | `player_choice` with context `SINGLETON.MULTIPLAYER_SCALING_MODEL` skipped as "automatic" | exporter bug (contract text wrong): the recorder label is the last model pushed on any `PlayerChoiceContext`, never popped (`mod/src/HumanFunnels.cs` `_lastChoiceContextModel`); `Hooks/Hook.cs:268-275` pushes every hook listener, the `MultiplayerScalingModel` singleton included (`Models.Singleton/MultiplayerScalingModel.cs:19`, never asks for a choice) | every in-combat `player_choice` → `unsupported_action:player_choice` + note (`diff_export.extract_decisions`); label kept only as `STICKY_CHOICE_CONTEXT_LABEL` for the note. Corpus: +5 first-combat attempts flagged (`UWP6NM93JF`, `QV7LMELKUQ`, `NUFWHA7VGU` ×3), all character-blocked today; eligible set unchanged |
| 2 | major | the map state counted as combat end (false `win`, truncated window, `final_hp`, dropped reasons) | exporter bug: the player can open the map view mid-combat (61 map snapshots between battle snapshots of one room) | `map` removed from `COMBAT_END_STATE_TYPES`; a non-battle state ends the combat only after the room's last battle snapshot (`_combat_end`); an action whose `state_seq` is a map-view snapshot uses the latest battle snapshot before it (`oracle.view_state_seq` records the map seq). Corpus: 42 of 2 982 combat windows ended early before (19 really end at `Combat ended`, 2 at `run_ended` abandoned, 21 save&quit from the map view); first combat `1792615807-4RHDW1LUA9` attempt 1: 6 → 20 decisions, end `combat_ended_message`, the map-view play (seq 96) now has a pre_state |
| 3 | minor | `monsters-slimes-weak.md` §11 stale harness-context point | doc | marked resolved; the stock harness agrees 18/18 on `QC7ZPPWXEK` |
| 4 | minor | contract pins the six-argument `CombatSim.Create` (always `CombatRunContext.Default`) | contract text (shared) | replacement text §3.1; `diff-harness.md` §2 already documents the context overload the harness uses |
| 5 | minor | "act-1 weak pool" claims in `rng-encounter-private.md` §4.1/§5.3 omit Underdocks (also act index 0) | doc scope; conclusions hold (checked: `Models.Acts/Underdocks.cs:44,71,81,84,87`; no `MonsterModel.Rng` reader, `Toadpole` uses `Rng.Chaotic` `:320-321`, `CorpseSlug` takes the encounter `Rng` `:480`; null slots `CorpseSlugsWeak.cs:32,34`, `SeapunkWeak.cs:22`, `SludgeSpinnerWeak.cs:19`, `ToadpolesWeak.cs:28-29`) | §4.1 and §5.3 extended |
| 6 | minor | shared docs (`monsters.md` §1/§8, `CONTRACTS.md` §4.3/§4.7/§4.8) not merged | doc (shared) | replacement text §3.2, §3.3 |
| 7 | minor | per-monster `Rng` seed gets `null` map coordinates; legacy formula throws | unreachable in P0 (no ported rule reads it) | documented as a pre-condition for the first opt-in monster, with the recorder source of the coordinate (`rng-encounter-private.md` §8) |
| 8 | minor | `Slimed.AddToCombatAndPreview` rejects pet targets | unreachable from slime moves at all: targets = `CombatState.PlayerCreatures` (`Models/MonsterModel.cs:269`) = creatures with `Player != null` (`Combat/CombatState.cs:560`, `Entities.Creatures/Creature.cs:509`); pets are monster creatures (`Commands/PlayerCmd.cs:51,951`, `Entities.Players/PlayerCombatState.cs:375-387`) | documented (`monsters-slimes-weak.md` §11) |
| 9 | minor | recorder 0.2.0 + `STS2_MCP` parts log turn ends only as `ready_to_begin_enemy_turn` | exporter gap (fail loudly, never guess) | `ready_to_begin_enemy_turn` with no non-cancelled `end_turn` since the previous one → `turn_end_unrecorded:seq<N>`. Corpus: only `1784043137-CPSVMWJPUL` (5 turn ends), already ineligible |
| 10 | minor | `hand_index` contract text vs exporter; interchangeability blind to per-instance state | contract text (shared) + exporter hardening | copies are interchangeable only when equal in every recorded hand field except `index` and `description` (the latter carries the targeted card's damage preview: `1786880759-X20BJH1S4R` seq 110, Strikes read 6 and 9); a post-action match on (id, upgraded, cost) counts only for such copies (`diff_bundle.resolve_hand_index`). Corpus: 1 119 of 58 447 same-(id, upgraded, cost) hand groups differ in such a field (enchantments, Soulbound, …); no hand_index of the 142 exported first-combat attempts changed. Contract text §3.1 |
| 11 | minor | `Niche` KALEIDOSCOPE term 6 depends on unlocked characters | the dependence is real but pinned at Neow: `Kaleidoscope.IsAllowedAtNeow` (`Models.Relics/Kaleidoscope.cs:116-123`) requires every character unlocked, for the Neow option (`Models.Events/Neow.cs:303,358`) and through `NEOWS_BONES` (`Models.Relics/NeowsBones.cs:186`); 2 loops × (5 − 1 − 1) `NextInt` draws (`:53,56,114`, `Extensions/ListExtensions.cs:10-35`, `Models/ModelDb.cs:129-136`); same in v0.107.1 | made explicit (`diff_bundle.KALEIDOSCOPE_NICHE_DRAWS`, test), `diff-bundle-exporter.md` §2 |

The findings list handed to this stage was cut off inside finding 11; anything after it was not seen.

Integrate-report divergences: all were classified by the integrate stage (harness context bug, fixed; exporter
ineligible `CPSVMWJPUL`; recorder snapshot timing in `--pre-state snapshot`). The re-run (§2) shows no new or
unclassified divergence; `CPSVMWJPUL` now also carries `turn_end_unrecorded`.

Not changed (outside this stage's files): the recorder (`mod/src/HumanFunnels.cs` should report the top of the
`PlayerChoiceContext` stack: patch `PopModel`, `GameActions.Multiplayer/PlayerChoiceContext.cs:12-44`), and
`sts2rec/sts2rec/human_dataset.py:300-310`, which still compares `combat_card_index` with hand positions.

## 2. Verification (2026-10-30)

- `dotnet build sim/Sts2Sim.sln -c Release`: 0 warnings, 0 errors. Sim tests: 367/367 (no sim code changed).
- `sts2rec` tests: 680 passed, 4 skipped (5 new: map-view lineage end to end, combat end with screens between
  battle snapshots, choices + unrecorded turn ends, keyword-differing copies, damage-preview copies; one assertion
  added to the Niche test). Each fix was reverted in turn through a pytest plugin (scratchpad): every revert fails
  at least one new test.
- Export (`settled`, read-only corpus): 142 attempts in 133 lineages, 4 eligible (v0.107.1: `VW67B447SR`,
  `X20BJH1S4R`, `STFR70JJFB`, `QC7ZPPWXEK`), byte-identical to the export before the fixes; 1 918 decisions (was 1 904),
  477 unsettled (402 rolled forward, 75 unobserved). Reason classes changed only by `unsupported_action` 18 → 23 and
  `turn_end_unrecorded` 1.
- `Sts2Sim.Diff`: 4/4 agree, 69/69 decisions compared and agreed, outcome and `final_hp` 4/4, 0 soft diffs, 0 errors,
  0 unrecognized keys, exit 0. Controls: `--pre-state unobserved` 45/45 + 24 unobserved, outcomes 4/4;
  `--pre-state snapshot` all four stop at their first mid-resolution snapshot (`player.discard_count`, recorder
  timing); the `--include-ineligible` export: 4 agree + 138 skipped, and with the harness flag the only extra run is
  `CPSVMWJPUL` (hard at decision 0, `hand[0].id` `BASH` vs `DEFEND_IRONCLAD`: Neow not recorded).

## 3. Replacement text for the shared docs (to merge)

### 3.1 `diff-bundle.md`

Top-level schema, add after `"final_hp"`:

```jsonc
  "ineligible_reasons": [ "..." ]                // 仅 --include-ineligible 写入 ineligible/ 的 bundle；
                                                 // harness 默认跳过，--include-ineligible 时才重放
```

Decision entry, replace `"pre_state": { <projection> },` with:

```jsonc
      "pre_state": { <projection> } | null,     // actions[].state_seq 对齐的快照投影；该快照取于上一张牌
                                                 // 结算中途时，按其间事件前滚到结算后（settled 模式）；
                                                 // 无法前滚 ⇒ null = 未观测：重放、不比较
      "oracle": { ... },                         // exporter 溯源：action_seq, state_seq, settled,
                                                 // pending_events, combat_card_index, view_state_seq,
                                                 // rolled_forward / unobserved_reason, snapshot_pre_state；
                                                 // 消费端原样保留（分歧复现里的 oracle_provenance），不比较
```

Replace the paragraph "决策定义 … 交叉校验。" with:

> 决策定义：`play_card` + `end_turn`。排除 `ready_to_begin_enemy_turn`（每次回合结束后都会记录；若自上一个
> 起没有记录 `end_turn` ⇒ 不合格 `turn_end_unrecorded`）。战斗内的任何 `player_choice`（弃牌、选牌…）v1
> 无法表达 ⇒ 不合格 `unsupported_action:player_choice`；recorder 的 `context_model` 是最后一次
> `PushModel` 的模型且从不出栈，`SINGLETON.MULTIPLAYER_SCALING_MODEL` 标签不表示自动选择。`hand_index` =
> 该实例在 pre_state 手牌中的位置：`combat_card_index` 是 NetCombatCardDb id，不是手牌位置；exporter 用可见
> 手牌求解（除 `index`/`description` 外各字段都相同的副本可互换，取第一张；否则须由动作后快照唯一确定，否则
> 不合格 `hand_index_ambiguous`）；`card_id`/`upgraded` 作交叉校验。动作的 `state_seq` 指向战斗中打开的地图
> 视图快照时，用其前最近的战斗快照（`oracle.view_state_seq` 记录地图快照）。

Replace the `rng_offsets` comment's formula line with:

```jsonc
      // 首战配方：Niche = 6·[KALEIDOSCOPE] + 1·[NEW_LEAF] + 1·[NEOWS_BONES]
      // （6 = 2 次 × StableShuffle(其余 4 个角色卡池) 各 3 次 NextInt；Neow 只在全角色解锁时提供 KALEIDOSCOPE，故恒为 6）
```

Replace the "Create 签名" item of "sim 侧约定" with:

> - Create 签名：`CombatSim.Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId,
>   int ascension, int? currentHp, int? maxHp, CombatRunContext context)`，`CardSpec = (string Id, bool Upgraded)`，
>   `context = CombatRunContext.ForAct(source.act, source.floor, null, null, CombatRunContext.ParseSeeding(seeding))`。
>   遭遇私有 Rng 的种子取决于 seeding 与 TotalFloor（`SLIMES_WEAK` 组成、`CORPSE_SLUGS_WEAK` 起手招式）；不带
>   context 的六参数重载恒用 `CombatRunContext.Default`（xxh64、floor 2），对 legacy_v18 或非 2 层的 bundle 是错的。
>   deck 顺序 = bundle 顺序，不排序。

### 3.2 `monsters.md`

§1, after the "Chosen: **`NibbitsWeak`** …" paragraph, add:

> Ported since (2026-10-30): all eight weak encounters of act index 0 — Overgrowth's `FuzzyWurmCrawlerWeak`,
> `NibbitsWeak`, `ShrinkerBeetleWeak`, `SlimesWeak` and Underdocks' `CorpseSlugsWeak`, `SeapunkWeak`,
> `SludgeSpinnerWeak`, `ToadpolesWeak` (`Models.Acts/Underdocks.cs:44,71,81,84,87`), specified in
> `monsters-wurm-beetle.md`, `monsters-slimes-weak.md`, `monsters-corpseslugs-seapunk.md` and
> `monsters-underdocks-weak.md`; `SlimesWeak` draws its composition and `CorpseSlugsWeak` its starters from the
> encounter `Rng` (`rng-encounter-private.md` §3).

§8, replace the first three bullets (per-monster `Rng`, encounter `Rng`, `SortEnemiesBySlotName`) with:

> - Per-monster `MonsterModel.Rng`, encounter `Rng` and `SortEnemiesBySlotName`: ported 2026-10-30
>   (`rng-encounter-private.md` §3-§5). The encounter `Rng` is seeded through `CombatRunContext` (seeding scheme,
>   `TotalFloor`); the per-monster `Rng` is a seam (`MonsterDef.UsesPrivateRng`/`InitPrivateRng`) that no P0
>   monster uses — the bundle path passes no map coordinate and the v0.107.1 formula is not ported
>   (`rng-encounter-private.md` §8); the setup slot sort runs after each slotted monster's `SetUpForCombat`.

### 3.3 `CONTRACTS.md`

- §4.2: add `static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension,
  int? currentHp, int? maxHp [, CombatRunContext context])` (rng deep-cloned; without `context` =
  `CombatRunContext.Default`) and `readonly record struct CardSpec(string Id, bool Upgraded)` (`Api/CardSpec.cs`).
- §4.3, §4.7, §4.8: merge the block of `rng-encounter-private.md` §6 verbatim (`RngSeedingScheme`,
  `CombatRunContext`, `RngSeeds.LegacyV18Encounter`; `MonsterDef.UsesPrivateRng`/`InitPrivateRng`,
  `EncounterDef.Slots`/`GenerateMonsters(cs, rng)`/`EncounterRngRequired`, the `MonsterSpawner` context overloads
  and slot sort; the `CombatLoop` context / `EncounterDef` overloads).
