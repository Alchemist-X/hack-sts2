# Run layer: gap analysis and port design

W0 design deliverable of the P1 full port (`docs/p1-sim-full-port-plan-2026-11-01.md`).
Scope: every rule outside a single combat that a complete 1:1 single-player environment needs —
run creation, act selection, map generation, room flow, rewards and odds, relic grab bags,
merchant, rest site, treasure, the event framework, Neow and the act-start ancients, ascension
run effects, out-of-combat potions and deck prompts, map-point history, and save import.
Machine-readable companion: `sim/specs/inventory/run-layer-gaps.json`.

## 中文摘要

- run 层在 sim 里**完全不存在**：P0 只有 `CombatSim`，牌组/HP/RNG 由调用方传入。需要新建
  `RunState`/`RunPlayer`、地图、房间流、奖励/赔率、两个遗物抓袋、商店、营地、宝箱、事件框架、
  Neow 与幕初远古事件、存档导入；W2 估算约 1.46 万行（代码+测试，不含 W3 内容）。
- 进阶只有 **A0–A10**（`AscensionManager.maxAscensionAllowed = 10`）；P1 计划里的 "A1–A20"
  应更正。A1 精英 ×1.6、A2 远古回血 80%、A3 金币 ×0.75、A4 药水槽 −1、A5 进阶之厄、
  A6 删牌涨价、A7 稀有率/升级率减半、A8/A9 怪物数值（在怪物文件里）、A10 第三幕双 Boss。
- STS2 没有 "Boss 遗物"：Boss 只给金币+药水+必稀有卡；对应的"选遗物"是**下一幕起点的远古事件**
  （第二幕 Orobas/Pael/Tezcatara(+Darv)，第三幕 Nonupeipe/Tanx/Vakuu(+Darv)）。
- 关键 1:1 陷阱：地图点 `Children/parents` 是 `HashSet<MapPoint>`（枚举序 = 增删序列），
  卡牌稀有度/药水/未知点赔率全是 float 累加；遗物抓袋有两只（共享袋只含共享池且可刷新，
  读档后刷新被关闭）；`CardRarityOdds.regularCommonOdds` 的"静态初始化一次"陷阱在 v0.111.0
  **不影响规则**（Common 概率从不参与掷骰）。
- 若干规则在 UI 节点代码里（`NMapScreen` 可走点、`NTreasureRoom` 跳过、`NRewardsScreen`
  继续、`NPotionPopup` 药水可用性、`NRestSiteRoom` 离开条件、宝箱遗物 `RelicCmd.Obtain`），必须当规则移植。
- v0.111.0 反编译丢失了 9 处静态数组初值（`GoldenPathActMap`、商店卡牌类型表等），需 IL 复核；
  暂用 v0.107.1 值。
- 存档只在三个边界写：进入地图点（房间类型未掷）、战斗胜利（奖励未生成）、远古事件结束。导入器按
  `SerializableRun` 的字段恢复，三种存档点分别对应 RunSim 的三个入口相位。本地 168 个 v0.111.0
  `.run` 历史（schema 10）+ 存档可作 run 层 oracle：Neow 三选项、地图、每层遭遇、卡牌/药水/遗物报价。

## 0. Conventions

- Rule truth is the v0.111.0 tree (`output/server-bootstrap/linux-decompiled/`). Citations are
  `<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped. Async methods are
  cited at their state-machine `MoveNext` lines; line order inside a state machine is **not**
  execution order — every order stated below was traced through the `goto` labels (example:
  `Entities.RestSite/HealRestSiteOption.cs:138` heal → `:179` `AfterRestSiteHeal` → `:167`
  rewards → `:168` offer).
- `[v107]` marks a cross-reference into the v0.107.1 tree
  (`artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/<Folder>/<File>.cs`). It is used
  only (a) where the v0.111.0 text lost data (static array blobs, §9.2) or (b) as a readability
  aid for a method whose v0.111.0 body was checked to have the same calls. Every (a) use is an
  open question until re-verified against v0.111.0 IL.
- Target configuration: one player, `GameMode.Standard`, no modifiers, `NetGameType.Singleplayer`
  (not "fake multiplayer"), `TestMode.IsOff` semantics everywhere (`TestMode` branches change
  rules: §9.4), local player `NetId = 1` (`Nodes/NGame.cs:1989`).
- "Node-layer rule": a rule implemented in `MegaCrit.Sts2.Core.Nodes.*` UI code. The sim must
  port these as rules even though Nodes are otherwise ignored (list in §9.3).

## 1. Gap summary

| # | Subsystem | Game sources | Sim today | Gap |
| --- | --- | --- | --- | --- |
| 1 | Run state, player run side | `Runs/RunState.cs`, `Entities.Players/Player.cs` | none; `PlayerState` is combat-only, deck/HP are `CombatSim.Create` inputs | full |
| 2 | Run/player RNG sets | `Runs/RunRngSet.cs`, `Random/PlayerRngSet.cs` | complete (`Random/RngSets.cs`, snapshots, seed formulas in `RngSeeds`) | consumers only; ad-hoc map/act-selection streams |
| 3 | Odds | `Odds/*.cs` | none | full |
| 4 | Relic grab bags, relic factory | `Runs/RelicGrabBag.cs`, `Factories/RelicFactory.cs` | none | full |
| 5 | Map generation | `Map/*.cs` | none | full |
| 6 | Acts, room sets, encounter/event pools | `Models/ActModel.cs`, `Models.Acts/*.cs`, `Rooms/RoomSet.cs`, `Helpers/GrabBag.cs` | encounter id is a `CombatSim` input | full |
| 7 | Room flow (enter/exit/stack, act change, victory) | `Runs/RunManager.cs`, `Rooms/*.cs` | none | full |
| 8 | Rewards | `Rewards/*.cs`, `Commands/RewardsCmd.cs`, `Factories/{CardFactory,PotionFactory}.cs` | none | full |
| 9 | Merchant | `Entities.Merchant/*.cs`, `Rooms/MerchantRoom.cs` | none | full |
| 10 | Rest site | `Entities.RestSite/*.cs`, `Rooms/RestSiteRoom.cs`, `Multiplayer.Game/RestSiteSynchronizer.cs` | none | full |
| 11 | Treasure | `Rooms/TreasureRoom.cs`, `Multiplayer.Game/{TreasureRoomRelicSynchronizer,OneOffSynchronizer}.cs`, `Entities.TreasureRelicPicking/*.cs` | none | full |
| 12 | Event framework | `Models/EventModel.cs`, `Events/EventOption.cs`, `Rooms/EventRoom.cs`, `Multiplayer.Game/{EventSynchronizer,EventCombatSynchronizer}.cs` | none | framework W2, 68 event defs W3 |
| 13 | Neow, act-start ancients, victory | `Models/AncientEventModel.cs`, `Models.Events/{Neow,Orobas,Pael,Tezcatara,Nonupeipe,Tanx,Vakuu,Darv,TheArchitect}.cs` | Neow arrival HP formula only (`StartingDecks.HpAfterNeowArrival`) | full |
| 14 | Ascension run effects | `Entities.Ascension/*.cs`, `Helpers/AscensionHelper.cs` | A5 bane for the starter deck; A8/A9 inside ported monsters | A1–A4, A6, A7, A10 |
| 15 | Run-scope hooks | `Hooks/Hook.cs`, `Models/AbstractModel.cs` | 0/46 run-scope, 12 both-scope missing (`sim/specs/inventory/hooks.json`) | W1 delivers dispatchers |
| 16 | Deck prompts | `Commands/CardSelectCmd.cs`, `TestSupport/ICardSelector.cs` | none (P0 forbids prompts) | W1 API, W2 run call sites |
| 17 | Potions out of combat | `GameActions/{UsePotionAction,DiscardPotionGameAction}.cs`, `Commands/PotionCmd.cs`, `Nodes.Potions/NPotionPopup.cs` | none | full |
| 18 | Map-point history, score | `Runs.History/*.cs`, `Runs/ScoreUtility.cs` | none | minimal subset |
| 19 | Unlock/profile dependence | `Unlocks/UnlockState.cs`, `Timeline.Epochs/*.cs` | implicit "all unlocked" | explicit profile input |
| 20 | Save import | `Saves/SerializableRun.cs`, `Saves.Runs/*.cs`, `Runs/RunState.cs:584-595` | `RunRngSetSnapshot` only | full |

## 2. Run state model

The game splits a run into `RunState` (shared) and `Player` (per player). The sim keeps that
split as `RunState` + `RunPlayer` (one player). The existing `CombatState`/`PlayerState` stays
the combat container; §8.3 defines how it attaches to the run.

### 2.1 `RunState` fields that rules read

| Field | Game | Rule readers (examples) | Sim representation | Clone | Canonical |
| --- | --- | --- | --- | --- | --- |
| `Rng` (12 streams + seeds) | `Runs/RunState.cs:477`, `Runs/RunRngSet.cs:33-55` | everything (Appendix A) | existing `RunRngSet` | deep (existing) | existing tag 0x5203 |
| `Odds.UnknownMapPoint` | `Runs/RunState.cs:486`, `Odds/RunOddsSet.cs:22-25` | `RollRoomTypeFor` | `UnknownMapPointOdds`: `_nonEventOdds[4]`, `_baseOdds[4]` (float32, fixed key order Monster, Elite, Treasure, Shop) | value copy | 8 floats by bit pattern |
| `SharedRelicGrabBag` | `Runs/RunState.cs:495`, created `refreshAllowed: true` (`:554`) | treasure relics (§3.7) | `RelicGrabBag` (§5.4) | deep (lists of def refs) | ordered id lists per rarity + `RefreshAllowed` |
| `UnlockState` | `Runs/RunState.cs:504`, union of players (`:679`) | pools, events, ancients, acts, boss discovery (§5.6) | `UnlockProfile` (immutable) | shared | hash of sorted ids, written once |
| `AscensionLevel` | `Runs/RunState.cs:468` | §6.4 | `int` 0..10 | value | i32 |
| `GameMode` | `Runs/RunState.cs:459` | tutorial gate `Runs/RunManager.cs:4090-4109` | constant `Standard` | — | — |
| `Acts[3]` | `Runs/RunState.cs:338` | all room pulls | `ActInstance { ActDef, RoomSet, SharedAncientSubset }` | deep (RoomSet lists are def refs + counters) | act id + RoomSet |
| `CurrentActIndex`, `ActFloor`, `NextRoomId` | `Runs/RunState.cs:346-371`, `:415` | map travel, rooms ids, `TreasureRoom` ctor check | ints; setter side effects (`:353-360`: clears visited coords, `ActFloor = 0`, `NextRoomId = 0` on change) | value | i32 ×3 |
| `Map` (current act only) | `Runs/RunState.cs:376` | travel, `TryHandleSpoilsMap`, blacklist | `ActMap` (§4) | **shared** topology + cloned quest overlay (§2.6) | topology hash once per act + quest overlay |
| `VisitedMapCoords` | `Runs/RunState.cs:325`, `:763-772` | current point, travel, saves | `List<MapCoord>` | copy | list |
| `MapPointHistory` | `Runs/RunState.cs:327`, `:801-818` | `TotalFloor` (`:423`), shop blacklist (`Runs/RunManager.cs:3978-3986`), first-run checks, score | §2.5 minimal entries | copy | entries (type, room types, model ids) |
| `VisitedEventIds` | `Runs/RunState.cs:331`, `:796-799` | `RoomSet.EnsureNextEventIsValid` (`Rooms/RoomSet.cs:122`) | `HashSet<string>` (only `Contains`) | copy | sorted ids |
| `ExtraFields` | `Runs/ExtraRunFields.cs:9-27` | `StartedWithNeow` (`Runs/RunManager.cs:327`, `:2146`), `TestSubjectKills`, `FreedRepy` | 3 fields | value | bool, i32, bool |
| `_currentRooms` (room stack) | `Runs/RunState.cs:329`, `:774-794` | event combats, `IsPreFinished`, `ProceedFromTerminalRewardsScreen` | `List<RoomInstance>` (§2.4) | deep | list |
| `_allCards` | `Runs/RunState.cs:333`, `:718-761` | ownership bookkeeping only | not ported; card instance ids instead | — | — |
| `Modifiers`, `BadgeModels`, `MultiplayerScalingModel` | `Runs/RunState.cs:515-548`, `:650-652` | listeners; SP effect is nil (§9.5) | empty / no-op listener | — | — |

### 2.2 `RunPlayer` fields

| Field | Game | Notes | Clone / canonical |
| --- | --- | --- | --- |
| `CharacterId`, `MaxEnergy`, `BaseOrbSlotCount` | `Entities.Players/Player.cs:145`, `:319`, `:373`; values from `Models.Characters/*.cs` | Ironclad 80 HP, Silent 70, Defect 75 (3 orb slots), Regent 75, Necrobinder 66; all 99 gold, 3 energy (`Models/CharacterModel.cs:63-71`) | value |
| `CurrentHp`, `MaxHp` | `Player.Creature` (`:152`) persists across combats | combat copy-in/copy-out boundary (§8.3) | i32 ×2 |
| `Gold` | `:253-271` | `PlayerCmd.GainGold/LoseGold/SetGold` (`Commands/PlayerCmd.cs:882-932`) | i32 |
| `Deck` | `:312-316` | ordered deck `CardInstance`s with upgrade level, enchantment (id, amount, props), saved props (6 cards: `Dowsing`, `GeneticAlgorithm`, `Guilty`, `MadScience`, `SpoilsMap`, `TheScythe`), `FloorAddedToDeck` | deep; list order |
| `Relics` | `:129`, `:241`, `:883-947` | ordered `RelicInstance`: def, `[SavedProperty]` counters (40 relics, e.g. `PenNib.AttacksPlayed`, `Girya.TimesLifted`, `SilverCrucible.TimesUsed/TreasureRoomsEntered`), `StackCount` (2 stackable), `IsWax`/`IsMelted`, `Status`, `FloorAddedToDeck` | deep; list order |
| `PotionSlots` | `:131`, `:243`, `:984-1096` | list with `null` holes, length = `MaxPotionCount`; slot index matters (first null slot gets the next potion, `:1027-1030`; shrinking compacts, `:996-1015`) | deep |
| `PlayerRng` (Rewards, Shops, Transformations) | `:166`, seeded `XXH64(seedText) + slot` (`:729-733`) | existing `PlayerRngSet` | existing tag 0x5204 |
| `PlayerOdds` (card rarity offset, potion reward odds) | `:175`, `Odds/PlayerOddsSet.cs:31-35` | both draw from `PlayerRng.Rewards` | 2 floats |
| `RelicGrabBag` (player bag) | `:184`, created `new RelicGrabBag()` = `refreshAllowed: false` (`:714`) | elite/dig/event relics and shop relics (§5.4) | deep |
| `ExtraFields` | `Entities.Players/ExtraPlayerFields.cs:9-45` | `CardShopRemovalsUsed` (removal price), `WongoPoints`; damage/debuff/badge stats are log-only | i32 ×2 (+ stats if kept) |
| `UnlockState` | `:191` | per-player copy, union into run (`Runs/RunState.cs:679`) | shared |
| `CanUseOrRemovePotions` | `:282-297` | cleared/set inside three events (`Models.Events/{TheFutureOfPotions,RanwidTheElder,StoneOfAllTime}.cs`) | bool |
| `IsActiveForHooks` | `:215`, alive at construction (`:703`) | hook gate | bool |
| `Discovered*`, `MaxAscensionWhenRunStarted` | `:274`, `:328-364` | profile bookkeeping, no rules | not ported |

### 2.3 Acts and room sets

`ActInstance` = act def + `RoomSet` (`Rooms/RoomSet.cs:15-180`): `events` (ordered, pre-shuffled
at run start) + `eventsVisited`; `normalEncounters` (weak first, then regular) +
`normalEncountersVisited`; `eliteEncounters` (15) + `eliteEncountersVisited`;
`bossEncountersVisited`; `Boss`; `SecondBoss`; `Ancient`. Pulls are by modulo index
(`:74-90`); counters advance in `MarkVisited` (`:92-112`) only when the room is the base of the
room stack and not pre-finished (`Runs/RunManager.cs:1342-1348`). `SharedAncientSubset` is set
for acts after the first (`Runs/RunManager.cs:4058-4068`, `Models/ActModel.cs:216-221`). All
three acts are generated at run start (§6.2), so the room sets of later acts are fixed early and
appear in every save (`Saves.Runs/SerializableRoomSet.cs`).

### 2.4 Transient room state (lives inside the current map point)

| State | Game | Sim |
| --- | --- | --- |
| room stack entries | `Runs/RunState.cs:774-794`; `AbstractRoom.Id` from `NextRoomId` (`Rooms/AbstractRoom.cs:40-44`) | `RoomInstance { RoomType, ModelId, Id, IsPreFinished, payload }` |
| combat room | `Rooms/CombatRoom.cs:600-662`: `GoldProportion`, `ExtraRewards`, `ShouldResumeParentEventAfterCombat`, `ParentEventId` | payload + embedded `CombatState` |
| rewards stack | `Multiplayer.Game/RewardsSetSynchronizer.cs:576-829` (per player stack of `RewardsSet`) | `List<RewardsSet>`; each reward keeps its populated content and `SuccessfullySelected` |
| merchant inventory | `Entities.Merchant/MerchantInventory.cs:21-44` | 5 character + 2 colorless card entries, 3 relic, 3 potion, removal entry |
| rest-site options | `Multiplayer.Game/RestSiteSynchronizer.cs:531-553` | ordered option list + `ProceedAllowed` |
| treasure | `Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:46-52`, `Nodes.Rooms/NTreasureRoom.cs:454-456` | current relics, `ChestOpened`, `SingleplayerSkipped`, relic claimed |
| event instance | `Models/EventModel.cs:302-405` | def, private `Rng`, current options (page), `IsFinished`, `WasChosen` flags, per-event fields |
| pending prompt | `Commands/CardSelectCmd.cs:3441-3666`, `Commands/RelicSelectCmd.cs:163` | `PendingPrompt` (§3.12): kind, candidate instance ids, min, max, flags, partial selection, continuation key |

### 2.5 Map-point history: the minimal subset

`MapPointHistoryEntry { MapPointType, Rooms[{ RoomType, ModelId, MonsterIds, TurnsTaken }],
PlayerStats[...] }` (`Runs.History/MapPointHistoryEntry.cs`, `Runs/PlayerMapPointHistoryEntry.cs`).
Rules read only: the entry count (`TotalFloor`, `Runs/RunState.cs:423`, used by encounter
seeding, `IsBeforeAct3TreasureChest` in 16 relics' `IsAllowed` — `Models/RelicModel.cs:539-543`
— and `RelicCmd.Obtain` floor stamps), whether the previous entry holds a `Shop` room
(`Runs/RunManager.cs:3981`), and first-run counts (§9.6). Everything in `PlayerStats` is a log
(card/relic/potion choices, bought items, rest choices) except `GoldGained`, which
`ScoreUtility.GetScoreForGoldGained` reads (`Runs/ScoreUtility.cs:119-122`). The sim keeps the
entry list with types, room types, model ids and per-entry `GoldGained`; the full stats block is
an optional logging feature (it is also the run-history oracle, §7.5).

### 2.6 Clone and canonical implications

- **Map topology is immutable after generation.** No code outside `Map/` adds or removes
  children/parents or changes point types after `GenerateMap` returns (searched all
  `AddChildPoint`/`RemoveChildPoint`/`PointType =` writes; only `Runs/RunManager.cs:2148` sets the
  start type, inside generation). The only mutable per-point data is the quest marker list
  (`Map/MapPoint.cs:12`, `:430-448`; writers `Models.Cards/SpoilsMap.cs:197`, `:216`,
  `Models.Relics/FurCoat.cs:320`, `:341`). Clones therefore share the `ActMap` object and copy a
  small quest overlay keyed by coord (quest entries store the owning instance id, never a model
  reference).
- **.NET collection order.** `MapPoint.Children`/`parents` are `HashSet<MapPoint>` with
  reference equality (`Map/MapPoint.cs:14`, `:42`, `:82-85`; no `GetHashCode` override). .NET
  `HashSet<T>`/`Dictionary<K,V>` enumerate their entry array in index order; `Remove` pushes the
  slot on a LIFO free list that the next `Add` reuses, so enumeration order is a pure function of
  the Add/Remove sequence and does **not** depend on hash codes. The port uses the BCL types and
  replays the exact Add/Remove sequence (pruning and Spoils redirection remove children). A clone
  of a set that ever had a removal must not be rebuilt by re-adding (that compacts the free list);
  with immutable shared maps this never arises. `RelicGrabBag._deques` is a
  `Dictionary<RelicRarity, List<RelicModel>>` (`Runs/RelicGrabBag.cs:18`) whose key order (first
  appearance during `Populate`) drives the shuffle order; keys are never removed.
- **Floats.** Odds are float32 and accumulate (`Odds/CardRarityOdds.cs:62`,
  `Odds/PotionRewardOdds.cs:39-42`, `Odds/UnknownMapPointOdds.cs:168`); canonical form writes the
  bit pattern; the save importer must parse JSON numbers as float32 (§7.3).
- **Defs vs instances.** Relic/potion/card/event/act/encounter defs are immutable and shared.
  Mutable per-instance state (relic counters, card props, enchantment amount, event page) lives in
  instances with stable instance ids; cross references (combat card → deck card, quest → owner,
  reward → card) are ids.
- **Canonical tags** (proposal, disjoint from rng 0x52xx, state 0x53xx, powers 0x54xx,
  monsters 0x4Dxx, cards 0xCAxx): run state 0x55xx, map 0x56xx, relic/potion/enchantment
  instances 0x57xx, rewards/merchant/rest/treasure 0x58xx, events/prompts 0x59xx.
- Budget: run-state clone must stay ≤1 ms with an embedded combat; the dominant cost is the deck
  and relic lists (≤ ~60 objects), so the P0 clone budget holds if maps are shared.

### 2.7 Hook listener order at run scope

`RunState.IterateHookListeners(childCombatState)` (`Runs/RunState.cs:830-933`): for each active
player — every deck card in deck order, each followed by its enchantment; then, only when no combat
is attached, for each active player the non-melted relics in list order and the potions in slot
order (non-null); then modifiers, badges and the multiplayer scaling model; then mod subscribers;
then, when a combat is attached, the combat's own listeners. Each candidate passes `Contains`
(`:1037-1073`: removed cards/relics/potions are skipped). Dispatcher scopes per hook are
classified in `sim/specs/inventory/hooks.json` (`run`: `IterateHookListeners(null)`; `both`:
`IterateHookListeners(combatState)`). The W1 kernel owns this iterator; the run layer only needs
it to include deck cards, enchantments, relics and potions of the `RunPlayer`.

## 3. Run flow and decision points

### 3.1 New run (exact order)

1. **Acts.** `StartRunLobby.BeginRunLocally`: `rng = new Rng(XXH64(seedText), "act_selection")`
   (`Multiplayer.Game.Lobby/StartRunLobby.cs:705`; the string hash is taken without the `old`
   prefix path), `ActModel.GetRandomList(rng, unlock, isMultiplayer)` (`Models/ActModel.cs:470-507`):
   per act index (`Models/ModelDb.cs:244-275`; index 0 = Overgrowth, Underdocks; 1 = Hive;
   2 = Glory) take the unlocked acts; an unlocked non-default act that the **profile** has not
   discovered (`SaveManager.Progress.DiscoveredActs`) is forced; otherwise `NextItem`. The lobby's
   `Act1` choice (default `"random"`, character-select dropdown) then overrides index 0
   (`StartRunLobby.cs:236-242`, `:707`, `:737-748`, `Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:1436-1439`).
   A random character is resolved on the same rng afterwards (`StartRunLobby.cs:708-715`). The sim
   takes the act list (or the `act1` choice) as an input and also implements `GetRandomList`.
2. **Player.** `Player.CreateForNewRun` (`Entities.Players/Player.cs:712-717`): starting deck with
   `FloorAddedToDeck = 1` (`:1098-1117`), starting relics (`:1141-1161`), starting potions
   (`:749-761`), HP = `StartingHp`, gold = `StartingGold`, 3 potion slots, empty player grab bag
   (`refreshAllowed: false`). Unlock state from the profile (`Nodes/NGame.cs:1988`).
3. **RunState.** `RunState.CreateForNewRun` (`Runs/RunState.cs:550-582`): `RunRngSet(seed)`,
   `RunOddsSet(Rng.UnknownMapPoint)`, shared `RelicGrabBag(refreshAllowed: true)`, players attached,
   `Player.InitializeSeed` → `PlayerRngSet(XXH64(seedText) + slot)` and fresh `PlayerOddsSet`
   (card rarity offset −0.05, potion odds 0.4), `CardModel.AfterCreated` per deck card.
4. **`SetUpNewSingleplayer`** (`Runs/RunManager.cs:3647-3669`) → `InitializeShared` (creates the
   `AscensionManager`, `:3801`) → `InitializeNewRun` (`:3826-3869`):
   a. shared bag `Populate(SharedRelicPool.GetUnlockedRelics(unlock), UpFront)` (`:3828`);
   b. player bag `PopulateRelicGrabBagIfNecessary(UpFront)` (`:3835`);
   c. `ExtraFields.StartedWithNeow = unlock.IsEpochRevealed<NeowEpoch>()` (`:3924-3927`);
   d. modifiers `OnRunCreated` (none in Standard);
   e. `AscensionManager.ApplyEffectsTo(player)` (`Entities.Ascension/AscensionManager.cs:27-39`):
      A4 −1 potion slot, A5 `ASCENDERS_BANE` appended with `FloorAddedToDeck = 1`.
5. **`GenerateRooms`** (`Runs/RunManager.cs:4055-4088`), all on `UpFront`, §6.2.
6. **`NGame.StartRun`** (`Nodes/NGame.cs:2091-2134`): `FinalizeStartingRelics` = `AfterObtained`
   on every starting relic in order (`Runs/RunManager.cs:1975-2013`), `Launch`, then
   `EnterAct(0, doTransition: false)`.

`UpFront` draw order at run start is therefore: shared-bag shuffles (one `UnstableShuffle` per
rarity deque, `n−1` `NextInt` each, `Extensions/ListExtensions.cs:21-36`), player-bag shuffles,
shared-ancient shuffle and per-act subset sizes, then per act: event shuffle, weak/regular/elite
grab-bag draws, boss `NextItem`, ancient `NextItem`, and at A10 the second boss. A real save taken
at the Neow point (save point A) carries the resulting `up_front.counter` (413 at act 2 start in
the local A8 Silent save), the shared/player bag lists and all three room sets — the run-start
oracle (§7.5).

### 3.2 Entering an act

`EnterAct(i)` (`Runs/RunManager.cs:142-398`, traced): `FadeOut`; `ClearScreens`;
`ExitCurrentRooms` (`:252`); `SetActInternal(i)` (`:305`, body `:2747-2770`: set
`CurrentActIndex`, clear visited coords and `ActFloor`, `Odds.UnknownMapPoint.ResetToBase()`,
`GenerateMap`); then if `i == 0 && StartedWithNeow` → `EnterMapCoord(Map.StartingMapPoint)`
(`:327-333`, the Neow point) else `EnterRoomInternal(new MapRoom())` (`:343`); `FadeIn`;
`Hook.AfterActEntered(State)` (`:316`). `GenerateMap` (`:2107-2158`): a saved map for this act
index is used if present (load path) → `Hook.ModifyGeneratedMapLate`; else
`Act.CreateMap` → `Hook.ModifyGeneratedMap`; then `Hook.AfterMapGenerated`; if
`!StartedWithNeow && actIndex == 0` the start point becomes `Monster` (`:2146-2149`); finally
`RemoveStaleVisitedMapCoords`. For acts 2 and 3 the player starts on the map with no visited
coord, so the only travelable point is the row-0 `Ancient` start point (§3.5).

### 3.3 Entering a map point

`EnterMapCoord` (`Runs/RunManager.cs:4124-4135`) rejects an already visited coord
(`Runs/RunState.cs:763-772`), then `EnterMapPointInternal(row + 1, pointType)`
(`Runs/RunManager.cs:4151-4159`, body `:569-720`):

1. `UpdatePlayerStatsInMapPointHistory` if history exists (`:569-572`, log fields only).
2. `ActFloor = coord.row + 1` (`:573`).
3. `ExitCurrentRooms` (`:574`): top room first; `RewardsSetSynchronizer.BeforeLeavingRoom` skips
   every open rewards set (`OnSkipped` on unclaimed rewards,
   `Multiplayer.Game/RewardsSetSynchronizer.cs:805-829`), then `room.Exit` (merchant writes
   unsold stock to history, `Rooms/MerchantRoom.cs:200-281`; treasure ends relic picking,
   `Rooms/TreasureRoom.cs:318-322`; events run `EnsureCleanup`, `Rooms/EventRoom.cs:263-277`).
4. **Save point A**: `SaveManager.SaveRun(null)` (`:616-627`).
5. Room type: `Unknown` with a pre-finished room → `Monster` (no roll); else
   `blacklist = BuildRoomTypeBlacklist(previous history entry, current point's children)` — `Shop`
   if the previous point had a shop room or every child is a shop (`:3978-3986`) — and
   `RollRoomTypeFor` (`:4207-4227`; first-run override `:4229-4255`, §9.6).
6. `CreateRoom` (`:4178-4205`): Monster/Elite/Boss → `Act.PullNextEncounter(type).ToMutable()`;
   Treasure → `TreasureRoom(actIndex)` (index 0..2 enforced, `Rooms/TreasureRoom.cs:294-301`);
   Shop → `MerchantRoom`; Event → `Act.PullAncient()` for an `Ancient` point, else
   `Act.PullNextEvent(state)`; RestSite → `RestSiteRoom`.
7. `AppendToMapPointHistory(pointType, room.RoomType, room.ModelId)` (`:687`;
   `Runs/RunState.cs:801-818`) — from here `TotalFloor` counts this point.
8. `EnterRoom` → `EnterRoomInternal` (`:1303-1365`): push; `Hook.BeforeRoomEntered` (not for
   `MapRoom`); `room.Enter` (assigns `NextRoomId`); `Act.MarkRoomVisited(roomType)` when this room
   is the stack base.

### 3.4 `RunSim` API, phases, legal-action order

```csharp
enum RunPhase : byte { Map, Combat, Rewards, CardReward, Shop, RestSite, Treasure, Event, Prompt, Victory, Defeat }
enum RunActionKind : byte {
    PlayCard, EndTurn, UsePotion, DiscardPotion,                    // combat + potions (any phase)
    Travel,                                                          // index into LegalMapPoints
    ClaimReward, ProceedRewards,                                     // rewards screen
    PickRewardCard, RewardAlternative,                               // card-reward screen
    BuyEntry, LeaveShop,                                             // merchant (entry index = AllEntries order)
    ChooseRestOption, LeaveRestSite,
    OpenChest, TakeTreasureRelic, SkipTreasureRelic,
    ChooseEventOption, LeaveEvent,
    PromptSelect, PromptConfirm, PromptCancel                        // generic prompt (§3.12)
}
readonly record struct RunAction(RunActionKind Kind, int A, int B);
sealed class RunSim {
    static RunSim NewRun(RunConfig config);                  // seed text, character, ascension, acts|act1, UnlockProfile
    static RunSim FromSave(SavedRun save, ImportOptions options);   // §7
    RunPhase Phase; bool IsTerminal; RunState State; CombatSim? Combat;
    IReadOnlyList<RunAction> LegalActions(); void LegalActions(List<RunAction> buffer);
    bool IsLegal(RunAction a); void Step(RunAction a);
    RunSim Clone(); ulong StateHash(); void WriteCanonical(CanonicalWriter w);
}
```

- `Step` runs until the next decision (automatic steps — chest gold, act transition, map
  generation, room rolls — are not decisions). A decision exists only where the game waits for
  input; every player-choice prompt is a decision (P1 plan rule).
- Legal actions are listed in a fixed order: the phase's own actions by ascending index, then
  potion actions (slot ascending, then target), then the leave/proceed action last — the P0 rule
  "`EndTurn` last" generalised.
- `Combat` phase delegates to the embedded combat (P0 `GameAction` mapped 1:1). `CombatSim`
  remains usable stand-alone for combat-only search.
- Recorder mapping (state_type → phase): `map`→Map, `monster|elite|boss|hand_select`→Combat,
  `rewards`→Rewards, `card_reward`→CardReward, `shop|fake_merchant`→Shop/Event, `rest_site`→RestSite,
  `treasure`→Treasure, `event`→Event, `card_select|bundle_select|relic_select|crystal_sphere`→Prompt
  (`mod/src/StateBuilder/PassiveStateBuilder.cs:62-200`; legal-action derivation in
  `sts2rec/sts2rec/legal_actions.py:101-240`). The game's own auto-player lists the same screens
  (`AutoSlay.Handlers.Rooms/*`, `AutoSlay.Handlers.Screens/*`).

### 3.5 D1 — Map travel and unknown points

- **Option set** (node-layer rule, `Nodes.Screens.Map/NMapScreen.cs:1388-1425`): no visited coord
  → the start point; last visited = first boss and a second boss exists → second boss; last
  visited row = `RowCount − 1` → boss; else `MapTravel.GetTravelablePointsFrom` = children, or the
  whole next row when `Hook.ShouldAllowFreeTravel` (WingedBoots, Flight modifier)
  (`Map/MapTravel.cs:9-16`). Order: column ascending (the recorder sorts the same way,
  `mod/src/StateBuilder/PassiveStateBuilder.Map.cs:52-55`).
- **Mutation**: `MoveToMapCoordAction` → `RunManager.EnterMapCoord` (`GameActions/MoveToMapCoordAction.cs:63-119`) → §3.3.
- **Unknown point** (`Odds/UnknownMapPointOdds.cs:99-177`), one `UnknownMapPoint.NextFloat`:
  allowed = {Monster, Elite, Treasure, Shop, Event} − blacklist, then
  `Hook.ModifyUnknownMapPointRoomTypes` (LanternKey card, JuzuBracelet, GoldenCompass);
  default result Event if allowed, else the lowest enum value; walk `_nonEventOdds` in key order
  Monster(0.1), Elite(−1), Treasure(0.02), Shop(0.03) summing non-negative odds of allowed types
  and pick the first with `roll <= cumulative`; then reset the picked type to its base and add
  `Hook.ModifyOddsIncreaseForUnrolledRoomType(base)` to every other allowed non-event type (an
  Event pick raises all of them; Elite keeps drifting further negative). `ResetToBase` at every
  act start (`Runs/RunManager.cs:2749`). First-run override (`:109-120`): §9.6.

### 3.6 D2 — Rewards screen (incl. card reward, full potion slots)

**Generation** (`Rooms/CombatRoom.cs:176-299`: per player `RewardsCmd.GenerateForRoomEnd`, then
`Hook.BeforeCombatRewardOffered`, then `Offer`). `RewardsSet.WithRewardsFromRoom`
(`Rewards/RewardsSet.cs:416-433`): no rewards for the final act's boss; first-run tutorial
tables (§9.6); else `GenerateRewardsFor` (`:473-515`); then the combat room's `ExtraRewards`
(event combats). Composition and **draw order on `PlayerRng.Rewards`**:

| Room | Rewards (list order) | Draws at construction | Draws at `Populate` (list order) |
| --- | --- | --- | --- |
| Monster | Gold (if `GoldProportion > 0`; min/max scaled and `Math.Round`ed), Potion?, Card×3 | potion-odds `NextFloat` (`:517-525`) | gold `NextInt(min, max+1)`; potion rarity `NextFloat` + `NextItem`; per card: rarity `NextFloat`, `NextItem`, upgrade `NextFloat` |
| Elite | Gold, Potion?, Card×3, Relic | potion-odds `NextFloat` (elite bonus +0.125) | gold; potion; cards; relic rarity `NextFloat` (`Factories/RelicFactory.cs:54-63`) then player-bag pull (no rng) |
| Boss (acts 1–2) | Gold (100), Potion?, Card×3 (BossEncounter odds: always Rare) | potion-odds `NextFloat` | gold; potion; cards |

`GenerateWithoutOffering` (`Rewards/RewardsSet.cs:68-121`): `Populate` each reward in list order,
`Hook.ModifyRewards` (relics PrayerWheel, WhiteStar, BlackStar, LavaRock, AmethystAubergine,
WongosMysteryTicket, Driftwood; the Midas/Vintage modifiers are excluded),
populate the added rewards, `Hook.AfterModifyingRewards`, then **sort by `RewardsSetIndex`**
(Gold 1, Potion 2, Relic 3, SpecialCard 4, Card 5, CardRemoval 7; linked set = max of members;
`Rewards/GoldReward.cs:101`, `PotionReward.cs:113`, `RelicReward.cs:102`,
`SpecialCardReward.cs:105`, `CardReward.cs:423`, `CardRemovalReward.cs:86`,
`LinkedRewardSet.cs:17`). The display order is the sorted order; the rng order is the unsorted one.

**Legality and mutation** (`Rewards/Reward.cs:242-258`, `Multiplayer.Game/RewardsSetSynchronizer.cs:624-734`):
any not-yet-`SuccessfullySelected` reward may be claimed, in any order.

- Gold → `PlayerCmd.GainGold` (`Hook.ModifyGoldGained`: BowlerHat, Ectoplasm).
- Potion → `PotionCmd.TryToProcure` (`Commands/PotionCmd.cs:253`; `Hook.ShouldProcurePotion`:
  Sozu). Slots full → `TooFull`, the reward stays unclaimed (`Rewards/PotionReward.cs:176-195`);
  the player may discard or use a potion (§3.13) and claim again. `NotAllowed` counts as taken.
- Relic → `RelicCmd.Obtain` (`Commands/RelicCmd.cs:403-425`: add, remove from both bags unless
  stackable, `FloorAddedToDeck = TotalFloor`, `AfterObtained`; the `RelicObtained` event lets a
  pending card reward re-modify its options, `Rewards/CardReward.cs:574-589`).
- Card → opens the card-reward screen (phase `CardReward`): options = the 3 cards, then
  `CardRewardAlternative.Generate` (`Entities.CardRewardAlternatives/CardRewardAlternative.cs:67-89`):
  `Skip` (ends selection, reward **not** completed, stays claimable), `REROLL` when `CanReroll`
  (Driftwood; rerolls with `RerollOptions`, `Rewards/CardReward.cs:761-782`), plus
  `Hook.ModifyCardRewardAlternatives` (PaelsWing `SACRIFICE`, completes the reward); at most 2.
  Picking a card → `CardPileCmd.Add(card, Deck)` (`Hook.ShouldAddToDeck`,
  `TryModifyCardBeingAddedToDeck`: Frozen/Molten/Toxic egg upgrades, FresnelLens);
  `Hook.ShouldAllowSelectingMoreCardRewards` may keep the screen open (`:648`; no implementer in
  v0.111.0).
- SpecialCard → add to deck. CardRemoval → removal prompt (`Rewards/CardRemovalReward.cs:106-112`).
- **Proceed** (node-layer rule, `Nodes.Screens/NRewardsScreen.cs:668-718`): post-combat (terminal)
  set → `ProceedFromTerminalRewardsScreen` (`Runs/RunManager.cs:2443-2462`): resume the parent
  event if this was an event combat (`ResumePreviousRoom`, `:2541-2587`), else open the map;
  unclaimed rewards are skipped only when the room is exited (§3.3 step 3). Boss rewards proceed
  → act change (§3.14). Non-terminal sets (`OfferCustom` from events and rest site) →
  `SkipLocalRewardsSet` immediately (`OnSkipped` on unclaimed rewards, `:750-771`).
- `OnSkipped` only writes history (choice logs) — no rule effect.

### 3.7 D3 — Treasure room

- **At room entry** (`Rooms/TreasureRoom.cs:236-263`): `Hook.AfterRoomEntered`, then
  `TreasureRoomRelicSynchronizer.BeginRelicPicking` (`Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:137-203`):
  per player, if `Hook.ShouldGenerateTreasure` (SilverCrucible returns false): rarity
  `RelicFactory.RollRarity(Rng.TreasureRoomRelics)` — `NextFloat` < 0.5 Common, < 0.83 Uncommon,
  else Rare — then the first-run Gorget override (§9.6) ?? **shared** bag `PullFromFront(rarity)`
  ?? `Circlet` (`Factories/RelicFactory.cs:13`). The relic is fixed before the chest is opened.
- **Open chest** is forced: the proceed button is disabled until the chest is open
  (node-layer rule `Nodes.Rooms/NTreasureRoom.cs:640-646`). The sim keeps `OpenChest` as an
  explicit action (the only non-potion action until opened) so that potion use before opening
  stays expressible. `OpenChest` (`:172-330`):
  `DoNormalRewards` → `OneOffSynchronizer.DoTreasureRoomRewards` (`Multiplayer.Game/OneOffSynchronizer.cs:650-667`):
  if `ShouldGenerateTreasure`, gold = `PlayerRng.Rewards.NextInt(42, 53)` (42–52), ×0.75 at A3,
  `(int)` → `GainGold`, plus `SpoilsMap.OnQuestComplete` when the point carries the quest
  (`:670-700`); then `DoExtraRewardsIfNeeded` (`Rooms/TreasureRoom.cs:47-171`:
  `GenerateForRoomEnd(treasure room)` has no base rewards, `Rewards/RewardsSet.cs:482-488`; hook
  rewards are offered as a nested set); then the relic choice.
- **Choice**: take (index 0) or skip. Take → `PickRelicAction` → `OnPicked` → `AwardRelics` →
  `RelicsAwarded` → `RelicCmd.Obtain` (node-layer rule,
  `Nodes.Screens.TreasureRoomRelic/NTreasureRoomRelicCollection.cs:505`). Skip = proceed-as-skip
  (`Nodes.Rooms/NTreasureRoom.cs:551-565`; single player sets `_singleplayerSkipped`,
  `TreasureRoomRelicSynchronizer.cs:258-262`). No relic → `CompleteWithNoRelics` (`:402-407`).
- **Relic-picking fight** exists only with ≥2 voters on one relic
  (`Entities.TreasureRelicPicking/RelicPickingResult.cs:24-102`: rounds of Rock/Paper/Scissors
  drawn with `TreasureRoomRelics.NextItem`, a move loses when `(m1 + 1) % 3 == m2`; unclaimed
  relics `StableShuffle`d as consolation prizes, `TreasureRoomRelicSynchronizer.cs:292-377`).
  Single player never reaches it; fake-multiplayer bots vote with the same stream (`:172-192`).
  Port the algorithm as a tested pure function, unreachable in the SP sim.

### 3.8 D4 — Shop

- **Inventory at room entry** (`Rooms/MerchantRoom.cs:85-86`, before `AfterRoomEntered` at
  `:115`): `MerchantInventory.CreateForNormalMerchant` (`Entities.Merchant/MerchantInventory.cs:87-109`), draw order:
  1. `Shops.NextInt(5)` = on-sale slot (`:118`);
  2. five character entries for types Attack, Attack, Skill, Skill, Power (static blob lost in
     v0.111.0 at `:196-206`; values from [v107] `Entities/Merchant/MerchantInventory.cs:15-22`, §9.2),
     each `MerchantCardEntry.Populate` (`MerchantCardEntry.cs:202-229`): pool minus cards already
     in the shop → `CardFactory.CreateForMerchant` (`Factories/CardFactory.cs:34-56`):
     `Hook.ModifyMerchantCardPool`, drop Basic, player-count filter, rarity =
     `Hook.ModifyMerchantCardRarity(PlayerOdds.CardRarity.RollWithoutChangingFutureOdds(Shop))`
     (**`Rewards` `NextFloat`**), next allowed rarity with that type, `Shops.NextItem`, then
     `RollForUpgrade(-999999999)` (**`Rewards` `NextFloat`**, never upgrades, `:239-242`);
     `Hook.ModifyMerchantCardCreationResults`; price `Shops.NextFloat(0.95, 1.05)`
     (`MerchantCardEntry.cs:253-265`); the sale slot calls `CalcCost` again after `Populate` (a
     second `Shops.NextFloat`) and halves (`MerchantInventory.cs:125-128`, `MerchantCardEntry.cs:247-251`);
  3. two colorless entries (Uncommon, Rare): `CreateForMerchant(rarity)` (`Factories/CardFactory.cs:58-69`:
     `Shops.NextItem`, `Rewards` upgrade float), price ×1.15 (`MerchantCardEntry.cs:171-184`);
  4. relics: `RollRarity(Rewards)` ×2 **first**, then Shop rarity
     (`MerchantInventory.cs:144-157`); each entry pulls the **player** bag **from the back** with
     `IsAllowedInShops` (`MerchantRelicEntry.cs:153-156`), removes it from the shared bag, price
     `MerchantCost × Shops.NextFloat(0.85, 1.15)` with `Math.Round` (`:158-161`; costs C 175,
     U 225, R 275, Shop 200, `Models/RelicModel.cs:307-318`);
  5. potions: `CreateRandomPotionsOutOfCombat(3, Shops)` (all three potions first, no duplicates,
     `Factories/PotionFactory.cs:27-61`), then each entry's price `Shops.NextFloat(0.95, 1.05)`
     (C 50, U 75, R 100; skipped in TestMode, `MerchantPotionEntry.cs:178-200`);
  6. removal entry: `BaseCost + PriceIncrease × CardShopRemovalsUsed` (75/25, A6: 100/50,
     `MerchantCardRemovalEntry.cs:319-332`).
- **Displayed price** = `Hook.ModifyMerchantPrice` (MembershipCard, TheCourier) while in a
  `MerchantRoom` (`MerchantEntry.cs:165-176`). Legal buy: stocked and `Cost <= Gold`
  (`:308-321`). Card buy fails if `CardPileCmd.Add` fails; potion buy fails when full or
  forbidden (`MerchantPotionEntry.cs:203-225`); on success: lose gold, then
  `Hook.ShouldRefillMerchantEntry` (TheCourier) → restock (re-roll from `Shops`/`Rewards`) else
  clear, then `Hook.AfterItemPurchased` (MawBank).
- **Removal**: available unless `Hook.ShouldAllowMerchantCardRemoval` is false (Hoarder
  modifier), which marks the entry used when the slot is filled (node-layer rule
  `Nodes.Screens.Shops/NMerchantCardRemoval.cs:153-156`); buying opens a cancelable removal prompt
  (cancel = no charge); success → lose gold, remove, `CardShopRemovalsUsed++`
  (`Multiplayer.Game/OneOffSynchronizer.cs:610-626`) and the entry becomes used for this visit
  (`MerchantCardRemovalEntry.cs:377-381` → node-layer `NMerchantCardRemoval.OnCardRemovalUsed`,
  `Nodes.Screens.Shops/NMerchantCardRemoval.cs:210-214`, → `SetUsed`, `MerchantCardRemovalEntry.cs:393-396`).
- Leave at any time (`LeaveShop`).

### 3.9 D5 — Rest site

- **Options** (`Entities.RestSite/RestSiteOption.cs:47-68`, generated at room entry by
  `RestSiteSynchronizer.BeginRestSite`, `Multiplayer.Game/RestSiteSynchronizer.cs:531-553`):
  Heal, Smith (+Mend in multiplayer), then `Hook.ModifyRestSiteOptions` in listener order: DIG
  (Shovel), LIFT (Girya, `TimesLifted < 3`), COOK (MeatCleaver), KINDLE (PumpkinCandle), CLONE
  (PaelsGrowth), HATCH (ByrdonisEgg card). `IsEnabled`: Smith needs an upgradable card
  (`SmithRestSiteOption.cs:247`), Cook needs ≥2 removable cards (`CookRestSiteOption.cs:198`).
- **Effects**: HEAL = `MaxHp × 0.3m` → `Hook.ModifyRestSiteHealAmount` (RegalPillow) →
  `CreatureCmd.Heal` → `AfterRestSiteHeal` → `ModifyRestSiteHealRewards` (TinyMailbox,
  DreamCatcher) → `RewardsCmd.OfferCustom` (nested non-terminal rewards)
  (`HealRestSiteOption.cs:138-179`, `:310-353`). SMITH = cancelable, manually confirmed upgrade
  prompt of `SmithCount` (default 1, `SmithRestSiteOption.cs:221-228`) cards → `CardCmd.Upgrade` →
  `AfterRestSiteSmith`; cancelling returns false (`SmithRestSiteOption.cs:255-282`).
  DIG = `RelicCmd.Obtain(PullNextRelicFromFront(player))` (player bag, `Rewards` rarity float).
  LIFT = `Girya.TimesLifted++`. KINDLE = `PumpkinCandle.Rekindle()` (`KindleRestSiteOption.cs:40-44`).
  HATCH = obtain Byrdpip (`HatchRestSiteOption.cs:99`). CLONE = clone every Clone-enchanted deck
  card into the deck (`CloneRestSiteOption.cs:152-160`). COOK = cancelable, manually confirmed
  2-card removal prompt, then +5 max HP (`CookRestSiteOption.cs:210-232`; v0.107.1 gave +9 — an
  example of why [v107] values are never used without a v0.111.0 check).
- **After a successful option** (`RestSiteSynchronizer.cs:640-661`): history; all remaining
  options are cleared unless `Hook.ShouldDisableRemainingRestSiteOptions` is false
  (MiniatureTent), which only removes the chosen one. An option returning false (cancelled
  prompt) changes nothing.
- **Leave**: allowed after a successful option or when no option remains (node-layer rule
  `Nodes.Rooms/NRestSiteRoom.cs:188-190`, `:1056-1062`).

### 3.10 D6 — Events (framework; 68 event defs are W3 content)

- **Pull** (`Models/ActModel.cs:380-386`): `RoomSet.EnsureNextEventIsValid` advances
  `eventsVisited` past events whose `IsAllowed(runState)` is false (36 events override it) or that
  were already visited (`Rooms/RoomSet.cs:114-129`); `Hook.ModifyNextEvent` (LanternKey card);
  `AddVisitedEvent`. `MarkRoomVisited(Event)` then increments `eventsVisited` once more on entry.
  Ancient rooms are `EventRoom`s too, so entering the act's ancient also advances
  `eventsVisited` (the local act-2 save at the Orobas point has `events_visited = 1`): the first
  shuffled event of every act is skipped unless the index wraps. Checked against the same save's
  act 1: list `[BrainLeech, AromaOfChaos, LuminousChoir, MorphicGrove, WarHistorianRepy,
  TabletOfTruth, …]`, Neow then three events AromaOfChaos, MorphicGrove, TabletOfTruth (LuminousChoir
  and WarHistorianRepy skipped by `IsAllowed`), final `events_visited = 6` — exactly this model.
- **Begin** (`Rooms/EventRoom.cs:161-183`, `Multiplayer.Game/EventSynchronizer.cs:177-225`,
  `Models/EventModel.cs:68-144`): mutable copy, `Owner`, **private `Rng`** seeded
  `Seed + (IsShared ? 0 : slot) + XXH64(event id)` (`:91`), `BeforeEventStarted`,
  `CalculateVars`, `SetInitialEventState` → `GenerateInitialOptions`; then
  `Hook.AfterRoomEntered`; `AfterEventStarted`.
- **Options**: `CurrentOptions` in list order; legal = `!IsLocked` (`IsLocked = OnChosen == null`,
  `Events/EventOption.cs:279`, `:292`) and not (`DisableOnChosen && WasChosen`) (`:66`).
  Choosing runs `OnChosen` (`Multiplayer.Game/EventSynchronizer.cs:376-393`), which may set the
  next page (`SetEventState`, `Models/EventModel.cs:790-810`), finish the event (empty options ⇒
  finished), open prompts, offer rewards (`OfferCustom`), or start a combat
  (`EnterCombatWithoutExitingEvent`, shared events only, `:817-839` →
  `EventCombatSynchronizer`: `CombatRoom { ParentEventId, ShouldResumeParentEventAfterCombat,
  ExtraRewards }` entered with `EnterRoomWithoutExitingCurrentRoom`,
  `Runs/RunManager.cs:1527-1532`; events using it: FakeMerchant, BattlewornDummy, DenseVegetation,
  PunchOff, TheLanternKey). After such a combat the rewards proceed resumes the event
  (`EventRoom.Resume`, `Rooms/EventRoom.cs:373-379`).
- **Finished** event → `LeaveEvent` (proceed to map).
- Event combats append rooms to the **same** history entry and never advance the normal-encounter
  counter (they are not the stack base). Events read many run fields and some profile fields
  (WelcomeToWongos reads `Progress.WongoPoints`, §5.6).

### 3.11 D7 — Neow and the act-start ancients ("boss relic")

There is no boss relic reward in STS2: boss rewards are gold, potion and a Rare card reward
(§3.6). The relic step between acts is the **ancient event on the next act's row-0 start point**.

- **Arrival heal** (`Models/AncientEventModel.cs:76-105`, skipped for pre-finished loads): Neow
  first sets HP to 0; heal = `(MaxHp − CurrentHp) × (0.8m at A2 WearyTraveler)`.
- **Neow options** (`Models.Events/Neow.cs:295-388`; standard path, no modifiers), all on the event's
  private Rng (seed `Seed + 0 + XXH64("NEOW")`): curse list (10, `:175-187`) minus
  `!IsAllowedAtNeow` → `NextItem`; positive list (14, `:145-161`) minus the curse's exclusions
  (CursedPearl→GoldenPearl, HeftyTablet→ArcaneScroll, LeafyPoultice→NewLeaf,
  PrecariousShears→PreciseScissors, NeowsSacrifice→PhialHolster+LostCoffer); unless the curse is
  LargeCapsule: `NextBool` LavaRock/SmallCapsule; `NextBool` NutritiousOyster/StoneHumidifier;
  `NextBool` NeowsTalisman/Pomander; drop `!IsAllowedAtNeow` (Kaleidoscope needs all character
  pools unlocked, ScrollBoxes needs bundles; `Models.Relics/Kaleidoscope.cs:116-123`); options =
  first 2 of `UnstableShuffle(positives)` + the curse. Choosing → `RelicCmd.Obtain` + `Done()`
  (`Models/AncientEventModel.cs:340-377`); all costs are the chosen relics' `AfterObtained`
  effects (W3). Options depend only on seed, character, unlock profile and player state —
  a cheap oracle (§7.5).
- **Act 2/3 ancients**: `Ancient = NextItem(unlocked act ancients + shared subset)` at run start
  (§6.2): act 2 Orobas (OrobasEpoch), Pael, Tezcatara; act 3 Nonupeipe, Tanx, Vakuu; Darv
  (DarvEpoch) through the shared subset. Option generation also uses only the event Rng
  (`Models.Events/{Orobas,Pael,Tezcatara,Nonupeipe,Tanx,Vakuu,Darv}.cs` `GenerateInitialOptions`).
  `Hook.ShouldAllowAncient` false → a single proceed option (`Models/AncientEventModel.cs:275-289`).
- **Save point C**: when an ancient finishes the room is marked pre-finished and saved
  (`Rooms/EventRoom.cs:394-418`). Reload re-runs option generation on a fresh private Rng (no
  shared-stream effect) and starts finished.

### 3.12 D8 — Deck prompts

All run-layer card choices go through `CardSelectCmd` with `CardSelectorPrefs { MinSelect,
MaxSelect, RequireManualConfirmation, Cancelable }` (`CardSelection/CardSelectorPrefs.cs:8-105`):

| Call | Candidates (order) | Users |
| --- | --- | --- |
| `FromDeckForRemoval` | `IsRemovable` (not Eternal) ∧ filter; curses first, then deck order (`Commands/CardSelectCmd.cs:3607-3612`) | shop, removal reward, Cook, events, relics |
| `FromDeckForUpgrade` | `IsUpgradable`, deck order (`:3441-3482`) | Smith, events |
| `FromDeckForTransformation` | not Quest ∧ `IsTransformable` (`:3485-3530`) | events, relics |
| `FromDeckForEnchantment` | `CanEnchant` ∧ filter (`:3533-3600`) | events, ancients |
| `FromDeckGeneric` | filter, optional sort (`:3614-3666`) | removal path |
| `FromChooseACardScreen` | given list, `canSkip` (`:3169`) | events, relics, potions |
| `FromSimpleGridForRewards`, `FromSimpleGrid` | given list (`:3236`, `:3295`) | events |
| `FromChooseABundleScreen` | bundles (`:3798`) | ScrollBoxes |

Auto-resolution (no decision): candidates ≤ `MinSelect` and not `RequireManualConfirmation` →
all candidates are taken (`:3451`, `:3495`, `:3633`); no candidates → empty result.
`RelicSelectCmd.FromChooseARelicScreen` has no caller in v0.111.0. The game already exposes the
seam the sim needs: `TestSupport/ICardSelector.cs` (`GetSelectedCards(options, min, max)`,
`GetSelectedCardReward`). Sim protocol: a prompt suspends the current effect as `PendingPrompt`;
`PromptSelect(i)` adds candidate `i` (strictly increasing indices, so each subset has exactly one
action sequence); `PromptConfirm` when `min ≤ count ≤ max`; `PromptCancel` when cancelable. The
resumable-continuation mechanism itself is a W1 kernel contract (combat prompts share it).

### 3.13 D9 — Potions out of combat

Legality is a node-layer rule (`Nodes.Potions/NPotionPopup.cs:285-345`): potion present, not
queued, owner alive; `Usage` AnyTime (5 potions) → usable out of combat; CombatOnly → combat
only; Automatic → never manual; `CanUseOrRemovePotions` false (inside three events) disables use
and discard; `PassesCustomUsabilityCheck`. Use → `UsePotionAction` (target = self out of combat,
`GameActions/UsePotionAction.cs:96-122`) → `OnUseWrapper`; discard → `DiscardPotionGameAction`
→ `PotionCmd.Discard` (`Commands/PotionCmd.cs:283`: history, `Hook.AfterPotionDiscarded`). Both
are legal in every non-combat phase (map, rewards, shop, rest, treasure, event), which is how a
full-slot potion reward is resolved.

### 3.14 D10 — Act transition, victory, defeat

- **Boss rewards proceed** (`Nodes.Screens/NRewardsScreen.cs:678-692`): with a second boss pending
  (A10 final act, first boss done) → map → second boss (`:680-684`); else
  `ActChangeSynchronizer.MoveToNextAct`: `ActFloor++`, `EnterNextAct`
  (`Multiplayer.Game/ActChangeSynchronizer.cs:77-91`) → `EnterAct(i + 1)` (§3.2; exiting the boss
  room skips unclaimed boss rewards) or, after the last act, `EventRoom(TheArchitect)`
  (`Runs/RunManager.cs:821-872`).
- **Victory**: TheArchitect is cosmetic (score damage VFX, its own Rng only); its proceed option
  calls `WinRun` → `OnEnded(true)` (`Models.Events/TheArchitect.cs:1060-1063`,
  `Runs/RunManager.cs:2999-3000`, `:4684-4779`). The sim may end the run at the Architect room
  (`AbstractRoom.IsVictoryRoom`, `Rooms/AbstractRoom.cs:28-38`). Score:
  `Runs/ScoreUtility.cs:18-35`.
- **Defeat**: `CreatureCmd.Kill` ends the run when every player is dead, in or out of combat
  (`LoseCombat` first when a combat is live, then `OnEnded(false)`, `Commands/CreatureCmd.cs:2397-2412`;
  `RunState.IsGameOver`, `Runs/RunState.cs:446-456`), so an event can end the run too. Note `Player.ReviveBeforeCombatEnd` heals a dead player to 1 when the combat ends in a
  win (`Combat/CombatManager.cs:1688-1689`, `Entities.Players/Player.cs:67-69`).

## 4. Map generation

### 4.1 Which map class

| Case | Class | Rng | Citation |
| --- | --- | --- | --- |
| default, every act | `StandardActMap(rng, act, isMultiplayer, replaceTreasureWithElites: false, hasSecondBoss: act.HasSecondBoss)` | `new Rng(RunRngSet.Seed, $"act_{actIndex + 1}_map")` = `Seed + XXH64(name)` | `Models/ActModel.cs:465-468`, `Map/StandardActMap.cs:99-102` |
| SpoilsMap card in the deck, act index 1 | `SpoilsActMap(runState)` via `Hook.ModifyGeneratedMap` | `new Rng(Seed, "spoils_map")` | `Models.Cards/SpoilsMap.cs:151-163`, `Map/SpoilsActMap.cs:59-73` |
| GoldenCompass, the act it was obtained in | `GoldenPathActMap(runState)`; obtaining it regenerates the current map immediately (`AfterObtained` → `RunManager.GenerateMap`) | none | `Models.Relics/GoldenCompass.cs:46`, `:113-119` |
| load | `SavedActMap(saved)` then `Hook.ModifyGeneratedMapLate` | none | `Runs/RunManager.cs:2109-2117`, `Map/SavedActMap.cs:40-99` |
| BigGameHunter modifier | excluded (modifiers) | — | `Models.Modifiers/BigGameHunter.cs:14-32` |

`ModifyGeneratedMapLate` implementers: SpoilsMap (records the treasure coord), FurCoat (quest
markers). `AfterMapGenerated`: SpoilsMap adds its quest marker. GoldenCompass shows that a map can
be **replaced** mid-act (never mutated): with shared immutable maps a replacement is just a new
object; `RemoveStaleVisitedMapCoords` (`Runs/RunState.cs:992-999`) then drops visited coords
the new map lacks.

### 4.2 `StandardActMap` — algorithm and draw order

Constructor (`Map/StandardActMap.cs:75-97`): `mapLength = rooms + 1` (rooms per act, §6.1; −1 in
multiplayer); grid 7 × `mapLength`; then, all on the act map rng:

1. **Counts** `act.GetMapPointTypes(rng)`: Overgrowth/Underdocks rests `NextGaussianInt(7,1,6,7)`
   then unknowns `NextGaussianInt(12,1,10,14)` (`Models.Acts/Overgrowth.cs:134-139`,
   `Map/MapPointTypeCounts.cs:57-60`); Hive rests `NextGaussianInt(6,1,6,7)`, unknowns
   `Gaussian(12,1,10,14) − 1` (`Models.Acts/Hive.cs:117-122`); Glory rests `NextInt(5,7)`,
   unknowns `Gaussian − 1` (`Models.Acts/Glory.cs:106-111`). Elites `(int)Math.Round(5f × 1.6f)`
   = 8 at A1+, else 5 (`Map/MapPointTypeCounts.cs:29`); shops 3 (`:36`). `NextGaussianInt` is
   Box–Muller with rejection, two `NextDouble` per attempt (`Random/Rng.cs:188-212`; sim port `sim/Sts2Sim.Core/Random/Rng.cs:186-206`).
2. **Fixed points**: boss (col 3, row `mapLength`), start (col 3, row 0), second boss (col 3,
   row `mapLength + 1`) (`:82-87`).
3. **Paths** `GenerateMap` (`:219-246`): 7 times: start = `GetOrCreatePoint(NextInt(0,7), 1)`;
   only for the second path, redraw while it equals an existing start; `PathGenerate`
   (`:121-131`) steps row by row to `mapLength − 1`: `GenerateNextCoord` (`:133-183`)
   `StableShuffle([-1,0,1])` (sort, then `UnstableShuffle` = 2 `NextInt`) and takes the first
   direction (target column clamped to 0..6) without an invalid crossover (`:185-217`: a diagonal
   step is invalid when the point beside it in the target column already has a child in the
   mirrored direction); connect. Then every point in row `mapLength − 1` → boss, boss → second
   boss, start → every row-1 point in column order.
4. **Types** `AssignPointTypes` (`:260-326`): fixed rows — last row RestSite, row
   `mapLength − 7` Treasure, row 1 Monster (all `CanBeModified = false`); queue = rests, shops,
   elites, unknowns (in that order); `AssignRemainingTypesToRandomPoints` (`:396-426`): up to 3
   passes while the queue is non-empty: unassigned points in `GetAllMapPoints` order (column-major,
   `Map/ActMap.cs:287-300`), `StableShuffle` (by coord, `Map/MapCoord.cs:69-75`), and for each
   point `GetNextValidPointType` (`:428-444`) rotates the queue to the first type valid there, or
   leaves it unassigned; leftovers become Monster. Validity (`:446-549`): rows < 6 forbid RestSite
   and Elite; rows ≥ `mapLength − 3` forbid RestSite; Elite/RestSite/Treasure/Shop may not be
   adjacent (parent or child) to the same type; RestSite/Monster/Unknown/Elite/Shop may not equal a
   sibling (another child of one of its parents). `EnsureRowsContainsPointType`,
   `AssignPointTypesToRandomRows`, `GetRows` (`:328-394`) are dead code.
5. **Prune and repair** (`Map/MapPathPruning.cs:14-24`), up to 3 rounds, stop when a repair
   round changes nothing:
   - `FindMatchingSegments` (`:89-109`): enumerate every start→boss path by DFS over `Children`
     (`:111-164`, HashSet order); cut segments that start at a branching point (or row 0) and end
     at a merge point (≥2 parents), ≥3 points (`:166-212`); key = endpoints + type string
     (`:214-252`) in an ordinal `SortedDictionary`; overlapping segments under one key are not
     added (`:254-273`); keys with >1 segment are duplicates.
   - `PrunePaths` (`:280-304`): per duplicate group in key order: `UnstableShuffle(group)`;
     `PruneAllButLast` (`:306-330`) removes single-parent/single-child chain points that nothing
     else needs (`PruneSegment`, `:332-362`; `RemovePoint`, `:364-398`); if nothing was pruned,
     `BreakAParentChildRelationshipInAnySegment` (`:414-454`) cuts one branch edge. Restart the
     search after each change; more than 50 iterations throws (`:73-87`).
   - `RepairPrunedPointTypes` (`:26-71`): for Shop, Elite, RestSite, Unknown (that order) refill the
     deficit from modifiable Monster points (`StableShuffle`, assign while valid).
6. **Post-processing, no rng** (`Map/MapPostProcessing.cs`): `CenterGrid` (`:9-69`; shift one
   column when the two outer columns on one side are empty), `SpreadAdjacentMapPoints`
   (`:177-249`; per row, repeatedly move a point to an allowed column — within ±1 of every parent
   and child — that strictly increases its minimum gap to row neighbours; allowed columns enumerate
   ascending), `StraightenPaths` (`:71-112`). Coordinates change here, so saves and snapshots
   carry post-processed coords.

### 4.3 `SpoilsActMap` and `GoldenPathActMap`

- `SpoilsActMap` (`Map/SpoilsActMap.cs`): same counts call; 7 paths whose allowed columns narrow
  towards one treasure point at (3, `RowCount − 7`) (`:266-277`); within 3 rows of it the
  direction list is a fixed centre-first priority list instead of a shuffle (`:148-253`); every
  other point of the treasure row is redirected into it (`:113-131`, `:313-348`); boss/start
  connections (`:350-373`); type assignment without the fixed treasure row (`:375-469`); pruning;
  **no** post-processing (`:72`).
- `GoldenPathActMap` (`Map/GoldenPathActMap.cs:39-72`): one column (col 3) of fixed types; the
  16-entry list is lost in v0.111.0 (`:41-42`, §9.2); [v107] `Map/GoldenPathActMap.cs:9-27`:
  Monster, Unknown, Monster, RestSite, Monster, RestSite, Unknown, Treasure, Unknown, Treasure,
  Unknown, Shop, Elite, RestSite, Elite, RestSite (index 2 removed in multiplayer).

### 4.4 Porting rules

- Use BCL `HashSet<MapPoint>` (reference equality) and replay the Add/Remove sequence exactly
  (§2.6). `StableShuffle` on points uses `MapPoint.CompareTo` → `MapCoord.CompareTo` = (col, row)
  (`Map/MapPoint.cs:87-94`) through the existing `RngCollectionExtensions.StableShuffle`
  (runtime sort semantics, rng spec §8).
- `ActMap.GetPoint` checks boss, second boss and start before the grid (`Map/ActMap.cs:324-343`);
  `GetAllMapPoints` excludes them (`:287-300`).
- Map generation is atomic inside one `Step`; the finished map is frozen and shared by clones.
- `FindAllPaths` is exponential in the worst case; measure and memoise only if a bench shows a
  problem (the game does the same work).

### 4.5 Verification of the map port

1. **Save oracle** (exact): `acts[i].saved_map` (`Saves.Runs/SerializableActMap.cs` `FromActMap`):
   points in `GetAllMapPoints` order, each with `coord`, `type`, `can_modify` and `children` in
   HashSet order; `boss`, `second_boss`, `start`, `start_coords` (start-point HashSet order),
   `width`, `height`. The sim writes the same projection; equality including list orders checks the
   HashSet replay. Only the current act's map is saved (`Runs/RunManager.cs:3994-4001`), so the
   recorder must copy the save at each act start (§7.4); the local save already provides one act-2
   (Hive, A8) map.
2. **Recorder snapshot** (`mod/src/StateBuilder/PassiveStateBuilder.Map.cs:17-163`): nodes = start
   + grid (`GetAllMapPoints` order) + boss (+ second boss) as `{col, row, type =
   MapPointType.ToString(), children sorted by col}`, boss ids from the act, and `next_options`.
   Compare node sets, per-node sorted child lists, types, boss ids and the D1 option set. Children
   order is lost, so this check cannot catch HashSet-order bugs; use it for coverage across the
   whole recorder corpus (game-version gated).
3. **Run history** (`.run`, schema 10): the per-floor `map_point_type` sequence must be a path in
   the sim map.

## 5. Rewards and odds

### 5.1 Card rarity odds (`Odds/CardRarityOdds.cs`)

| Odds type | Rare base | Uncommon base | Users |
| --- | --- | --- | --- |
| RegularEncounter | 0.03 (A7 0.0149) | 0.37 | monster card rewards, default |
| EliteEncounter | 0.10 (A7 0.05) | 0.40 | elite card rewards |
| BossEncounter | 1.0 (offset forced 0) | 0 | boss card rewards (always Rare) |
| Shop | 0.09 (A7 0.045) | 0.37 | merchant character cards |
| Uniform | 0.33 | 0.33 | some events/relics |

- Offset (`CurrentValue`) starts at −0.05 (`:43-46`). `Roll` (`:53-65`): `r = Rewards.NextFloat()`;
  Rare if `r < rare + offset`; Uncommon if `r < uncommon + (rare + offset)` (float32 sums); else
  Common. Then Rare → offset = −0.05; otherwise offset = `min(offset + growth, 0.4)` with growth
  0.01 (A7 0.005). `RollWithoutChangingFutureOdds` uses the offset without updating it (shop);
  `RollWithBaseOdds` ignores it.
- Choice of method (`Factories/CardFactory.cs:200-217`): `Roll` iff `ForceRarityOddsChange` or
  (source Encounter and type Regular/Elite/Boss); otherwise `RollWithBaseOdds`.
- **Static-init trap** (P0 review item): `regularCommonOdds` is a static field initialised once
  from whatever ascension is live at first type access (`:13`). No roll reads a Common value
  (`:74-76`, `:91-92` read only Rare and Uncommon), so the trap is rules-inert in v0.111.0; every
  value a roll reads is a per-call property (`:31-41`). Port all of them as functions of the run's
  ascension, with no static state; re-check this on each game update.

### 5.2 Card reward generation (`Factories/CardFactory.cs:71-91`, `:168-198`)

Per card: `Hook.ModifyCardRewardCreationOptions` (PrismaticGem, DingyRug; modifiers excluded) →
candidates = unlocked cards of the option pools filtered by `CardPoolFilter`
(`Runs/CardCreationOptions.cs:119-122`, `Models/CardPoolModel.cs:68-86`) `Except` the cards already
offered in this reward (dedupe within one reward only) → player-count filter (`:25-32`) → Uniform:
any non-Basic, non-Ancient card; otherwise roll (§5.1) and `GetNextAllowedRarity` (`:219-237`:
Common→Uncommon→Rare→Common… until present; back at the start → `None` → exception) →
`Rewards.NextItem` → `CreateCard` → `RollForUpgrade` unless `NoUpgradeRoll` (`:239-261`): always
one `NextFloat`; for an upgradable non-Rare card the chance is `actIndex × 0.25` (A7 0.125),
`Hook.ModifyCardRewardUpgradeOdds` (no implementer), upgrade iff `roll <= chance` (a roll of exactly
0 upgrades even in act 1). After all cards: `Hook.TryModifyCardRewardOptions` (LastingCandy; Late:
Glitter, LavaLamp, SilverCrucible, Frozen/Molten/Toxic egg, SilkenTress) unless `NoModifyHooks`.

- Base rewards use only the character pool (`CardCreationOptions.ForRoom`,
  `Runs/CardCreationOptions.cs:78-107`): colorless and curse cards enter rewards only through
  hooks and events. Shops add two colorless cards (§3.8).
- `CardCreationOptions` is a mutable record: `WithFlags`/`WithRarityOdds`/`WithCardPools` mutate and
  return `this` (`:124-154`), so a reward's `Options` and `RerollOptions` alias one object
  (`Rewards/CardReward.cs:532-533`) — keep the aliasing.
- Flags (`Runs/CardCreationFlags.cs:6-19`) are read by the factory (`NoUpgradeRoll`,
  `NoModifyHooks`, `ForceRarityOddsChange`) and by hook implementers (`IsCardReward`,
  `IsFromCombat`, `NoCardPoolModifications`, …).

### 5.3 Potion reward odds and potion generation

- `PotionRewardOdds` (`Odds/PotionRewardOdds.cs:16-44`): starts 0.4. `Roll`:
  `Hook.ShouldForcePotionReward` (WhiteBeastStatue) → true with no draw and no change; else
  `r = Rewards.NextFloat()`, threshold `current + (Elite ? 0.25 × 0.5 : 0)`; hit → current −= 0.1,
  miss → += 0.1, no clamp.
- `PotionFactory.CreateRandomPotions` (`Factories/PotionFactory.cs:47-61`): per potion
  `NextFloat` ≤ 0.1 Rare, ≤ 0.35 Uncommon, else Common, then `NextItem` among options of that
  rarity (character pool + shared pool, unlocked, `:63-66`), removing the chosen one.

### 5.4 Relic rarity and the two grab bags (`Runs/RelicGrabBag.cs`)

- `RollRarity` (`Factories/RelicFactory.cs:54-63`): `NextFloat` < 0.5 Common, < 0.83 Uncommon,
  else Rare. Shop rarity is only requested explicitly.
- **Player bag** (`:61-108`): shared + character pool unlocked relics, rarities {Common, Uncommon,
  Rare, Shop} only, one deque per rarity in first-appearance order, each `UnstableShuffle`d on
  `UpFront`; `refreshAllowed: false`.
- **Shared bag** (`:110-154`): shared pool only, **no rarity filter** (the save lists common,
  uncommon, rare, shop, event, ancient), `refreshAllowed: true` for a new run — but `FromSerializable`
  builds it with `refreshAllowed: false` and without `_originalRelics` (`:358-413`), so a loaded
  run never refreshes. The importer must reproduce that (game quirk, §9.7).
- **Pull** (`:156-197`, `:262-287`): drop relics whose `IsAllowed(runState)` is false (20 overrides,
  16 of them `IsBeforeAct3TreasureChest`, `Models/RelicModel.cs:539-543`); empty deque and refresh
  allowed → refill from the original list; while no relic passes the filter fall back
  Shop → Common → Uncommon → Rare → none; multiplayer fallback list; front/back = first/last passing
  element.
- **Users**: player bag front — elite `RelicReward` (`Rewards/RelicReward.cs:142-163`), DIG, events
  and relics via `RelicFactory.PullNextRelicFromFront`; player bag back — shop; shared bag front —
  treasure only. `RelicFactory.PullNext*` also removes the relic from the shared bag
  (`Factories/RelicFactory.cs:30-47`); `RelicCmd.Obtain` removes a non-stackable relic from both
  bags (`Commands/RelicCmd.cs:411-415`). Fallback relic: Circlet (`Factories/RelicFactory.cs:13`).

### 5.5 Gold

Encounter range (`Models/EncounterModel.cs:45-81`): Monster 10–20, Elite 35–45, Boss 100–100,
×0.75 at A3, then `(int)`. Monster rewards scale both bounds by `GoldProportion = 1 −
escaped/spawned` (`:324-327`, set at combat end `Rooms/CombatRoom.cs:829-832`) with `Math.Round`
(`Rewards/RewardsSet.cs:494-497`) and drop the gold reward at proportion 0. Treasure 42–52, ×0.75
at A3 (§3.7). Event encounters (`Models/ModelDb.cs:170-177`) and `GremlinMercNormal` override reward
properties.

### 5.6 Unlock-state dependence and the sim assumption

| Rule | Depends on | Citation |
| --- | --- | --- |
| character card pools | `<Char>2/5/7Epoch` remove cards (same pattern in all five pools) | `Models.CardPools/IroncladCardPool.cs:122-138` |
| character relic pools | `<Char>3/6Epoch` | `Models.RelicPools/IroncladRelicPool.cs:36-43` |
| character potion pools | `<Char>4Epoch` | `Models.PotionPools/IroncladPotionPool.cs:23` |
| colorless pool | Colorless1–5 epochs | `Models.CardPools/ColorlessCardPool.cs:100-119` |
| shared relic pool | Relic1–5 epochs | `Models.RelicPools/SharedRelicPool.cs:142-161` |
| shared potion pool | Potion1–2 epochs | `Models.PotionPools/SharedPotionPool.cs:73-92` |
| event pool | Event1 (TrashHeap), Event2 (Reflections), Event3 (ColorfulPhilosophers) | `Models/ActModel.cs:245-256` |
| act 1 choice | Underdocks needs UnderdocksEpoch | `Models.Acts/Underdocks.cs:107-110` |
| ancients | Neow (NeowEpoch; also `StartedWithNeow`), Orobas (OrobasEpoch), Darv (DarvEpoch) | `Models.Acts/Overgrowth.cs:100-108`, `Models.Acts/Hive.cs:98-106`, `Unlocks/UnlockState.cs:56-67` |
| characters | Silent1, Regent1, Necrobinder1, Defect1 epochs (Orobas picks another unlocked character; Kaleidoscope at Neow needs all pools) | `Unlocks/UnlockState.cs:31-54` |
| boss identity | encounters seen: Standard mode replaces the boss with the first unseen one in `BossDiscoveryOrder` (rng already consumed) | `Models/ActModel.cs:338-358`, `Runs/RunManager.cs:4078-4081` |
| first run | `NumberOfRuns == 0`, `EpochUnlockCount() == 0` | §9.6 |
| profile, not in the run save | `Progress.DiscoveredActs` (forced act), `Progress.NumberOfRuns` (floor-1 event), `Progress.WongoPoints` (WelcomeToWongos badge relic) | `Models/ActModel.cs:487`, `Runs/RunManager.cs:4241`, `Models.Events/WelcomeToWongos.cs:634-646` |

Sim input: `UnlockProfile { Epochs, EncountersSeen, NumberOfRuns, DiscoveredActs,
ProgressNumberOfRuns, WongoPoints }` with two presets — `AllUnlocked` (`UnlockState.all`: every
epoch, every encounter seen, 999999999 runs, `Unlocks/UnlockState.cs:18`; all acts discovered;
WongoPoints 0) as the training default, and `FromSave` (exact `players[0].unlock_state`, plus
recorder-copied profile fields) for differential tests. The local profile has all 57 epochs and
162 runs but only 85 encounters seen, so the boss override can fire there: differential tests must
not assume `AllUnlocked`.

## 6. Act structure

### 6.1 Acts (`Models.Acts/*.cs`)

| Act | Index | Default | Unlock | Rooms (SP) | Weak | Weak/normal/elite/boss encounters | Own events | Ancients | Rest / unknown counts | Boss discovery order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Overgrowth | 0 | yes | always | 15 | 3 | 4 / 12 / 3 / 3 | 13 | Neow | G(7,1,6,7) / G(12,1,10,14) | Vantom, CeremonialBeast, TheKin |
| Underdocks | 0 | no | UnderdocksEpoch | 15 | 3 | 4 / 10 / 3 / 3 | 10 | Neow | same | WaterfallGiant, SoulFysh, LagavulinMatriarch |
| Hive | 1 | yes | always | 14 | 2 | 4 / 10 / 3 / 3 | 10 | Orobas*, Pael, Tezcatara | G(6,1,6,7) / G(12,1,10,14) − 1 | TheInsatiable, KnowledgeDemon, KaiserCrab |
| Glory | 2 | yes | always | 13 | 2 | 3 / 9 / 3 / 3 | 7 | Nonupeipe, Tanx, Vakuu | `NextInt(5,7)` / G − 1 | Queen, TestSubject, Aeonglass |

Plus 18 shared events (`Models/ModelDb.cs:138-158`) in every act and Darv (shared ancient,
`:162`). Multiplayer: rooms − 1 (`Models/ActModel.cs:185-193`). Floors per act = rooms + 2
(`:195-198`). `*` epoch-gated.

### 6.2 Room generation (`Models/ActModel.cs:241-323`, all on `UpFront`)

Before the per-act loop: `SharedAncients` (Darv if unlocked) `UnstableShuffle`d; each act after the
first takes `NextInt(count + 1)` ancients from the front (`Runs/RunManager.cs:4057-4068`). Per act:

1. Events = act events ++ shared events minus epoch-locked ones, `UnstableShuffle`d.
2. Normal list: `NumberOfWeakEncounters` draws from the weak grab bag, then draws from the regular
   bag until the list has `GetNumberOfRooms` entries; bags refill when empty. Every draw is
   `AddWithoutRepeatingTags` (`:362-373`): `GrabAndRemove` with "shares no `EncounterTag` with the
   previous entry and is not the previous entry", by rejection sampling — one `NextDouble` per
   attempt (`Helpers/GrabBag.cs:52-82`) — and an unconstrained grab when no element qualifies.
3. Elites: 15 draws, same rule, refilling bag.
4. `Boss = NextItem(AllBossEncounters)`; `Ancient = NextItem(unlocked ancients ++ shared subset)`.

Then (`Runs/RunManager.cs:4074-4087`): Standard mode `ApplyDiscoveryOrderModifications` (boss
override; Overgrowth's first-run fixed orders, `Models.Acts/Overgrowth.cs:110-127`); for the last
act at A10 `SecondBoss = NextItem(bosses ≠ Boss)`. Pulls: §3.3 step 6, §3.10. The weak→regular
switch is positional: the first `NumberOfWeakEncounters` monster rooms of an act (map Monster
points and Unknown→Monster alike) take weak encounters. `NextBossEncounter` returns `SecondBoss`
once a boss was visited (`Rooms/RoomSet.cs:80-90`).

### 6.3 Floor numbering

`ActFloor = row + 1` (ancient 1, rooms 2..rooms+1, boss rooms+2, second boss rooms+3);
`TotalFloor` = number of history entries (`Runs/RunState.cs:423`). Single player: act 1 =
floors 1–17 (Neow 1, boss 17), act 2 = 18–33, act 3 = 34–48 (second boss 49). Treasure rows
(`RowCount − 7`) fall on total floors 10, 26 and 41; `IsBeforeAct3TreasureChest` uses 41
(38 in multiplayer, `Models/RelicModel.cs:539-543`). The first combat is at total floor 2 (the
existing `CombatRunContext.Default`). `ActChangeSynchronizer` bumps `ActFloor` before the act
change (`Multiplayer.Game/ActChangeSynchronizer.cs:85`), which `SetActInternal` then resets.

### 6.4 Ascension run effects (A0–A10)

| A | `AscensionLevel` | Effect | Citation |
| --- | --- | --- | --- |
| 1 | SwarmingElites | 8 elites per map instead of 5 | `Map/MapPointTypeCounts.cs:29` |
| 2 | WearyTraveler | ancient arrival heal ×0.8 (Neow: 80 % of max HP) | `Models/AncientEventModel.cs:84-87` |
| 3 | Poverty | encounter and treasure gold ×0.75 | `Models/EncounterModel.cs:56-58`, `:75-77`; `Multiplayer.Game/OneOffSynchronizer.cs:659-662`; `Helpers/AscensionHelper.cs:12` |
| 4 | TightBelt | −1 potion slot at run start | `Entities.Ascension/AscensionManager.cs:29-32` |
| 5 | AscendersBane | curse in the starting deck | `Entities.Ascension/AscensionManager.cs:33-38` |
| 6 | Inflation | card removal 100 (+50 per use) instead of 75 (+25) | `Entities.Merchant/MerchantCardRemovalEntry.cs:319-321` |
| 7 | Scarcity | rare base odds halved, offset growth 0.005, reward upgrade scaling 0.125 | `Odds/CardRarityOdds.cs:13-41`, `Factories/CardFactory.cs:23` |
| 8 | ToughEnemies | monster HP, per monster file (101 files) | `Models.Monsters/*.cs` |
| 9 | DeadlyEnemies | monster damage, per monster file (96 files) | `Models.Monsters/*.cs` |
| 10 | DoubleBoss | second boss after the final act's boss | `Runs/RunManager.cs:4082-4086`, `Map/StandardActMap.cs:84-87`, `Rooms/RoomSet.cs:80-90` |

Levels are cumulative (`HasLevel: level >= n`, `Entities.Ascension/AscensionManager.cs:22-25`);
the maximum is 10 (`:8`; enum `Entities.Ascension/AscensionLevel.cs:3-16`). There is no A11–A20 in
v0.111.0 (`sts2rec/sts2rec/game_rules.py:6-7` already uses 0–10; the P1 plan's "A1–A20" is a
typo). The game reads ascension through the global `RunManager.Instance`
(`Helpers/AscensionHelper.cs:14-44`); the sim passes it explicitly, as P0 does.

## 7. Save import

### 7.1 Save points and resume phases

`SaveManager.SaveRun` has three production callers; no save exists inside a room (shop, rest
site, event page, reward screen, combat turn).

| Point | Caller | State captured | Resume (`RunManager.LoadIntoLatestMapCoord`, `Runs/RunManager.cs:2360-2381`) | Sim entry phase |
| --- | --- | --- | --- | --- |
| A | `Runs/RunManager.cs:618` | a map point was just entered: previous rooms exited, `ActFloor` set, room type **not** rolled, history entry **not** appended (`pre_finished_room = null`) | re-enter the last visited coord with `saveGame: false`: roll, create, append, enter — identical because every stream is restored | `EnteringMapPoint` |
| B | `Combat/CombatManager.cs:1748` | a combat was won: `AfterCombatEnd`, `AfterCombatVictory`, `MarkPreFinished` done; rewards **not** generated; `pre_finished_room` = the combat room (`encounter_id`, `reward_proportion`, `extra_rewards`, `parent_event_id`, `should_resume_parent_event`, `encounter_state`) | no room roll for Unknown (`:674-677`), no history append (`:685-688`); a parent event is re-entered first as stack base (`:690-701`); `StartPreFinishedCombat` (`Rooms/CombatRoom.cs:501-578`) regenerates monsters on the encounter-private Rng only and calls `OfferRoomEndRewards` → the same rewards | `PostCombatRewards` |
| C | `Rooms/EventRoom.cs:417` | an ancient event finished (`is_pre_finished`) | the event restarts finished; options are regenerated on a fresh private Rng (`Models/AncientEventModel.cs:291-302`) | `AncientFinished` (map next) |

No visited coord (run start before Neow, `StartedWithNeow = false`) → `MapRoom`. The debug
character-select path (`Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:563`) is not a
production save.

### 7.2 Game load sequence

`RunState.FromSerializable` (`Runs/RunState.cs:584-595`; players `Entities.Players/Player.cs:719-727`;
`RunRngSet.FromSave`; acts `Models/ActModel.cs:456-461`; odds `Odds/RunOddsSet.cs:38-50`; shared bag
`Runs/RelicGrabBag.cs:358-363`) → `SetUpSavedSingleplayer` (`Runs/RunManager.cs:2885-2955`:
`InitializeShared`, `InitializeSavedRun` `:3871-3914` — `ValidateRoomsAfterLoad(UpFront)` re-rolls
only a deprecated boss, `Models/ActModel.cs:325-336`; saved maps registered by act index) →
`Launch` → `GenerateMap` (uses the saved map) → `LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(pre_finished_room))`
(`Nodes/NGame.cs:1505-1597`).

### 7.3 Fields the importer needs (JSON, schema 20)

Model ids are `CATEGORY.ENTRY` (`CARD.STRIKE_SILENT`, `RELIC.RING_OF_THE_SNAKE`, `EVENT.OROBAS`,
`ENCOUNTER.KNOWLEDGE_DEMON_BOSS`, `ACT.HIVE`, `POTION.COLORLESS_POTION`); strip the category for the
sim id (as `sts2rec` `diff_bundle.strip_prefix` does). Enums are snake_case
(`Saves/SnakeCaseJsonStringEnumConverter.cs`); default-valued fields are omitted.

| JSON path | Sim target | Needed for |
| --- | --- | --- |
| `schema_version` | gate | accept 20 only; older saves go through the game's migrations (`Saves.Migrations.SerializableRuns/*`) and are out of scope |
| `rng.seed`, `rng.rngs.<stream>.{counter,s0..s3}` | `RunRngSet.FromSnapshot` (exists) | every run stream |
| `ascension`, `game_mode`, `modifiers` | `RunState` | reject non-standard or modded runs |
| `current_act_index`, `visited_map_coords`, `events_seen` | `RunState` | travel, event pulls |
| `odds.unknown_map_point_{monster,elite,treasure,shop}_odds_value` | `UnknownMapPointOdds._nonEventOdds` | parse as **float32** (`Utf8JsonReader.GetSingle`) |
| `shared_relic_grab_bag.relic_id_lists.<rarity>` | shared bag, `RefreshAllowed = false`, no original list | treasure relics |
| `acts[i].id`, `acts[i].rooms.*` (event/normal/elite id lists + visited counters, boss, second boss, ancient) | `ActInstance.RoomSet` (drop deprecated ids, `Rooms/RoomSet.cs:148-162`) | all pulls |
| `acts[current].saved_map` | `SavedActMap` (wiring order `Map/SavedActMap.cs:110-153`) | travel; other acts regenerate on entry |
| `map_point_history` (types, rooms; stats optional) | history | `TotalFloor`, shop blacklist, score |
| `extra_fields.{started_with_neow,test_subject_kills,freed_repy}` | `RunState.ExtraFields` | act-1 start, TestSubject, Repy |
| `pre_finished_room.*` | phase B/C reconstruction (`Rooms/CombatRoom.cs:674-718`, `Rooms/EventRoom.cs:336-343`) | resume |
| `players[0].{character_id,current_hp,max_hp,max_energy,max_potion_slot_count,base_orb_slot_count,gold,net_id}` | `RunPlayer` | — |
| `players[0].deck[]` `{id, current_upgrade_level, enchantment{id,amount,props}, props, floor_added_to_deck}` | deck `CardInstance`s | combat, prompts |
| `players[0].relics[]` `{id, props, floor_added_to_deck}` | `RelicInstance`s; `props` = `SavedProperties` (`Saves.Runs/SavedProperties.cs`) filled per relic def | relic counters |
| `players[0].potions[]` `{id, slot_index}` | `PotionSlots` | — |
| `players[0].rng.{seed,rngs.{rewards,shops,transformations}}` | `PlayerRngSet` (exists) | rewards, shops |
| `players[0].odds.{card_rarity_odds_value,potion_reward_odds_value}` | `PlayerOdds` (float32) | card/potion rolls |
| `players[0].relic_grab_bag.relic_id_lists` | player bag | elite/shop relics |
| `players[0].extra_fields.{card_shop_removals_used,wongo_points}` | `RunPlayer.ExtraFields` | removal price, Wongo |
| `players[0].unlock_state.{unlocked_epochs,encounters_seen,number_of_runs}` | `UnlockProfile` | pools (§5.6) |
| ignored | `save_time`, `start_time`, `run_time`, `win_time`, `num_reloads`, `platform_type`, `map_drawings`, `daily_time`, `discovered_*` | — |

**Importer design.** C#: `Sts2Sim.Core/Run/Save/` — DTOs for this subset, a reader on
`System.Text.Json` (part of the .NET 9 shared framework, no NuGet), `RunImporter.FromSave(save,
options) → RunSim` that builds the state exactly as `RunState.FromSerializable` does, reconstructs
the entry phase of §7.1, and returns `Unsupported` reasons (unported defs, non-standard mode,
schema) instead of throwing — the run-layer analogue of the P0 bundle `ineligible_reasons`. Profile
fields that are not in the save (`DiscoveredActs`, progress `NumberOfRuns`, `WongoPoints`) come from
`ImportOptions` (default: `AllUnlocked` semantics). Relic/card/enchantment `props` need a per-def
property schema: every W3 relic/card port declares its `[SavedProperty]` fields so the importer can
fill them. Python: `sts2rec/sts2rec/run_save.py` (parser + validation) for the exporter.

### 7.4 Recorder and sts2rec changes this needs (owned outside the sim)

- Copy `current_run.save` (copy only, never modify) into `<session>/native/` after each of the three
  `SaveRun` calls, with a sequence number and the matching state seq (Harmony postfix on
  `SaveManager.SaveRun`). Today the recorder archives only `run_history.run` and `replay.mcr`
  (`mod/src/RunLifecycle.cs:368`, `:394`). This is the P0 plan's "candidate B", generalised.
- Snapshot the profile fields the save lacks (`DiscoveredActs`, `NumberOfRuns`, `WongoPoints`) at
  run start.
- `sts2rec/sts2rec/native_run.py` accepts `.run` schema 8/9 only (`:29-31`); v0.111.0 writes
  schema 10.
- Card-select snapshots lack min/max/cancelable (`mod/src/StateBuilder/PassiveStateBuilder.Selections.cs:101-207`).

### 7.5 Oracles this enables

| Oracle | Data | Checks |
| --- | --- | --- |
| run start | save A at the Neow point | `up_front.counter`, both grab bags (ordered), three room sets, act-1 map |
| Neow | 168 local v0.111.0 `.run` files (`ancient_choice` + `was_chosen`) + seed + character | the three options (needs the run's unlock profile: Kaleidoscope gating) |
| maps | `saved_map` per act start; recorder map snapshots | §4.5 |
| floor sequence | `.run` `map_point_history[].rooms` (`model_id`, `room_type`, `monster_ids`) | encounter/event pulls and unknown rolls along the human path |
| offers | `.run` `card_choices`/`relic_choices`/`potion_choices` (unsold shop stock is logged at exit) + recorder reward/shop snapshots | reward and shop generation with the human's decisions replayed |
| full run | recorder actions + per-boundary save copies | per-decision run state (W4 exporter v2) |

W0 spot checks against the local v0.111.0 save (read-only; A8 Silent, act 2 Orobas point):
Hive normal list = 2 weak + 12 regular with the regular bag refilled after 10 (14 =
`GetNumberOfRooms`, no entry equal to its predecessor); 15 elites without consecutive repeats;
28 events = 10 Hive + 18 shared; the shared bag holds common/uncommon/rare/shop/event/ancient lists;
act-2 map 7 × 15; act-1 event index trace as in §3.10. All agree with §5.4, §6.2 and §3.10.

## 8. Ownership split and combat composition

### 8.1 Prerequisites from the W1 kernel wave

| Id | Contract | Why the run layer needs it |
| --- | --- | --- |
| K1 | resumable `PendingPrompt` API (deck/hand/grid/choose-a-card/bundle), shared with combat | every prompt in §3.12 is a decision |
| K2 | the 46 run-scope and 12 missing both-scope dispatchers + `AbstractModel` virtuals; run iterator in §2.7 order | rewards, shop, rest, map, unknown rolls, card adds |
| K3 | `RelicInstance`/`PotionInstance` runtime (hook listeners, `[SavedProperty]` state, stack count, melt) owned by `RunPlayer`, visible to combat listeners | relic counters persist across combats |
| K4 | run-scope Cmd facades: `PlayerCmd` gold, `CreatureCmd` heal/max HP outside combat, `CardPileCmd.Add(Deck)`/`RemoveFromDeck`, `CardCmd.Upgrade/Transform/Enchant`, `RelicCmd.Obtain/Remove/Replace/Melt`, `PotionCmd.TryToProcure/Discard`, `RewardsCmd.OfferCustom` | effects called from rooms and content |
| K5 | deck `CardInstance` (upgrade level, enchantment, props, `FloorAddedToDeck`) and the `DeckVersion` link from combat copies (`Entities.Players/Player.cs:1214-1233`) | deck edits during combat, save import |
| K6 | `CombatState` attachment to `RunPlayer` and to the run's **live** `RunRngSet` (P0 `CombatSim.Create(RunRngSet)` deep-clones by contract); the existing public `CombatLoop.SetUpCombat`/`StartCombat` split (loop spec §2.3) must accept that live set so `AfterRoomEntered` runs between them | streams continue across combats |

### 8.2 W2 agents (disjoint files)

| Agent | Owns (new) | Delivers | Needs | Est. LOC (code + tests) |
| --- | --- | --- | --- | --- |
| R1 run-state | `Sts2Sim.Core/Run/State/` (`RunState`, `RunPlayer`, `ActInstance`, `RoomSet`, `MapPointHistory`, `ExtraFields`, `UnlockProfile`, `RunConfig`, `RunCanonicalTags`), `Tests/RunStateTests.cs`, `sim/specs/run-state.md` | §2 fields, `DeepClone`, `WriteCanonical`, invariants | K3, K5 | 1,500 |
| R2 map | `Run/Map/` (`MapCoord`, `MapPoint`, `ActMap`, `StandardActMap`, `SpoilsActMap`, `GoldenPathActMap`, `SavedActMap`, `MapPathPruning`, `MapPostProcessing`, `MapPointTypeCounts`, `MapTravel`, `MapProjection`), `Tests/MapTests.cs`, `sim/specs/run-map.md` | §4, save/recorder projections | rng only | 1,800 |
| R3 odds & rewards | `Run/Odds/`, `Run/Rewards/`, `Run/Factories/` (`CardFactory` run part, `PotionFactory`, `RelicFactory`), `Run/Relics/RelicGrabBag.cs`, `Run/Util/GrabBag.cs`, tests, `sim/specs/run-rewards.md` | §5, §3.6 generation | K2, K3, K5 | 2,200 |
| R4 rooms | `Run/Rooms/` (room stack, combat-room glue, merchant inventory + entries, rest-site options, treasure + relic picking), tests, `sim/specs/run-rooms.md` | §3.7–3.9, §8.3 | R3, K1–K6 | 2,300 |
| R5 events | `Run/Events/` (`EventDef`, `EventInstance`, `EventOption`, pages, event combat + resume, `AncientEventDef`, Neow, TheArchitect, option generation of the 7 act-start ancients), tests, `sim/specs/run-events.md` | §3.10–3.11; the other 60 events are W3 content | K1, K3, R3 | 2,000 |
| R6 run loop | `Run/Loop/` (`RunSim`, `RunLoop`, `RunLegalActionGenerator`, `RunActionExecutor`), tests, `sim/specs/run-loop.md`; `Api/RunAction.cs` is scaffold-owned and frozen before R1–R7 start | §3.1–3.5, §3.12–3.14 | R1–R5 | 2,200 |
| R7 save import | `Run/Save/` (DTOs, reader, importer), tests on a scrubbed fixture, `sim/specs/run-save-import.md`; `sts2rec/sts2rec/run_save.py` + tests | §7 | R1, R2 | 1,400 |
| R8 run oracles | `Sts2Sim.Diff/Run/` (run-start, Neow, map, floor-sequence checks), `sts2rec` `export-run-oracles` | §7.5 gates | R6, R7 | 1,200 |

Order: freeze `RunAction`, the `RunState` shape and K1–K6 → R1 and R2 in parallel (R2 needs only
rng) → R3 and R5 → R4 → R6 integrates → R7 (starts after R1) → R8 → adversarial verification per
area → fixes → gates. W3 content (relics, potions, events, cards) plugs into the R3–R5 registries
one file per model, as in P0.

### 8.3 How `RunSim` composes `CombatSim`

Entering a combat room (`Rooms/CombatRoom.cs:83-114`, `:355-435`):

1. The encounter was pulled at room creation; the history entry is appended (so `TotalFloor`
   counts this floor); `Hook.BeforeRoomEntered` (run listeners).
2. Build `CombatState` on the run's live `RunRngSet` with the persistent HP/max HP, deck order,
   relics and potions (K6); `GenerateMonstersWithSlots` on the encounter-private Rng
   (`Seed + TotalFloor + XXH64(id)`); spawn monsters (Niche HP) — window A.
3. `CombatLoop.SetUpCombat` (deck cloned into the draw pile with `DeckVersion` links, opening
   shuffle, monster setup) — window B.
4. `Hook.AfterRoomEntered(runState, room)` (`Rooms/CombatRoom.cs:420`; 37 implementers, e.g.
   BronzeScales, Planisphere) — still window B.
5. `CombatLoop.StartCombat` (`AfterCombatRoomLoaded`, `:435`): `AfterCreatureAdded` loop,
   `BeforeCombatStart`, turn 1.

During combat relics and potions are combat listeners, potion use empties run slots, gold changes
(thieves, `HeistPower`) hit `RunPlayer.Gold`, deck edits go through `DeckVersion`.

Victory (`Combat/CombatManager.cs:1648-1810`): `IsInProgress = false`; `ReviveBeforeCombatEnd`
(dead player healed to 1); `Hook.AfterCombatEnd(run, combat, room)`; combat history cleared;
`room.OnCombatEnded()` (gold proportion); `Player.AfterCombatEnd` (powers removed, combat piles
cleared, block lost, `Entities.Players/Player.cs:1249-1254`); `Hook.AfterCombatVictory` (Early + normal); turns taken; `WinTime` for the
final boss; `MarkPreFinished`; **save point B**; then `OfferRoomEndRewards` (triggered by
`NCombatUi.ShowRewards`, `Nodes.Combat/NCombatUi.cs:343`) → `Rewards` phase. HP/max HP flow back
to `RunPlayer` (copy-out, or shared storage — K6 decides). Defeat: the run ends. Rewards depend on
the room type and `Encounter.ShouldGiveRewards` (`Commands/RewardsCmd.cs:248-258`). Event combats
keep the event as stack base (no `MarkRoomVisited`), add `ExtraRewards`, and their proceed resumes
the event.

### 8.4 W2 gates

- Self-consistency: `Hash(x) == Hash(x.Clone())`, clone independence, legal-action closure, 10⁵
  random full runs without exceptions. Until W3 lands, a `ContentFilter` in `RunConfig` restricts
  the run to ported content (e.g. ported encounters only, events replaced by a stub).
- Oracles: run-start oracle on real saves, Neow options on the 168 local `.run` files, map
  projections on saved maps and recorder snapshots, floor sequences on `.run` histories.

## 9. Risks and open questions

### 9.1 Multiplayer-only paths to stub

Mend rest option (`Entities.RestSite/RestSiteOption.cs:62-65`); relic-picking fights and bot votes
(§3.7); shared-event voting and its option rng (`Multiplayer.Game/EventSynchronizer.cs:244-285`);
`RelicGrabBag` multiplayer fallback list (`Runs/RelicGrabBag.cs:20`, `:225-260`); player-choice,
checksum, map-vote and rewards-set network synchronisers; `MultiplayerScalingModel` (×1 in single
player, `Models.Singleton/MultiplayerScalingModel.cs:41-57`); room counts −1 and the act-3
treasure threshold 38 (`Models/ActModel.cs:185-193`, `Models/RelicModel.cs:541`). Kept: the
single-player card filter (`Factories/CardFactory.cs:25-32` removes `MultiplayerOnly` cards).

### 9.2 Decompile losses (v0.111.0 static array blobs)

`RuntimeHelpers.InitializeArray` with a lost field token, so the initial values are missing:
`Map/GoldenPathActMap.cs:42`, `Entities.Merchant/MerchantInventory.cs:199`,
`Models.Cards/AscendersBane.cs:21`, `Models.Cards/Folly.cs:19`, `Models.Monsters/KnowledgeDemon.cs:773`,
`Models.Events/ColossalFlower.cs:491`, `:494`, `Models.Events/TinkerTime.cs:189`, `:196`, `:203`,
`Entities.Cards/CardKeywordOrder.cs:15`, `Models.Relics/GlassEye.cs:58` (plus UI-only
`Helpers/HandPosHelper.cs`, `AutoSlay`). `-PrivateImplementationDetails-.cs` is empty. Use the
[v107] values provisionally and re-read the blobs from v0.111.0 IL (an `ildasm`/ILSpy `-il` dump of
`<PrivateImplementationDetails>`).

### 9.3 Node-layer rules that must be ported as rules

`Nodes.Screens.Map/NMapScreen.cs:1388-1425` (travelable points); `Nodes.Screens/NRewardsScreen.cs:668-718`
(proceed, act change, skip); `Nodes.Rooms/NTreasureRoom.cs:172-330`, `:551-565`, `:640-646`
(chest open order, skip, proceed gating);
`Nodes.Screens.TreasureRoomRelic/NTreasureRoomRelicCollection.cs:505` (treasure relic obtain);
`Nodes.Rooms/NRestSiteRoom.cs:188-190`, `:1056-1062` (leave condition);
`Nodes.Potions/NPotionPopup.cs:285-345` (potion usability); `Nodes.Combat/NCombatUi.cs:343`
(rewards after victory); merchant removal gating and `SetUsed`
(`Nodes.Screens.Shops/NMerchantCardRemoval.cs:153-156`, `:210-214`, called from
`Entities.Merchant/MerchantCardRemovalEntry.cs:380`); `Nodes/NGame.cs:1988-1991`, `:2091-2148`
(new-run order); `Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:1436-1439` (act-1 choice).

### 9.4 `TestMode`-dependent branches (the sim follows `TestMode.IsOff`)

Merchant potion prices skip their `Shops` draw in TestMode (`MerchantPotionEntry.cs:196-199`);
`ShouldApplyTutorialModifications` is false in TestMode (`Runs/RunManager.cs:4096-4099`);
`UpdatePlayerStatsInMapPointHistory` no-ops (`:4793-4796`); `TryGetRoomTypeForTutorial`
(`:4232`); `ActModel.GetRandomList` act forcing (`Models/ActModel.cs:487`); `MapRoom` and
`MerchantRoom.Exit` short-circuit (`Rooms/MapRoom.cs:26-29`, `Rooms/MerchantRoom.cs:202-205`);
`MoveToMapCoordAction` fades (`GameActions/MoveToMapCoordAction.cs:64-75`).

### 9.5 Listeners inert in single player

`MultiplayerScalingModel` (block ×1), badges (`Models.Badges/*.cs`, stats only), achievements, mod
subscribers (`Modding/ModHelper.cs` `IterateAllRunStateSubscribers`). The sim keeps the scaling
model's position in the listener order only if W1 shows an observable effect (×1m keeps decimal
scale; none expected).

### 9.6 First-run (tutorial) gates — off for a veteran profile

`NumberOfRuns == 0`: unknown points forced Event, Event, Monster (`Odds/UnknownMapPointOdds.cs:109-120`);
Ironclad fixed reward tables when also `EpochUnlockCount() == 0` (`Rewards/RewardsSet.cs:527-666`);
Gorget in the first chest (`Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:409-419`); Overgrowth's
fixed encounter/event/elite order (`Models.Acts/Overgrowth.cs:110-127`). Profile
`Progress.NumberOfRuns == 0`: an `Unassigned` floor becomes an Event (`Runs/RunManager.cs:4229-4255`).
`!StartedWithNeow` (Neow epoch not revealed): act 1 starts on a Monster point
(`Runs/RunManager.cs:2146-2149`). The sim exposes these through `UnlockProfile`; the default
profile disables them all.

### 9.7 Save/load quirks the sim must reproduce

- The shared bag never refreshes after a load (`Runs/RelicGrabBag.cs:358-363`), unlike in the
  original session: an imported run follows the loaded behaviour.
- After a load the map's `parents` sets follow wiring order (`Map/SavedActMap.cs:127-153`), not
  generation order; no rule reads parents after generation, but projections must use the loaded map.
- Reloading save A re-rolls the room on restored streams; reloading B regenerates the same rewards;
  reloading C regenerates ancient options on a fresh private Rng — all deterministic. Save-scumming
  is therefore inert, consistent with the P0 finding that retries restart from the same counters.

### 9.8 Numeric risks

- float32 accumulation in odds (§2.6) and float comparisons (`roll <= cumulative`,
  `r < rare + offset`) — port with `float`, never `double`.
- Godot `Mathf.RoundToInt`/`Mathf.Round` (merchant card and potion prices,
  `MerchantCardEntry.cs:181`, `:260`; `MerchantPotionEntry.cs:198`) — assumed `MidpointRounding.ToEven`
  like `Math.Round`; GodotSharp is not in the tree — verify.
- `NextGaussianInt` uses `Math.Log/Sqrt/Sin` (map counts): libm differences between the game's
  platform and the sim's could flip a rounding at a boundary — compare against saved maps on both
  macOS-arm64 and linux-x64.
- `decimal` heal amounts (`MaxHp × 0.3m`, arrival heal × 0.8m) truncate in `CreatureCmd.Heal`
  (combat kernel rule; reuse it).

### 9.9 Excluded modes and story gates

Daily (`Daily/*.cs`) and Custom modes, all 17 modifiers (`Models.Modifiers/*.cs`, including
DeadlyEvents' odds changes, BigGameHunter maps, Midas/Vintage rewards, Hoarder removals), and
multiplayer. Story/timeline content enters only as unlock state (epochs); FTUE popups block input
but change no rules. TheArchitect's dialogue and damage VFX are cosmetic.

### 9.10 Open questions

1. v0.111.0 values of the lost static arrays (§9.2), in particular `GoldenPathActMap` types and the
   merchant colored-card type list (assumed Attack, Attack, Skill, Skill, Power).
2. Godot `Mathf.RoundToInt` midpoint behaviour (§9.8).
3. Can the player close the map after a terminal rewards proceed and still claim unclaimed rewards
   (the rewards overlay stays on the stack, `mod/src/StateBuilder/PassiveStateBuilder.cs:87-89`)? The
   sim's legal set depends on it; verify in-game (MCP).
4. Resolved: the removal entry is marked used only by the UI node
   (`Nodes.Screens.Shops/NMerchantCardRemoval.cs:210-214`), so the sim ports "one removal per
   visit" as a rule; confirm no other path (e.g. TheCourier restock) re-enables it.
5. `ModHelper.IterateAllRunStateSubscribers` with the recorder mod loaded: confirm the recorder
   registers no run-hook subscriber (it is passive Harmony).
6. Exact `EncounterTag` sets per encounter (needed by `AddWithoutRepeatingTags`) — inventory work in
   `encounters.json`.
7. Whether `UnlockState.FromSerializable(null)` (`Unlocks/UnlockState.cs:131-139`, returns `all`)
   occurs in real saves; the local save always carries an unlock state.
8. Cross-platform determinism of `NextGaussianInt` (§9.8).
9. Whether WelcomeToWongos' profile-dependent badge (`Progress.WongoPoints`) should be modelled or
   pinned to 0 for training.
10. P1 plan says "A1–A20": confirm the plan text is corrected to A0–A10.

## Appendix A. RNG consumers outside combat

| Stream | Run-layer consumers |
| --- | --- |
| `UpFront` | grab-bag populates, shared-ancient shuffle and subset sizes, `ActModel.GenerateRooms`, A10 second boss, `ValidateRoomsAfterLoad` (`Runs/RunManager.cs:3828-3835`, `:4055-4087`, `:3879`) |
| `UnknownMapPoint` | `UnknownMapPointOdds.Roll` (`Odds/UnknownMapPointOdds.cs:124`) |
| `TreasureRoomRelics` | treasure relic rarity; bot votes and fights (`Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:160`, `:183`, `:339`, `:354`) |
| `Niche` | monster HP (combat); content such as Kaleidoscope's pool shuffles (the P0 Niche offset recipe) and Astrolabe (`Models.Relics/Astrolabe.cs:102`) |
| `PlayerRng.Rewards` | gold rewards, potion odds and generation, card rewards (rarity, pick, upgrade), elite/DIG relic rarity, merchant card rarity + upgrade floats + relic rarities, treasure gold; events PotionCourier, TheLegendsWereTrue, BattlewornDummy, EndlessConveyor, Wellspring; relics DustyTome, PaelsTooth, NeowsBones, ScrollBoxes |
| `PlayerRng.Shops` | merchant sale slot, card picks, all prices, potion generation, restocks |
| `PlayerRng.Transformations` | LeafyPoultice, Claws (other transforms pass their own rng) |
| ad hoc | `act_selection` (`XXH64(seedText) + XXH64(name)`), `act_{n}_map`, `spoils_map` (`Seed + XXH64(name)`), event private (`Seed + slot + XXH64(eventId)`, `Models/EventModel.cs:91`), encounter private, monster private (existing) |

## Appendix B. Run-scope hooks the run layer needs (from `sim/specs/inventory/hooks.json`)

Run scope, none in the sim (46): `AfterActEntered`, `BeforeCombatRewardOffered`,
`BeforeCardRemoved`, `AfterGoldGained`, `AfterItemPurchased`, `AfterMapGenerated`,
`AfterModifyingCardRewardOptions`, `AfterModifyingRewards`, `AfterRestSiteHeal`, `AfterRestSiteSmith`,
`AfterRewardTaken`, `BeforeRoomEntered`, `AfterRoomEntered`, `ModifyCardRewardCreationOptions(+Late)`,
`ModifyCardRewardUpgradeOdds`, `ModifyGeneratedMap(+Late)`, `ModifyMerchantCardPool`,
`ModifyMerchantCardRarity`, `ModifyMerchantCardCreationResults`, `ModifyMerchantPrice`,
`ModifyRestSiteHealAmount`, `ModifyNextEvent`, `ModifyUnknownMapPointRoomTypes`,
`ModifyOddsIncreaseForUnrolledRoomType`, `TryModifyCardBeingAddedToDeck(+Late)`,
`TryModifyCardRewardAlternatives`, `TryModifyCardRewardOptions(+Late)`, `TryModifyRestSiteOptions`,
`TryModifyRestSiteHealRewards`, `TryModifyRewards(+Late)`, `ModifyExtraRestSiteHealText`,
`ShouldAddToDeck`, `ShouldAllowAncient`, `ShouldAllowSelectingMoreCardRewards`,
`ShouldDisableRemainingRestSiteOptions`, `ShouldGenerateTreasure`, `ShouldProceedToNextMapPoint`,
`ShouldRefillMerchantEntry`, `ShouldAllowMerchantCardRemoval`, `ShouldForcePotionReward`,
`ShouldAllowFreeTravel`. Both scope, missing (12): `AfterCardChangedPiles(+Late)`, `AfterCombatEnd`,
`AfterCombatVictory(+Early)`, `AfterModifyingGoldGained`, `BeforePotionUsed`, `AfterPotionUsed`,
`AfterPotionDiscarded`, `AfterPotionProcured`, `ModifyGoldGained`, `ShouldProcurePotion`.
Implementer counts per hook are in the JSON companion.
