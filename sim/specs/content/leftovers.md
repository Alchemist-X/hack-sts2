# content-leftovers (W3.5) index

Things the W3 waves missed. `AfterCombatEnd` reaches listeners with `room = null` (`TurnFlow.CombatEnd.cs`), so the reward powers use `room ?? RoomStack.CurrentRoom(rs) as CombatRoom`. With no CombatRoom (a standalone `CombatSim`) they skip the reward and record `ApproximatedRule`. The game always has a room. The `MapPointHistoryEntry.GetEntry(netId).MarkLootStolen/MarkLootReturned` and `UpgradedCards` calls only feed run-history statistics; the sim has no per-player history entry, so they are dropped.

## Powers (port batch 8)
- FORBIDDEN_GRIMOIRE_POWER — Models.Powers/ForbiddenGrimoirePower.cs:15 — AfterCombatEnd: Amount × `CardRemovalReward(owner)` extra rewards (selecting one is still W2 R3)
- HEIST_POWER — Models.Powers/HeistPower.cs:18 — Instanced; BeforeDeath(owner): `GoldReward(Amount, Target.Player, wasGoldStolenBack: true)` if the current room is a CombatRoom
- IMPROVEMENT_POWER — Models.Powers/ImprovementPower.cs:16 — AfterCombatEnd: Amount × `RunState.Rng.CombatCardSelection.NextItem` over the upgradable deck cards, without replacement; `CardCmd.Upgrade` each
- ROYALTIES_POWER — Models.Powers/RoyaltiesPower.cs:15 — AfterCombatEnd: `GoldReward(Amount, owner)` extra reward
- SURPRISE_POWER — Models.Powers/SurprisePower.cs:113 — Single; ShouldStopCombatFromEnding true. AfterDeath(owner, not prevented): CreateCreature(FAT_GREMLIN, "fat"); for each THIEVERY instance, `HeistPower{Target}` × its Gold onto the fat gremlin (amount 0 is skipped, as in PowerCmd.Apply); then Add(SNEAKY_GREMLIN, "sneaky"), then Add(fat gremlin); total stolen > 0 in GREMLIN_MERC_NORMAL → MarkGoldStolen. The Heist apply on the not-yet-added creature attaches the power directly (`ApproximatedRule`): the sim's CanReceivePowers needs an attached creature (the game only needs `CombatState != null`), so the apply hooks do not fire
- SWIPE_POWER — Models.Powers/SwipePower.cs:41 — Instanced, Single. Steal(card): Target = card owner; if the card has a deck version, `RemoveFromDeck` it and keep an owner-less copy (owner-less so clones hash equal). BeforeDeath(owner): re-attach to Target.Player, clear HasBeenRemovedFromState (`RunState.AddCard`), `SpecialCardReward` with encounter source THIEVING_HOPPER_WEAK
- THIEVERY_POWER — Models.Powers/ThieveryPower.cs:41 — Instanced; GoldVar(0) is the int `Gold`. Steal(): if Target is alive with Gold > 0, LoseGold(min(Amount, gold), Stolen) and Gold += that amount

## Monsters
- GREMLIN_MERC — Models.Monsters/GremlinMerc.cs:103-130 — AfterAddedToRoom applies SURPRISE 1 and one THIEVERY 20 per player (Target = player). Each move calls `Steal()` on every THIEVERY instance (:261, :442, :565)
- THIEVING_HOPPER — Models.Monsters/ThievingHopper.cs:725-739 — after the combat removals, each stolen card gets a fresh `SwipePower.Steal(card)` and then `PowerCmd.Apply(swipe, hopper, 1)`. `IsHovering` lives on `ThievingHopperExtraState` (tag `MonsterCanonicalTags.ThievingHopperExtra` 0x4DF0); FLUTTER_MOVE sets it before FLUTTER 5 (:290), ESCAPE_MOVE clears it before escaping (:209-212), FLUTTER_POWER clears it on the stun
- FAT_GREMLIN, BATTLE_FRIEND_V1/V2/V3 — already ported by cards-11-monsters-6-8 and re-checked against Models.Monsters/FatGremlin.cs:204-233 and BattleFriendV1.cs:40-89 (IsAwake is animation-only)

## Cards, relic, events
- AGGRESSION — Models.Cards/Aggression.cs:94-113 — 1-cost Rare Power, Self; AGGRESSION_POWER 1; upgrade adds Innate
- FORBIDDEN_GRIMOIRE, ROYALTIES, MAD_SCIENCE — `[SimExclude]` removed now that their powers exist
- MEAL_TICKET — already ported (relics-7-2), re-checked against Models.Relics/MealTicket.cs:45-90
- TINKER_TIME — Models.Events/TinkerTime.cs:137-246 — CHOOSE_CARD_TYPE → TakeRandom(2, event Rng) of ATTACK/SKILL/POWER → TakeRandom(2) of that type's riders → a MadScience with TinkerTimeType/Rider into the deck. The rider arrays come from IL (lost InitializeArray in ChooseRiderEffect; ldtoken field RVAs 0x94F520, 0x94F5D0, 0x94F640 = int32 {1,2,3}, {4,5,6}, {7,8,9}): Attack Sapping/Violence/Choking, Skill Energized/Wisdom/Chaos, Power Expertise/Curious/Improvement. Hover-tip MadScience cards (`GetCardTypeHoverTip`) are UI-only and not created
- BATTLEWORN_DUMMY — Models.Events/BattlewornDummy.cs:166-200 — three SETTING options enter V1/V2/V3 encounters with `shouldResumeAfterCombat: true`. Resume: DEFEAT if RanOutOfTime, else VICTORY plus V1 a potion (`PlayerRng.Rewards.NextItem` over character + shared unlocked potions), V2 upgrade 2 of `StableShuffle(event Rng)` of the upgradable deck cards, V3 `RelicFactory.PullNextRelicFromFront`; extra rewards appended; `RewardsCmd.OfferCustom`. RanOutOfTime throws `W3.5 kernel-gaps: CombatState.EncounterExtra`

## Soak and exclusions
- ContentSoak sets every MAD_SCIENCE whose TinkerTimeType is None to a deterministic type/rider from its InstanceId (`Content_leftoversTests.ConfigureTinkerTimeForSoak`). A None-type MadScience throws in OnPlay in the game too (Models.Cards/MadScience.cs default branch); only TINKER_TIME creates it
- THIEVING_HOPPER_WEAK and BATTLEWORN_DUMMY_EVENT_V1/V2/V3_ENCOUNTER are un-excluded. GREMLIN_MERC_NORMAL stays `[SimExclude]`: SURPRISE → `GremlinMercNormal.MarkGoldStolen` and `CalculateGoldProportion` need `CombatState.EncounterExtra` / `CombatState.EscapedCreatures` (kernel-gaps)
- W4 integrate 1: GREMLIN_MERC_NORMAL un-excluded (kernel-gaps `EncounterExtra` / `EscapedCreatures` landed); `BattlewornDummy.RanOutOfTime` reads `CombatRoom.EncounterExtra` (`BattlewornDummyEventEncounter.Extra.RanOutOfTime`, Models.Events/BattlewornDummy.cs:62-67). Both former PENDING tests are real.
