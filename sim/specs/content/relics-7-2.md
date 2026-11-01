# relics-7-2 (wave C) port index

MAW_BANK — Models.Relics/MawBank.cs:17 — Flash and ShouldFlashOnPlayer dropped; BaseRoom via RoomStack.BaseRoom; HasItemBeenBought in extra canonical
MEAL_TICKET — Models.Relics/MealTicket.cs:15 — Flash dropped; heal out of combat via CreatureCmd.Heal(RunState) (no hook, as the game with a null CombatState); HpHealed map history not tracked
NEOWS_SACRIFICE — Models.Relics/NeowsSacrifice.cs:17 — HoverTips and PreviewCardPileAdd dropped; GUILTY is unregistered at this base, deck add skipped and recorded in the fidelity log until it is ported
NEOWS_TALISMAN — Models.Relics/NeowsTalisman.cs:12 — card Tags read through CardInstance.HasTag (canonical tags)
NEOWS_TORMENT — Models.Relics/NeowsTorment.cs:15 — HoverTips and PreviewCardPileAdd dropped
NUTRITIOUS_OYSTER — Models.Relics/NutritiousOyster.cs:13 — out of combat via CreatureCmd.GainMaxHp(RunState) (MaxHpGained history not tracked)
NUTRITIOUS_SOUP — Models.Relics/NutritiousSoup.cs:18 — NCardEnchantVfx dropped; ModelDb canonical CanEnchant via a fresh EnchantmentRegistry instance
OLD_COIN — Models.Relics/OldCoin.cs:14 — none
PAELS_CLAW — Models.Relics/PaelsClaw.cs:19 — EnchantmentName StringVar holds the id GOOPY instead of the localized title; enchant VFX dropped
PAELS_HORN — Models.Relics/PaelsHorn.cs:15 — PreviewCardPileAdd dropped
PANDORAS_BOX — Models.Relics/PandorasBox.cs:19 — replacements drawn in deck order up front (game draws lazily inside Transform; same Niche call order); CardFactory.CreateRandomCardForTransform inlined because its cs==null path throws; deck transform through new CardCmd.TransformInDeck; NSimpleCardsViewScreen dropped; CardsTransformed history and FloorAddedToDeck not tracked
PEAR — Models.Relics/Pear.cs:13 — as NUTRITIOUS_OYSTER
PHIAL_HOLSTER — Models.Relics/PhialHolster.cs:16 — none
PLANISPHERE — Models.Relics/Planisphere.cs:16 — Flash dropped; CurrentMapPoint via RunLoop.CurrentMapPoint, CurrentRoomCount as rs.CurrentRooms.Count
POTION_BELT — Models.Relics/PotionBelt.cs:13 — none
SAND_CASTLE — Models.Relics/SandCastle.cs:16 — GridCardPreviewContainer UI dropped
SERE_TALON — Models.Relics/SereTalon.cs:17 — PreviewCardPileAdd and Cmd.Wait(0.75) dropped; out-of-combat LoseMaxHp uses LoseHpInternal (no damage hooks out of combat)
SIGNET_RING — Models.Relics/SignetRing.cs:13 — none
STORYBOOK — Models.Relics/Storybook.cs:15 — PreviewCardPileAdd dropped
STRAWBERRY — Models.Relics/Strawberry.cs:13 — as NUTRITIOUS_OYSTER
TANXS_WHISTLE — Models.Relics/TanxsWhistle.cs:15 — PreviewCardPileAdd dropped
TOUCH_OF_OROBAS — Models.Relics/TouchOfOrobas.cs:18 — implements IEventRelicSetup; ModelId? as string ids; StringVars hold ids, hover tips dropped; obtained without setup and without a starter relic it records fidelity and returns (the game throws; unreachable from Orobas)
VENERABLE_TEA_SET — Models.Relics/VenerableTeaSet.cs:17 — Flash dropped; GainEnergyInNextCombat in extra canonical
WAR_HAMMER — Models.Relics/WarHammer.cs:15 — Flash dropped
WAR_PAINT — Models.Relics/WarPaint.cs:14 — none
WHETSTONE — Models.Relics/Whetstone.cs:14 — skipped: already ported (W1 pilot)
