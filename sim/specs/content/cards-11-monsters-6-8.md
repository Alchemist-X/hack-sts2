# W3 content index: cards-11-monsters-6-8 (wave C)

Kernel edits: `CardCmd.TransformDeckCardTo(rs, original, replacementId)` (deck branch of `Commands/CardCmd.cs:2052-2065`; FloorAddedToDeck + map-history CardsTransformed dropped, no fields); `PowerModel.CloneDetached()` (`Models/AbstractModel.cs:155-171` + `Models/PowerModel.cs:703-712`: owner null; `InitInternalData` reset not mirrored); `CardDef.GetCardType(card)` + `CardInstance.Type` routed through it (per-instance type for MadScience).

## Cards
- BYRDONIS_EGG — Models.Cards/ByrdonisEgg.cs:8 — TryModifyRestSiteOptions adds `HatchRestSiteOption` (R4).
- CREATIVE_AI — Models.Cards/CreativeAi.cs:15 — PowerUp anim dropped.
- DEPRECATED_CARD — Models.Cards/DeprecatedCard.cs:13 — DeckVersion nulled as `DeckVersionId = NoDeckVersion`; shouldShowInCardLibrary dropped.
- DOWSING — Models.Cards/Dowsing.cs:17 — CurrentRoomCount = `rs.CurrentRooms.Count`, CurrentMapPoint = `rs.Map.GetPoint(rs.CurrentMapCoord)`; TransformTo<Abundance> via new `CardCmd.TransformDeckCardTo`; RoomsEntered setter's Rooms var sync done in the def (importer FillProperties sets the state only).
- FORBIDDEN_GRIMOIRE — Models.Cards/ForbiddenGrimoire.cs:14 — `[SimExclude]` until FORBIDDEN_GRIMOIRE_POWER (powers batch 8, unowned) is ported.
- GENETIC_ALGORITHM — Models.Cards/GeneticAlgorithm.cs:16 — CurrentBlock/IncreasedBlock in extra state; Block var synced in the def; save import does not resync the var.
- GUILTY — Models.Cards/Guilty.cs:15 — AfterCombatEnd deck listener; `Pile.Type == Deck` read as `IsInDeck`.
- LANTERN_KEY — Models.Cards/LanternKey.cs:8 — ModifyNextEvent returns `EventRegistry.Create("WAR_HISTORIAN_REPY")` (events-3456-2); throws until that event is ported.
- MAD_SCIENCE — Models.Cards/MadScience.cs:25 — `[SimExclude]`: unconfigured instance (TinkerTimeType None) throws as in the game, and IMPROVEMENT_POWER is unported; RiderEffect enum nested in MadScience (TinkerTime event owner should use `MadScience.SetTinkerTimeType/SetTinkerTimeRider`); MockedChaosCard test seam dropped; saved props stored as int.
- MISERY — Models.Cards/Misery.cs:20 — Dictionary snapshot mirrored as an ordered list (insertion order, overwrite-in-place during the temp-power merge); HittableEnemies snapshotted once.
- ROYALTIES — Models.Cards/Royalties.cs:15 — `[SimExclude]` until ROYALTIES_POWER (powers batch 8, unowned) is ported.
- SPOILS_MAP — Models.Cards/SpoilsMap.cs:17 — AfterMapGenerated quest add throws "W3 gap" (sim ActMap quest overlay is copy-on-write, the hook cannot replace the map; reachable only after ModifyGeneratedMapLate, which RunLoop does not dispatch — saved-map path, RunManager.cs:2117); quest owner key = card id; `OnQuestComplete(rs, card)` static for TreasureRoom (R4 stub at TreasureRoom.TryHandleSpoilsMap).
- THE_HUNT — Models.Cards/TheHunt.cs:23 — no CombatRoom on the room stack ⇒ no attack (as game); extra reward `CardReward(owner, ForRoom(rs, owner, roomType), 3)` via `CombatRoom.AddExtraReward`.
- THE_SCYTHE — Models.Cards/TheScythe.cs:17 — CurrentDamage/IncreasedDamage in extra state; Damage var synced in the def.
- WHITE_NOISE — Models.Cards/WhiteNoise.cs:15 — Cast anim dropped.

## Monsters
- BATTLE_FRIEND_V1 — Models.Monsters/BattleFriendV1.cs:17 — skin/animator dropped.
- BATTLE_FRIEND_V2 — Models.Monsters/BattleFriendV2.cs:17 — HP 150.
- BATTLE_FRIEND_V3 — Models.Monsters/BattleFriendV3.cs:17 — HP 300.
- FAT_GREMLIN — Models.Monsters/FatGremlin.cs:23 — IsAwake (anim only), banter, waits, interactable toggle dropped.
- GREMLIN_MERC — Models.Monsters/GremlinMerc.cs:26 — SURPRISE_POWER / THIEVERY_POWER (powers batch 8, unowned) throw "W3 gap" at spawn and at `ThieveryPower.Steal`; _hasSpoken banter dropped; encounter stays `[SimExclude]`.
- THIEVING_HOPPER — Models.Monsters/ThievingHopper.cs:31 — card pick ported (priorities, CombatCardGeneration.NextItem, RemoveFromCombat); `SwipePower.Steal` + apply throws "W3 gap" (SWIPE_POWER unported); IsHovering (sfx only) and stolen-card node dropped.
- BYRDPIP — Models.Monsters/Byrdpip.cs:10 — IsHealthBarVisible / skin dropped.
- PAELS_LEGION — Models.Monsters/PaelsLegion.cs:11 — animator triggers dropped.
- OSTY — Models.Monsters/Osty.cs:13 — already ported (W1), not touched.
