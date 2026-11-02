from annot_lib import A, P

A("DieForYouPower", 3, ["Osty's guard: powered attacks on Osty's owner have their unblocked damage redirected to Osty while Osty is alive (ModifyUnblockedDamageTarget)",
                        "dead creatures cannot be hit (ShouldAllowHitting = IsAlive, global)", "Osty is not removed from combat on death; the power survives its death"],
  req=["Osty / pets (PetOwner, PlayerCmd.AddPet, OstyCmd.Summon applies it)", "Hook.ModifyUnblockedDamageTarget (present)", "dead-but-present pet creature"],
  notes="Applied only by core code (Commands/OstyCmd.cs), never by content. ShouldAllowHitting returns creature.IsAlive for every creature, not only the owner.",
  batch="osty")

A("DisintegrationPower", 2, ["owner side turn end (late): owner takes Amount damage (Unpowered, dealer owner)"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("DoomPower", 2, ["if Doom >= current HP at the end of the doomed creature's side turn, it dies: enemies at BeforeSideTurnEnd(Enemy), player-side creatures at AfterSideTurnEnd(Player)",
                   "one batch kill per side: the first doomed creature's power kills every doomed creature on that side (DoomKill: Kill each, then Hook.AfterDiedToDoom)",
                   "public DoomKill / GetDoomedCreatures used by cards (EndOfDays, TimesUp, …)"],
  req=["Hook.AfterDiedToDoom (dispatcher, per-listener choice context)", "HookParam.SideTurnParticipants", "CombatStatus.IsOverOrEnding",
       "CreatureCmd.Kill (present)"],
  notes="PlayVfx/StartDoomAnim are UI only (MonsterModel.OnDieToDoom is called only from the UI path and only TorchHeadAmalgam overrides it, with visuals). Kill order: DoomKill iterates GetCreaturesOnSide order.",
  vd=("rule-change", "v0.107.1 killed at BeforeSideTurnEnd of the owner's side for both sides; v0.111.0 keeps that for enemies but kills player-side creatures at AfterSideTurnEnd(Player)"),
  batch="doom")

A("DoubleDamagePower", 2, ["owner's (or owner's pets') powered card attacks deal x2", "Decrement at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "Creature.Pets (empty unless Necrobinder)"],
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("DrawCardsNextTurnPower", 2, ["owner's next hand draw +Amount (only once AmountOnTurnStart != 0)", "removed at owner side turn start after it applied"],
  req=["HookParam.SideTurnParticipants", "PowerModel.AmountOnTurnStart snapshot (present)"],
  notes="AmountOnTurnStart gate makes a copy applied during the current turn wait for the next turn.",
  batch="core")

A("DuplicationPower", 2, ["owner's next card is played one extra time (ModifyCardPlayCount +1); Decrement per use", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("DyingStarPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card DyingStar)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("EchoFormPower", 3, ["the first Amount cards the owner plays each turn (first-in-series plays counted from combat history) are played one extra time"],
  req=["CombatHistory: CardPlayStartedEntry by owner, IsFirstInSeries, HappenedThisTurn",
       "ordering: GeneratePlayCount (ModifyCardPlayCount) runs before this play's CardPlayStarted entry is recorded"],
  notes="AfterApplied/AfterRemoved/AfterCardPlayed/BeforeSideTurnStart only drive a VFX node.",
  vd=("cosmetic", "v0.111.0 adds VFX-only overrides; ModifyCardPlayCount logic identical"),
  batch="history")

A("EnergyNextTurnPower", 1, ["after the owner's player energy reset: gain Amount energy, then remove itself"],
  batch="core")

A("EnfeeblingTouchPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card EnfeeblingTouch)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("EnragePower", 1, ["whenever any Skill is played: owner gains Amount Strength"],
  notes="No owner check on the card (Test Subject boss reacts to the player's Skills).",
  batch="monster:TestSubject", deps=["StrengthPower"])

A("EntropyPower", 3, ["owner's player turn start: choose exactly Amount cards in hand (clipped to hand size) and transform each into a random card"],
  req=["CardSelectCmd.FromHand (choice API)", "CardCmd.TransformToRandom (card generation)", "turn-setup choice deferral (SetupPlayerTurn context)"],
  prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", "Amount", "Amount", "AfterPlayerTurnStart",
             extra={"prompt": "TransformSelectionPrompt", "filter": None, "followup": "CardCmd.TransformToRandom per selected card, in selection order"})],
  rng=["RunRng.CombatCardSelection (passed to CardCmd.TransformToRandom for each selected card)"],
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="prompt")

A("EnvenomPower", 1, ["owner's unblocked powered attack damage applies Amount Poison to the target"],
  batch="core", deps=["PoisonPower"])

A("EscapeArtistPower", 2, ["Thieving Hopper countdown: Decrement at owner side turn end while Amount > 1; stays at 1 (pulses)"],
  req=["HookParam.SideTurnParticipants", "MonsterDef coupling: ThievingHopper reads the amount"],
  batch="monster:ThievingHopper")

A("FadePower", 2, ["temporary Dexterity (TemporaryDexterityPower subclass; origin card Fade)"],
  req=["TemporaryDexterityPower base", "ITemporaryPower"],
  vd=("new", "not in v0.107.1"),
  batch="temp", deps=["TemporaryDexterityPower"])

A("FanOfKnivesPower", 1, ["marker read by the Shiv card (changes Shiv targeting)"],
  batch="core")

A("FastenPower", 1, ["owner gains +Amount on powered block from Defend-tagged cards or non-card sources"],
  req=["CardInstance.HasTag(CardTag.Defend)"],
  batch="core")

A("FeedingFrenzyPower", 2, ["temporary Strength (TemporaryStrengthPower subclass; origin card FeedingFrenzy)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("FeelNoPainPower", 1, ["each owner card exhausted: gain Amount block (Unpowered)"],
  batch="core")

A("FeralPower", 3, ["up to Amount zero-energy owner Attacks per turn return to the top of the hand instead of their result pile (not dupes)",
                    "on apply the counter starts at the number of such attacks already played this turn (combat history)"],
  req=["Hook.ModifyCardPlayResultLocation", "Hook.AfterModifyingCardPlayResultLocation", "CardLocation result struct",
       "CombatHistory: CardPlayStartedEntry (Attack, owner's player, EnergyValue 0, this turn)", "ResourceInfo.EnergyValue", "CardInstance.IsDupe",
       "HookParam.SideTurnParticipants"],
  vd=("rule-change", "v0.111.0 adds the IsDupe exclusion (dupes no longer return to hand) on top of the CardLocation hook refactor"),
  batch="history")
