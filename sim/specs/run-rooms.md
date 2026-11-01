# R4 rooms — spec index (W2, 2026-11-01)

One line per rule: `member — decompile file:line — dropped points / gaps`. Paths drop the `MegaCrit.Sts2.Core.` prefix.

## Room base and stack

- `AbstractRoom.Enter` (Id = `NextRoomId++`, then `EnterInternal`) — Rooms/AbstractRoom.cs:40-44, Runs/RunState.cs:1030-1035 — `Id` null is `AbstractRoom.NoId` (-1).
- `AbstractRoom.WriteCanonical` — header tag 0x5840, room type, model id, id, pre-finished, then `WritePayload` — sim only.
- `RoomStack.CreateRoom` — Runs/RunManager.cs:4178-4205 — combat rooms take the room type passed to `PullNextEncounter`; `model` arg covers the debug/event overload.
- `RoomStack.EnterRoom` (exit all, then enter) — Runs/RunManager.cs:929-1018 — fades, `ClearScreens`, music dropped.
- `RoomStack.EnterRoomInternal` — Runs/RunManager.cs:1247-1400 — external effects off when restoring the stack base or for a pre-finished combat/event room; push (throws if present); `BeforeRoomEntered` unless `MapRoom`; `Enter`; `MarkRoomVisited` when the stack has one room. `RunLocationTargetedBuffer`, `ActionExecutor.Unpause`, `RoomEntered` event dropped.
- `RoomStack.EnterRoomWithoutExitingCurrentRoom` — Runs/RunManager.cs:1402-1588 — appends `MapPointRoomHistoryEntry(type, modelId)` to the current history entry, then `EnterRoomInternal`; combat sync/fades dropped.
- `RoomStack.ExitCurrentRoom(s)` — Runs/RunManager.cs:1589-1760 — `BeforeLeavingRoom` then pop then `room.Exit`.
- `RoomStack.BeforeLeavingRoom` — Multiplayer.Game/RewardsSetSynchronizer.cs:750-829 — every open set, top first: `OnSkipped` on unselected rewards; stack cleared. Net messages dropped.
- `RoomStack.ResumePreviousRoom` — Runs/RunManager.cs:2489-2600 — exit top, `CurrentRoom.Resume(exited)`; fade/music dropped.
- `RoomStack.Offer(rs, set)` — Rewards/RewardsSet.cs:461 (`Offer`) — sim: pushes onto `rs.RewardsStack`; R3/R6 own the screen semantics (terminal flag, proceed).
- `RoomStack.CurrentMapPointHistoryEntry` / `CurrentMapCoord` — last history entry / last visited coord (Runs/RunState.cs `CurrentMapPointHistoryEntry`, `CurrentMapCoord`).
- `MapRoom` — Rooms/MapRoom.cs:1-45 — enter rejects stack reconstruction; node creation dropped.

## Combat room (§8.3)

- `EncounterRoomTypes` — Models.Encounters/*.cs `RoomType` overrides (90 encounters: 66 Monster, 12 Elite, 12 Boss) and `ShouldGiveRewards => false` (BattlewornDummyEventV1-3) — generated table, ids = Slugify(class); `EncounterDef` (W1 monsters) has no `RoomType`.
- `CombatRoom.RoomType` = `Encounter.RoomType` — Rooms/CombatRoom.cs:520-522.
- `CombatRoom` defaults: `GoldProportion` 1, `ShouldCreateCombat` true, `ShouldResumeParentEventAfterCombat` true — Rooms/CombatRoom.cs:526-560 — game `GoldProportion` is `float`; sim keeps the scaffold's `decimal` (value set from the float).
- `CombatRoom.EnterInternal` — Rooms/CombatRoom.cs:33-110 — pre-finished → `StartPreFinishedCombat`, else `StartCombat`.
- `CombatRoom.StartCombat` — Rooms/CombatRoom.cs:270-380 — windows A+B are `CombatLoop.SetUpCombat(rs, player, encounter, context)` (monster generation, spawn, setup); monster ids appended to the last history room; `Hook.AfterRoomEntered`; `CombatLoop.StartCombat` (= `AfterCombatRoomLoaded`). Preload/node dropped. `ShouldCreateCombat = false` throws `NotSupportedException` (not ported). Context = (CurrentActIndex, TotalFloor, current coord).
- `CombatRoom.StartPreFinishedCombat` — Rooms/CombatRoom.cs:381-480 — only `OfferRoomEndRewards` when `ShouldGiveRewards`; `GenerateMonstersWithSlots` (encounter-private rng only), `ResetCombatState`, `ProceedWithoutRewards` dropped (R6 drives proceed).
- `CombatRoom.OnCombatEnded` — Rooms/CombatRoom.cs:675-678 — GAP: `CalculateGoldProportion` (Models/EncounterModel.cs:324-327, GremlinMercNormal.cs:56-67) needs `CombatState.EscapedCreatures`, which the sim does not track; proportion stays 1.
- `CombatRoom.AfterCombatVictory` = `OnCombatEnded` + `MarkPreFinished` — Combat/CombatManager.cs:1648-1810 — the kernel's `EndCombatInternal` runs the hooks with `room = null` (W1); R6 calls this after victory, before save point B.
- `CombatRoom.OfferRoomEndRewards` — Rooms/CombatRoom.cs:176-299 — generate per player first, then per set `Hook.BeforeCombatRewardOffered` + offer.
- `CombatRoom.Exit` — Rooms/CombatRoom.cs:604-622 — `CombatManager.Reset` = `rs.Combat = null`; pre-finished creature removal dropped (no live combat).
- `CombatRoom.Resume` — throws (game `NotImplementedException`).

## Merchant (§3.8)

- `MerchantRoom.EnterInternal` — Rooms/MerchantRoom.cs:20-110 — one inventory per player, then `Hook.AfterRoomEntered`. Node-layer rule ported: `ShouldAllowMerchantCardRemoval` false → removal entry `SetUsed` at fill (Nodes.Screens.Shops/NMerchantCardRemoval.cs:153-156), before `AfterRoomEntered`.
- `MerchantRoom.Exit` — Rooms/MerchantRoom.cs:200-281 — history only; dropped.
- `MerchantRoom.TryPurchase(rs, i)` — index into `AllEntries`; `CanPurchase` = stocked and `EnoughGold`.
- `MerchantInventory.CreateForNormalMerchant` — Entities.Merchant/MerchantInventory.cs:87-109 — character, colorless, relics, potions, removal; `PurchaseCompleted += UpdateEntries` becomes `inventory.UpdateEntries(rs)` after a purchase.
- `MerchantInventory` colored types {Attack, Attack, Skill, Skill, Power} — MerchantInventory.cs:196-206 static blob lost in v0.111.0; values from design §3.8 / §9.2 ([v107]), not re-verified against IL.
- `PopulateCharacterCardEntries` — MerchantInventory.cs:115-131 — `Shops.NextInt(5)` sale slot first; `SetOnSale` right after that entry's `Populate`.
- `PopulateColorlessCardEntries` (Uncommon, Rare) — MerchantInventory.cs:133-142.
- `PopulateRelicEntries` (RollRarity ×2, then Shop) — MerchantInventory.cs:144-157.
- `PopulatePotionEntries` (`CreateRandomPotionsOutOfCombat(3, Shops)` then entries) — MerchantInventory.cs:159-174.
- `MerchantEntry.Cost`/`GetCost(rs)` — Entities.Merchant/MerchantEntry.cs:165-176 — sim `Cost` is the stored cost; `GetCost(rs)` applies `Hook.ModifyMerchantPrice` while the current room is a `MerchantRoom` (PlayerState has no run back-reference).
- `MerchantEntry.OnTryPurchaseWrapper` — MerchantEntry.cs:300-330 — stock, gold, purchase, refill-or-clear, `AfterItemPurchased`, inventory update. Purchase-failed events dropped.
- `MerchantCardEntry.Populate` — MerchantCardEntry.cs:202-229 — pool minus cards in the shop; `CardFactory.CreateForMerchant` (R3); `Hook.ModifyMerchantCardCreationResults`; `CalcCost`.
- `MerchantCardEntry.GetCost` (C 50, U 75, R 150; colorless `RoundToInt(x × 1.15f)`) — MerchantCardEntry.cs:171-184.
- `MerchantCardEntry.CalcCost` (`RoundToInt(cost × Shops.NextFloat(0.95, 1.05))`, sale halves) — MerchantCardEntry.cs:253-265.
- `MerchantCardEntry.OnTryPurchase` — MerchantCardEntry.cs:270-290 — `CardPileCmd.Add(Deck)` then `LoseGold(Spent)`; history/sync dropped. Restock clears sale and re-populates (MerchantCardEntry.cs:292-296).
- `MerchantRelicEntry` — Entities.Merchant/MerchantRelicEntry.cs:140-197 — player bag from the back with `IsAllowedInShops` (unregistered relic ids count as allowed — gap until W3), cost `Math.Round(MerchantCost × Shops.NextFloat(0.85, 1.15))`; purchase = `LoseGold` then `RelicCmd.Obtain`; restock blacklists stocked relics and rolls `RollRarity(player)`. `MarkRelicAsSeen` dropped.
- `MerchantPotionEntry` — Entities.Merchant/MerchantPotionEntry.cs:150-237 — C 50 / U 75 / R 100, `Mathf.Round(cost × Shops.NextFloat(0.95, 1.05))` (TestMode-off branch); purchase = `PotionCmd.TryToProcure` then `LoseGold`; restock passes an empty blacklist exactly as the game does.
- `MerchantCardRemovalEntry` — Entities.Merchant/MerchantCardRemovalEntry.cs:300-397 — 75 + 25 × removals (A6 Inflation 100 + 50); wrapper skips the stock check; `DoMerchantCardRemoval` (Multiplayer.Game/OneOffSynchronizer.cs:610-626): cancelable, manually confirmed 1-card removal prompt → `LoseGold`, `RemoveFromDeck`, `CardShopRemovalsUsed++`; success → `SetUsed` (node-layer NMerchantCardRemoval.cs:210-214) before `AfterItemPurchased`.
- `RoomPrompts.FromDeckForRemoval` — Commands/CardSelectCmd.cs:3607-3660 — removable cards, dead player → none, curses first then deck order; `FromDeckForUpgrade` — CardSelectCmd.cs:3441-3480. Prompts go through `rs.Choice.Ask` (kinds `DeckRemove`/`DeckUpgrade`, pile Deck).

## Rest site (§3.9)

- `RestSiteRoom.EnterInternal` — Rooms/RestSiteRoom.cs:23-120, Multiplayer.Game/RestSiteSynchronizer.cs:531-553 — `RestSiteOption.Generate` then `Hook.AfterRoomEntered`; preload/node dropped.
- `RestSiteOption.Generate` (Heal, Smith, Mend if >1 player, `Hook.ModifyRestSiteOptions`) — Entities.RestSite/RestSiteOption.cs:47-68.
- `RestSiteRoom.ChooseOption` — RestSiteSynchronizer.cs:610-661 — false → no change; success → `ShouldDisableRemainingRestSiteOptions` clears all else removes the chosen one; empty → completed. History dropped.
- `RestSiteRoom.ProceedAllowed` — node-layer Nodes.Rooms/NRestSiteRoom.cs:188-190 (after a successful option), :1056-1062 (no options left).
- `RestSiteRoom.Exit` — RestSiteSynchronizer.cs:663-679 `BeforeLocalRestSiteExited` — remaining options cleared.
- `HealRestSiteOption` — Entities.RestSite/HealRestSiteOption.cs:93-200 — `MaxHp × 0.3m` → `ModifyRestSiteHealAmount` → heal (out of combat = `HealInternal`, Commands/CreatureCmd.cs:2044-2230) → `AfterRestSiteHeal` → `ModifyRestSiteHealRewards` → `RewardsCmd.OfferCustom` (R3, always called, empty list included).
- `SmithRestSiteOption` — SmithRestSiteOption.cs:221-282 — `IsEnabled` = any upgradable deck card; cancelable confirmed prompt of `SmithCount`; `CardCmd.Upgrade(null, card)`; `AfterRestSiteSmith`.
- `DigRestSiteOption` — DigRestSiteOption.cs — `RelicCmd.Obtain(RelicFactory.PullNextRelicFromFront(player))` (R3).
- `CookRestSiteOption` — CookRestSiteOption.cs:198-232 — ≥2 removable; 2-card removal prompt; remove each; `GainMaxHp(5)` out of combat = `SetMaxHpInternal` + `HealInternal(gained)`.
- `HatchRestSiteOption` — HatchRestSiteOption.cs:99 — `RelicCmd.Obtain("BYRDPIP")`.
- GAP (stubs): `LiftRestSiteOption` (Girya.TimesLifted), `KindleRestSiteOption` (PumpkinCandle.Rekindle), `CloneRestSiteOption` (needs a run-scope `CloneCard`) — W3 content; `MendRestSiteOption` multiplayer-only.

## Treasure (§3.7, §9.3)

- `TreasureRoom(actIndex)` 0..2 — Rooms/TreasureRoom.cs:294-301.
- `TreasureRoom.EnterInternal` — TreasureRoom.cs:236-263 — `AfterRoomEntered`, then `BeginRelicPicking`.
- `BeginRelicPicking` — Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:137-203 — per player with `ShouldGenerateTreasure`: `RollRarity(Rng.TreasureRoomRelics)` → shared bag `PullFromFront` ?? `CIRCLET`; none → end voting. Tutorial Gorget override dropped (veteran profile, §9.6). Fake-multiplayer bot votes unreachable in SP.
- `TreasureRoom.OpenChest` — node-layer Nodes.Rooms/NTreasureRoom.cs:172-330 — `DoNormalRewards` then `DoExtraRewardsIfNeeded` then the relic choice; `ProceedAllowed` only after opening (NTreasureRoom.cs:640-646).
- `DoNormalRewards` — Multiplayer.Game/OneOffSynchronizer.cs:650-667 — `Rewards.NextInt(42, 53)`, ×0.75 at A3 Poverty, `(int)` → `GainGold`; SpoilsMap quest (:670-700): GAP — throws `NotImplementedException` when the deck holds `SPOILS_MAP` (needs R2 map-point quests + W3 card).
- `DoExtraRewardsIfNeeded` — TreasureRoom.cs:47-171 — `GenerateForRoomEnd` per player, offered; sim offers only non-empty sets (empty set = no screen).
- `TakeRelic` — TreasureRoomRelicSynchronizer.cs:238-377 + node-layer NTreasureRoomRelicCollection.cs:505 — SP single vote → `RelicCmd.Obtain`; end voting. Empty consolation `StableShuffle` draws nothing.
- `SkipRelic` — TreasureRoomRelicSynchronizer.cs:252-262, NTreasureRoom.cs:551-565 — SP sets `SingleplayerSkipped` (then proceed); `Exit` ends voting when skipped (:379-385).
- GAP: relic-picking fight (`RelicPickingResult.GenerateRelicFight`, ≥2 voters) not ported — unreachable in SP.
