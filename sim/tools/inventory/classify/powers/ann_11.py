from annot_lib import A, P

A("SkittishPower", 1, ["Phantasmal Gardener: the first card attack each player turn that deals unblocked damage to it grants Amount block; flag resets when the opposing side's turn ends"],
  req=["AttackCommand.Results / ModelSource (present)", "public HasGainedBlockThisTurn read by the monster"],
  notes="GetScaledAmountForMultiplayer/ShouldScaleInMultiplayer are multiplayer-only.",
  vd=("cosmetic", "HasFlag spelled through System.Enum; same logic"),
  batch="monster:PhantasmalGardener")

A("SleightOfFleshPower", 2, ["whenever the owner applies a non-temporary debuff (by amount sign) to an enemy: deal Amount (Unpowered) to that enemy"],
  req=["ITemporaryPower marker on the temporary-power family", "PowerModel.GetTypeForAmount (present)"],
  vd=("cosmetic", "Array.Empty spelled fully qualified"),
  batch="temp", deps=["TemporaryStrengthPower"])

A("SlipperyPower", 1, ["owner's HP loss per instance capped at 1 (ModifyHpLostAfterOsty); Decrement whenever it takes >= 1 unblocked damage"],
  notes="GetScaledAmountForMultiplayer/ShouldScaleInMultiplayer are multiplayer-only.",
  batch="core")

A("SlothPower", 2, ["owner can play at most Amount cards per turn (ShouldPlay); counter increments before each owner card play and resets at owner side turn start"],
  req=["HookParam.SideTurnParticipants", "per-instance field _cardsPlayedThisTurn"],
  batch="core")

A("SlowPower", 2, ["every card played (any owner) adds 1 SlowAmount; owner takes x(1 + 0.1 x SlowAmount) from powered attacks", "SlowAmount resets at owner side turn start"],
  req=["HookParam.SideTurnParticipants", "mutable per-instance DynamicVar SlowAmount"],
  vd=("cosmetic", "v0.111.0 adds a DisplayAmount DynamicVar (UI text only)"),
  batch="monster:BygoneEffigy")

A("SlumberPower", 2, ["Slumbering Beetle sleep: Decrement on unblocked damage (at 0: Stun into WakeUpMove, next ROLL_OUT_MOVE) and at owner side turn end (at 0: run WakeUpMove directly)"],
  req=["HookParam.SideTurnParticipants", "CreatureCmd.Stun(creature, stunMove, nextMoveId)", "MonsterDef coupling: SlumberingBeetle.WakeUpMove"],
  batch="monster:SlumberingBeetle")

A("SmoggyPower", 3, ["Living Fog debuff on the player: after the player plays a Skill, all unafflicted Skills get Smog (and Skills entering combat later this turn); Smog cards cannot be played",
                     "Smog is cleared at owner side turn end"],
  req=["Afflictions (Smog: CardCmd.Afflict<T>, CardCmd.ClearAffliction)", "CombatHistory: CardPlayStartedEntry (Skill, owner's player, this turn)",
       "HookParam.SideTurnParticipants", "PlayerCombatState.AllCards"],
  vd=("cosmetic", "closure capture renamed; same logic"),
  batch="afflict")

A("SmokestackPower", 1, ["whenever the owner's player generates a Status card: deal Amount (Unpowered) to all hittable enemies"],
  batch="core")

A("SneakyPower", 1, ["whenever a creature other than the owner plays an Attack card: owner gains Amount block (Unpowered, fast)"],
  notes="Multiplayer-oriented: in single player no other creature owns cards, so it never fires.",
  batch="core")

A("SoarPower", 1, ["Owl Magistrate: takes x(DamageDecrease 50 / 100) from powered attacks"],
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="monster:OwlMagistrate")

A("SoulboundPower", 2, ["whenever the applier generates a Soul card: shuffle Amount Souls (Soul.Create) into the owner's draw pile at random positions (re-entrancy guarded)"],
  req=["CardPileCmd.AddGeneratedCardsToCombat (Draw, CardPilePosition.Random)", "Soul card def + Soul.Create helper", "Hook.AfterCardGeneratedForCombat"],
  rng=["RunRng.Shuffle (CardPilePosition.Random insertion per Soul)"],
  vd=("new", "not in v0.107.1"),
  batch="cardops")

A("SpectrumShiftPower", 3, ["before the owner's hand draw: add Amount distinct random colorless cards to hand"],
  req=["CardFactory.GetDistinctForCombat", "ColorlessCardPool.GetUnlockedCards(UnlockState, CardMultiplayerConstraint)", "CardPileCmd.AddGeneratedCardsToCombat (Hand)"],
  rng=["RunRng.CombatCardGeneration (GetDistinctForCombat, Amount cards)"],
  batch="cardgen")

A("SpeedsterPower", 1, ["each card the owner draws outside the hand draw on its own turn: deal Amount (Unpowered) to all hittable enemies"],
  batch="core")

A("SpeedPotionPower", 2, ["temporary Dexterity (TemporaryDexterityPower subclass; origin potion SpeedPotion)"],
  req=["TemporaryDexterityPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryDexterityPower"])

A("SpinnerPower", 3, ["after the owner's player energy reset: channel Amount Glass orbs"],
  req=["OrbCmd.Channel<GlassOrb>", "OrbQueue", "GlassOrb def (not in ModelDb.Orbs list)"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="orbs")

A("SpiritOfAshPower", 1, ["before each owner Ethereal card play: gain Amount block (Unpowered)"],
  req=["CardInstance keyword query including in-combat modifiers (card.Keywords)"],
  batch="core")

A("StampedePower", 2, ["when the owner's auto post-play phase starts (end of turn): Amount times auto-play a random playable Attack from hand"],
  req=["Hook.AfterAutoPostPlayPhaseEntered", "CardCmd.AutoPlay", "auto post-play phase in the turn loop"],
  rng=["RunRng.Shuffle (NextItem over hand Attacks without Unplayable, once per iteration)",
       "RunRng.CombatTargets (indirect: CardCmd.AutoPlay random target for AnyEnemy attacks, after each Shuffle pick, Commands/CardCmd.cs:411)"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="cardops")

A("StarNextTurnPower", 3, ["after the owner's player energy reset: gain Amount stars, remove itself"],
  req=["Stars subsystem", "PlayerCmd.GainStars"],
  batch="stars")

A("SteamEruptionPower", 1, ["Waterfall Giant: on owner death (not prevented) runs WaterfallGiant.TriggerAboutToBlowState; keeps combat going; owner stays in combat; power survives death"],
  req=["dead-but-present monster flow (ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldStopCombatFromEnding)", "MonsterDef coupling: WaterfallGiant.TriggerAboutToBlowState"],
  batch="monster:WaterfallGiant")

A("StockPower", 3, ["Axebot: on owner death (not prevented, Amount > 0) spawns a replacement Axebot in the same slot with StockAmount = Amount - 1; keeps combat from ending"],
  req=["CreatureCmd.Add(monster, combatState, side, slotName) (mid-combat summon)", "MonsterDef coupling: Axebot.StockAmount / ShouldPlaySpawnAnimation"],
  notes="RevealReplacementAfterDeathAnim is UI.",
  vd=("cosmetic", "v0.111.0 spawns hidden and reveals after the death animation (UI); same rule"),
  batch="summon")
