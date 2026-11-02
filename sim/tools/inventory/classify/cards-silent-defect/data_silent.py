from data_common import *  # noqa: F401,F403

S = {}


def c(cls, mech, *, kreq=(), powers=(), cards=(), orbs=(), ench=(), prompts=(), rng_=(), tier_own=1, batch=1,
      notes="", oq=()):
    S[cls] = {
        "mechanics": list(mech),
        "kernel_requirements": list(kreq),
        "deps": {"powers": list(powers), "cards": list(cards), "orbs": list(orbs), "enchantments": list(ench)},
        "prompts": list(prompts),
        "rng_explicit": list(rng_),
        "tier_own": tier_own,
        "port_batch": batch,
        "notes": notes,
        "open_questions": list(oq),
    }


c("StrikeSilent", ["damage 6 (+3) to target"], batch=1,
  notes="Starter (x5). CardTag.Strike. Same shape as the ported StrikeIronclad.")
c("DefendSilent", ["block 5 (+3)"], batch=1,
  notes="Starter (x5). CardTag.Defend; GainsBlock=true (read by the Nimble enchantment filter).")
c("Neutralize", ["damage 3 (+1) to target", "apply Weak 1 (+1) to target"], powers=["WeakPower"], batch=1,
  notes="Starter. The SaveManager FastMode read only changes an animation delay (UI).")
c("Survivor", ["block 8 (+3)", "discard 1 card from hand (choice)"],
  kreq=["CardSelectCmd.FromHandForDiscard", K_DISCARD],
  prompts=[discard_prompt(1)], tier_own=3, batch=5,
  notes="Silent starter: a Silent run needs the HandSelect prompt from turn 1. Block first, then the prompt; "
        "a chosen Sly card auto-plays after the discard.")
c("Slice", ["damage 6 (+3) to target"], batch=1, notes="0 cost. FastMode read is UI-only.")
c("Deflect", ["block 4 (+3)"], batch=1, notes="0 cost.")
c("Backflip", ["block 5 (+3)", "draw 2"], batch=1)
c("DodgeAndRoll", ["block 4 (+2)", "apply BlockNextTurnPower = block actually gained"],
  kreq=[K_GAINBLOCK_RET], powers=["BlockNextTurnPower"], batch=2,
  notes="The power amount is the GainBlock return value (after Dexterity/Frail and block hooks), not the BlockVar.")
c("Anticipate", ["apply AnticipatePower (temporary Dexterity) 2 (+2)"],
  kreq=[K_TMPDEX], powers=["AnticipatePower"], batch=2,
  notes="0 cost. AnticipatePower is a TemporaryDexterityPower: Dexterity now, reverted at the owner's turn end.")
c("PoisonedStab", ["damage 6 (+2) to target", "apply Poison 3 (+1) to target"], powers=["PoisonPower"], batch=2)
c("DeadlyPoison", ["apply Poison 5 (+2) to target"], powers=["PoisonPower"], batch=2)
c("SuckerPunch", ["damage 8 (+2) to target", "apply Weak 1 (+1) to target"], powers=["WeakPower"], batch=1)
c("Snakebite", ["Retain", "apply Poison 7 (+3) to target"], powers=["PoisonPower"], batch=2,
  notes="Retain keyword exists in the sim. Silent2Epoch-gated.")
c("Abrasive", ["Sly", "apply Dexterity 1", "apply Thorns 4 (+2)"],
  kreq=[K_SLY], powers=["DexterityPower", "ThornsPower"], tier_own=2, batch=3,
  notes="3-cost Power with Sly: free auto-play when discarded by a card effect. Order: Dexterity then Thorns.")
c("Accelerant", ["apply AccelerantPower 1 (+1)"], powers=["AccelerantPower", "PoisonPower"], batch=2,
  notes="AccelerantPower has no hooks: PoisonPower.TriggerCount adds the Accelerant of alive opponents "
        "(min(Amount, 1 + sum); Models.Powers/PoisonPower.cs:235-241). Silent2Epoch-gated; rarity Rare->Uncommon in v0.111.0.")
c("Accuracy", ["apply AccuracyPower 4 (+2): Shiv attacks +N damage"],
  powers=["AccuracyPower"], cards=["Shiv"], batch=3,
  notes="AccuracyPower.ModifyDamageAdditive: dealer is owner, powered attack, cardSource tagged Shiv. Batch with Shiv generation.")
c("Acrobatics", ["draw 3 (+1)", "discard 1 card from hand (choice)"],
  kreq=["CardSelectCmd.FromHandForDiscard", K_DISCARD], prompts=[discard_prompt(1)], tier_own=3, batch=5,
  notes="Draw first, then the prompt; FirstOrDefault of the selection is discarded.")
c("Adrenaline", ["Exhaust", "gain 1 (+1) energy", "draw 2"], batch=1,
  notes="0 cost. Energy before draw. LocalContext.IsMe guard only wraps a VFX call.")
c("Afterimage", ["apply AfterimagePower 1: block per card played", "upgrade: gain Innate"],
  powers=["AfterimagePower"], batch=2,
  notes="AfterimagePower snapshots Amount per played card in BeforeCardPlayed, grants Unpowered block in AfterCardPlayed "
        "(internal Dictionary<CardModel,int> -> instance-id map in the sim).")
c("Assassinate", ["Innate", "Exhaust", "damage 10 (+3) to target", "apply Vulnerable 1 (+1)"],
  powers=["VulnerablePower"], batch=1, notes="0 cost.")
c("Backstab", ["Innate", "Exhaust", "damage 11 (+4) to target"], batch=1, notes="0 cost.")
c("BladeDance", ["Exhaust", "create 3 (+1) Shivs in hand, one CreateInHand call each"],
  kreq=[K_SHIV], cards=["Shiv"], tier_own=2, batch=3,
  notes="Each Shiv is its own AddGeneratedCardsToCombat call (History.CardGenerated + AfterCardGeneratedForCombat per card); "
        "a full hand (10) sends the Shiv to the discard pile (Commands/CardPileCmd.cs:3405).")
c("BladeOfInk", ["create 2 (+1) Shivs in hand (one batch)", "enchant each with Inky 1"],
  kreq=[K_SHIV, "CardCmd.Enchant<T> (Commands/CardCmd.cs:2146) and the enchantment subsystem"],
  cards=["Shiv"], ench=["Inky"], tier_own=3, batch=8,
  notes="Inky: +1 powered-attack damage (EnchantDamageAdditive) and Weak 1 to the target (or all on AllEnemies Shivs) "
        "after the play (EnchantmentModel.OnPlay). Silent7Epoch-gated.")
c("BladeSymphony", ["each alive teammate player gets 2 Shivs", "upgrade: cost -1"],
  kreq=[K_MP, K_SHIV], cards=["Shiv"], tier_own=2, batch=99,
  notes="New in v0.111.0; MultiplayerOnly, TargetType AllAllies. Out of single-player scope.")
c("Blur", ["block 5 (+3)", "apply BlurPower 1: block is not cleared next turn"], powers=["BlurPower"], batch=2,
  notes="BlurPower: ShouldClearBlock false for owner; decremented at the owner's side turn start.")
c("BouncingFlask", ["apply Poison 3 to a random hittable enemy, 3 (+1) times"],
  powers=["PoisonPower"],
  rng_=[rng("CombatTargets", "NextItem(HittableEnemies) once per bounce; skipped when no hittable enemy",
            cite="Models.Cards/BouncingFlask.cs:138")],
  batch=2,
  notes="TargetType RandomEnemy (no player target). Rng.NextItem draws even for a single enemy and not for none. "
        "Throw/splash VFX and Cmd.Wait(0.5) are inside TestMode.IsOff (strip).")
c("BubbleBubble", ["apply Poison 9 (+3) to target only if it already has Poison"], powers=["PoisonPower"], batch=2,
  notes="ShouldGlowGoldInternal is UI. Silent2Epoch-gated.")
c("BulletTime", ["every non-X card in hand costs 0 this turn", "apply NoDrawPower"],
  kreq=["CardModel.SetToFreeThisTurn (present in sim CardInstance)"], powers=["NoDrawPower"], batch=2,
  notes="3 cost (-1 on upgrade). NoDrawPower blocks non-hand-draw draws until the owner's turn end.")
c("Burst", ["apply BurstPower 1 (+1): next Skill(s) this turn play twice"], powers=["BurstPower"], batch=2,
  notes="BurstPower.ModifyCardPlayCount +1 for Skills, decremented per modified play, removed at turn end.")
c("CalculatedGamble", ["Exhaust", "discard the whole hand, then draw that many", "upgrade: gain Retain"],
  kreq=["CardCmd.DiscardAndDraw (Commands/CardCmd.cs:1839)", K_SLY], tier_own=2, batch=3,
  notes="0 cost. Hand snapshot at DiscardAndDraw; this card is in the play pile so it is not discarded. "
        "Sly cards among the discards auto-play after the draw.")
c("CloakAndDagger", ["block 6", "create 1 (+1) Shiv(s), one CreateInHand call each"],
  kreq=[K_SHIV], cards=["Shiv"], tier_own=2, batch=3)
c("Concoct", ["apply ConcoctPower 3 (+1) to an ally"], kreq=[K_MP], powers=["ConcoctPower"], batch=99,
  notes="New in v0.111.0; MultiplayerOnly, TargetType AnyAlly.")
c("CorrosiveWave", ["apply CorrosiveWavePower 2 (+1): this turn each card you draw poisons all enemies"],
  powers=["CorrosiveWavePower", "PoisonPower"], batch=2,
  notes="CorrosiveWavePower.AfterCardDrawn (owner's cards) applies Poison Amount to all hittable enemies (list overload); "
        "removed at the owner's side turn end.")
c("DaggerSpray", ["damage 4 (+2) to all enemies, 2 hits"], batch=1,
  notes="WithHitCount(2) + TargetingAllOpponents (ported). The BeforeDamage delegate is VFX only.")
c("DaggerThrow", ["damage 9 (+3) to target", "draw 1", "discard 1 card from hand (choice)"],
  kreq=["CardSelectCmd.FromHandForDiscard", K_DISCARD], prompts=[discard_prompt(1)], tier_own=3, batch=5)
c("Dash", ["block 10 (+3)", "damage 10 (+3) to target"], batch=1, notes="Block is gained before the attack.")
c("EchoingSlash", ["damage 10 (+3) to all hittable enemies; repeat once more per enemy killed"],
  kreq=["AttackContext (AttackCommand.CreateContextAsync: BeforeAttack at creation, AfterAttack at dispose; "
        "Commands.Builders/AttackCommand.cs:953)",
        "CreatureCmd.Damage(targets, DamageVar, dealer, cardSource, cardPlay) overload"],
  tier_own=2, batch=3,
  notes="Loop: attackCount=1; while >0: decrement, damage all hittable, AddHit, add kills. Hits are counted on one "
        "AttackCommand so AfterAttack fires once. Rarity Rare->Uncommon in v0.111.0.")
c("Envenom", ["apply EnvenomPower 1 (+1): unblocked powered attack damage applies Poison"],
  powers=["EnvenomPower", "PoisonPower"], batch=2,
  notes="EnvenomPower.AfterDamageGiven: dealer is owner, props.IsPoweredAttack, UnblockedDamage > 0 -> Poison Amount.")
c("EscapePlan", ["draw 1", "if the drawn card is a Skill: block 3 (+2)"], batch=1,
  notes="0 cost. Uses the card returned by CardPileCmd.Draw; nothing happens if no card was drawn.")
c("Expertise", ["draw 2 (+1)", "the drawn cards gain Retain this turn"],
  kreq=["CardCmd.ApplySingleTurnRetain (Commands/CardCmd.cs:2302; sim has CardInstance.GiveSingleTurnRetain)"],
  tier_own=2, batch=3,
  notes="Reworked in v0.111.0 (was: draw until you hold 6 (+1) cards).")
c("Expose", ["Exhaust", "remove all block from target", "remove Artifact from target", "apply Vulnerable 2 (+1)"],
  kreq=["CreatureCmd.LoseBlock with remover (ported)", "PowerCmd.Remove<T> (ported)"],
  powers=["VulnerablePower", "ArtifactPower"], batch=2,
  notes="0 cost. Order: LoseBlock(target.Block), Remove<ArtifactPower> if present, then Vulnerable (so Artifact cannot negate it).")
c("Fade", ["Retain", "apply FadePower (temporary Dexterity) 6 (+3) to an ally"], kreq=[K_MP],
  powers=["FadePower"], batch=99, notes="New in v0.111.0; MultiplayerOnly, TargetType AnyAlly.")
c("FanOfKnives", ["apply FanOfKnivesPower: Shivs hit all enemies", "create 4 (+1) Shivs, one call each"],
  kreq=[K_SHIV], powers=["FanOfKnivesPower"], cards=["Shiv"], tier_own=2, batch=3,
  notes="FanOfKnivesPower has no hooks; Shiv reads it for TargetType (AllEnemies) and TargetingAllOpponents in OnPlay. "
        "OnEnqueuePlayVfx override is UI.")
c("Finisher", ["damage 6 (+2) to target x (attacks you played this turn)"],
  kreq=[K_HIST_PLAYS, K_CALC], tier_own=3, batch=4,
  notes="CalculatedHits counts finished Attack plays by this player this turn (this card is not finished yet). "
        "With 0 earlier attacks the attack still runs with 0 hits: BeforeAttack, History.CreatureAttacked (no results) and "
        "AfterAttack fire (AttackCommand.Execute; Hook.ModifyAttackHitCount may raise the count).")
c("Flanking", ["apply FlankingPower 2 to target", "upgrade: cost -1"], kreq=[K_MP], powers=["FlankingPower"], batch=99,
  notes="MultiplayerOnly; rarity Uncommon->Rare in v0.111.0.")
c("Flechettes", ["damage 5 (+2) to target x (Skills in hand)"], kreq=[K_CALC], tier_own=2, batch=3,
  notes="Hit count = Skills in hand at play time (this card is in the play pile).")
c("FlickFlack", ["Sly", "damage 7 (+2) to all enemies"], kreq=[K_SLY], tier_own=2, batch=3,
  notes="Damage 6->7 in v0.111.0.")
c("Footwork", ["apply Dexterity 2 (+1)"], powers=["DexterityPower"], batch=1)
c("GrandFinale", ["playable only with an empty draw pile", "damage 60 (+15) to all enemies"],
  kreq=["CardDef.IsPlayable override (present in sim)"], batch=1, notes="0 cost. Glow is UI.")
c("HandTrick", ["block 7 (+3)", "a chosen Skill in hand (not already Sly) gains Sly this turn (choice)"],
  kreq=["CardSelectCmd.FromHand", "CardCmd.ApplySingleTurnSly (Commands/CardCmd.cs:2296; sim has GiveSingleTurnSly)"],
  prompts=[prompt("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, key="SelectionScreenPrompt",
                  filt="card.Type == Skill && !card.IsSlyThisTurn", cite="Commands/CardSelectCmd.cs:3668")],
  tier_own=3, batch=5, notes="Silent5Epoch-gated.")
c("Haze", ["apply Poison 4 (+2) to all hittable enemies", "apply Weak 1 (+1) to all hittable enemies"],
  powers=["PoisonPower", "WeakPower"], batch=2,
  notes="Reworked in v0.111.0: cost 3->2, Sly removed, Weak added; both use the PowerCmd.Apply list overload (ported).")
c("HiddenDaggers", ["discard 2 cards (choice)", "create 2 Shivs (one batch)", "if upgraded: upgrade those Shivs"],
  kreq=["CardSelectCmd.FromHandForDiscard", K_DISCARD, K_SHIV, K_UPGRADE],
  cards=["Shiv"], prompts=[discard_prompt(2)], tier_own=3, batch=5,
  notes="0 cost; no OnUpgrade override (IsUpgraded only gates the Shiv upgrade). Sly auto-plays happen inside the "
        "discard, before the Shivs are created. Silent7Epoch-gated.")
c("InfiniteBlades", ["apply InfiniteBladesPower: a Shiv at the start of each turn", "upgrade: gain Innate"],
  kreq=[K_SHIV], powers=["InfiniteBladesPower"], cards=["Shiv"], batch=3,
  notes="InfiniteBladesPower.BeforeHandDraw creates Amount Shivs (before the turn draw).")
c("KnifeTrap", ["auto-play every Shiv in your exhaust pile at the target", "if upgraded: upgrade them first"],
  kreq=[K_AUTOPLAY, K_UPGRADE, K_CALC], cards=["Shiv"], tier_own=2, batch=3,
  notes="Snapshot of Shiv-tagged exhaust cards; each is upgraded (if KnifeTrap is) then AutoPlay(target) and exhausts "
        "again. No OnUpgrade override. The CalculatedShivs var is display only. If the target dies mid-loop the remaining "
        "Shivs keep the dead target: AttackCommand filters out dead targets and breaks the hit loop (BeforeAttack/AfterAttack "
        "still fire), and each Shiv still exhausts.")
c("LeadingStrike", ["damage 3 (+3) to target", "create 2 Shivs, one call each"],
  kreq=[K_SHIV], cards=["Shiv"], tier_own=2, batch=3, notes="CardTag.Strike.")
c("LegSweep", ["block 11 (+3)", "apply Weak 2 (+1) to target"], powers=["WeakPower"], batch=1)
c("Malaise", ["Exhaust", "X cost: target loses X (+1 upgraded) Strength", "apply Weak X (+1 upgraded)"],
  kreq=[K_X], powers=["StrengthPower", "WeakPower"], batch=1,
  notes="No OnUpgrade override (IsUpgraded adds 1). Strength (negative) is applied before Weak.")
c("MasterPlanner", ["apply MasterPlannerPower: Skills you play gain Sly", "upgrade: cost -1"],
  kreq=["CardCmd.ApplyKeyword (Commands/CardCmd.cs:2278)", K_SLY], powers=["MasterPlannerPower"], batch=3,
  notes="MasterPlannerPower.AfterCardPlayed adds the Sly keyword permanently (combat copy). Silent5Epoch-gated.")
c("MementoMori", ["damage 9 (+2) + 4 (+1) x (cards you discarded this turn) to target"],
  kreq=["CombatHistory.CardDiscardedEntry (HappenedThisTurn, Actor == owner)", K_CALC, K_CALC_DMG],
  tier_own=3, batch=4)
c("Mirage", ["Exhaust (upgrade removes)", "block = total Poison on alive enemies"],
  kreq=[K_CALC, "CalculatedBlockVar -> CreatureCmd.GainBlock(amount, props, cardPlay)"], powers=["PoisonPower"],
  tier_own=2, batch=3, notes="Upgrade changed in v0.111.0 (was cost -1).")
c("Murder", ["damage 1 + 1 x (cards you drew this combat) to target", "upgrade: cost -1"],
  kreq=["CombatHistory.CardDrawnEntry (whole combat, Actor == owner)", K_CALC, K_CALC_DMG], tier_own=3, batch=4)
c("Nightmare", ["Exhaust", "choose a card in hand; next turn add 3 copies of it to hand", "upgrade: cost -1"],
  kreq=["CardSelectCmd.FromHand", "CardModel.CreateClone", "CardCmd.ClearAffliction",
        "PowerInstanceType.Instanced powers (one NightmarePower per play)"],
  powers=["NightmarePower"],
  prompts=[prompt("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, key="SelectionScreenPrompt",
                  cite="Commands/CardSelectCmd.cs:3668")],
  tier_own=3, batch=5,
  notes="The clone is taken at selection time (SetSelectedCard: CreateClone, ClearAffliction); NightmarePower."
        "BeforeHandDraw adds Amount clones of that snapshot. CanBeGeneratedInCombat=false is new in v0.111.0.")
c("NoxiousFumes", ["apply NoxiousFumesPower 2 (+1): Poison all enemies at the start of your turn"],
  powers=["NoxiousFumesPower", "PoisonPower"], batch=2)
c("Outbreak", ["apply Poison 9 (+3) to each hittable enemy", "then trigger each hittable enemy's Poison once"],
  kreq=["PoisonPower.Trigger() public entry (Models.Powers/PoisonPower.cs:272)"], powers=["PoisonPower"], batch=2,
  notes="Reworked in v0.111.0 (was a 1-cost Power applying OutbreakPower). Trigger runs the full tick "
        "(TriggerCount iterations incl. Accelerant, Decrement while alive), outside the enemy turn.")
c("PhantomBlades", ["apply PhantomBladesPower 9 (+3): Shivs gain Retain; the first Shiv each turn deals +N"],
  kreq=["CardCmd.ApplyKeyword", K_HIST_PLAYS], powers=["PhantomBladesPower"], cards=["Shiv"], batch=4,
  notes="CanonicalTags overridden with an empty set. The power counts Shiv plays finished this turn (history). "
        "Silent7Epoch-gated.")
c("PiercingWail", ["Exhaust", "each hittable enemy loses 6 (+2) Strength this turn"],
  kreq=[K_TMPSTR], powers=["PiercingWailPower"], batch=2,
  notes="Applies PiercingWailPower (negative TemporaryStrengthPower) per enemy.")
c("Pinpoint", ["damage 15 (+4) to target", "costs 1 less this turn per Skill played this turn"],
  kreq=[K_LISTENER, K_HIST_PLAYS, "CardEnergyCost.AddThisTurn (present in sim)"], tier_own=3, batch=4,
  notes="Card-level hook listener: AfterCardPlayed (own Skill) -> AddThisTurn(-1); AfterCardEnteredCombat "
        "-> AddThisTurn(-skills already played this turn) unless IsClone (CloneOf set by CardModel.CreateClone/CreateDupe, "
        "Models/CardModel.cs:2073, :3571, :3597).")
c("Pounce", ["damage 14 (+6) to target", "apply FreeSkillPower 1: next Skill costs 0"], powers=["FreeSkillPower"],
  batch=2, notes="FreeSkillPower: TryModifyEnergyCostInCombatLate for Skills in Hand/Play, decremented in BeforeCardPlayed.")
c("PreciseCut", ["damage 13 (+3) - 2 x (other cards in hand) to target"], kreq=[K_CALC, K_CALC_DMG],
  tier_own=2, batch=3, notes="Multiplier is -(hand count minus itself if in hand).")
c("Predator", ["damage 15 (+5) to target", "apply DrawCardsNextTurnPower 2"], powers=["DrawCardsNextTurnPower"], batch=2)
c("Prepared", ["draw 1 (+1)", "discard 1 (+1) cards (choice)"],
  kreq=["CardSelectCmd.FromHandForDiscard", K_DISCARD], prompts=[discard_prompt("Cards (1, +1)")], tier_own=3, batch=5,
  notes="0 cost. Draw count and discard count both come from the Cards var.")
c("Reflex", ["Sly", "draw 2 (+1)"], kreq=[K_SLY], tier_own=2, batch=3, notes="3 cost. Silent5Epoch-gated.")
c("Ricochet", ["Sly", "damage 3 to random enemies, 4 (+1) hits"], kreq=[K_SLY],
  rng_=[rng("CombatTargets", "AttackCommand.TargetingRandomOpponents picks a target per hit")],
  tier_own=2, batch=3)
c("SerpentForm", ["apply SerpentFormPower 4 (+2): each card you play deals N to a random enemy"],
  powers=["SerpentFormPower"],
  rng_=[rng("CombatTargets", "SerpentFormPower.AfterCardPlayed NextItem(HittableEnemies) per card", site="power:SerpentFormPower")],
  batch=2)
c("Shadowmeld", ["apply ShadowmeldPower 1: block gained this turn x2^Amount", "upgrade: cost -1"],
  powers=["ShadowmeldPower"], batch=2)
c("ShadowStep", ["discard your whole hand", "apply ShadowStepPower: Double Damage next turn", "upgrade: cost -1"],
  kreq=[K_DISCARD, K_SLY], powers=["ShadowStepPower", "DoubleDamagePower"], tier_own=2, batch=3,
  notes="CardsVar(3) is declared but OnPlay does not read it. ShadowStepPower applies DoubleDamagePower at the next side turn start.")
c("Sidestep", ["apply EnergyNextTurnPower 1 (+1)"], powers=["EnergyNextTurnPower"], batch=2,
  notes="New card in v0.111.0 (no v0.107.1 file). 0 cost.")
c("Skewer", ["X cost: damage 8 (+3) to target, X hits"], kreq=[K_X], batch=1)
c("Sneaky", ["Sly", "apply SneakyPower 1 (+1)"], kreq=[K_MP, K_SLY], powers=["SneakyPower"], tier_own=2, batch=99,
  notes="MultiplayerOnly: SneakyPower gives block when an ally plays an Attack.")
c("Speedster", ["apply SpeedsterPower 2: each non-hand-draw draw on your turn deals 2 to all enemies", "upgrade: gain Innate"],
  powers=["SpeedsterPower"], batch=2)
c("StormOfSteel", ["discard your whole hand", "create that many Shivs (one batch)", "if upgraded: upgrade them"],
  kreq=[K_DISCARD, K_SLY, K_SHIV, K_UPGRADE], cards=["Shiv"], tier_own=2, batch=3,
  notes="No OnUpgrade override. Shiv count = hand size before the discard (Sly auto-plays do not change it).")
c("Strangle", ["damage 8 (+2) to target", "apply StranglePower 2 (+1): target loses N HP per card you play this turn"],
  powers=["StranglePower"], batch=2,
  notes="StranglePower is InstancedPerApplier and removed at the owner's (enemy's) side turn end.")
c("SuckerPunch", ["damage 8 (+2) to target", "apply Weak 1 (+1) to target"], powers=["WeakPower"], batch=1)
c("Suppress", ["Innate", "damage 11 (+6) to target", "apply Weak 3 (+2)"], powers=["WeakPower"], batch=1,
  notes="Ancient rarity (offered by ancients, not by card rewards).")
c("Tactician", ["Sly", "gain 1 (+1) energy"], kreq=[K_SLY], tier_own=2, batch=3, notes="3 cost.")
c("TheHunt", ["Exhaust", "damage 10 (+5) to target",
              "on a fatal kill in a combat room: extra 3-card reward + TheHuntPower marker"],
  kreq=[K_RESULTS, "PowerModel.ShouldOwnerDeathTriggerFatal", "CombatRoom.AddExtraReward + CardReward("
        "CardCreationOptions.ForRoom, 3) (run layer)", "RunState.CurrentRoom"],
  powers=["TheHuntPower"], tier_own=4, batch=8,
  notes="Cross-cutting: the reward is a run-layer effect recorded from inside combat. TheHuntPower has no hooks and no "
        "readers (display marker). CanBeGeneratedInCombat=false. Fatal check is taken before the attack.")
c("ToolsOfTheTrade", ["apply ToolsOfTheTradePower: +1 draw each turn, then discard 1 (choice)", "upgrade: cost -1"],
  kreq=["CardSelectCmd.FromHandForDiscard at AfterPlayerTurnStart (turn-setup deferral, design-combat-kernel §4.6)",
        K_DISCARD],
  powers=["ToolsOfTheTradePower"],
  prompts=[discard_prompt("Amount", site="power:ToolsOfTheTradePower.AfterPlayerTurnStart",
                          ctx="HookPlayerChoiceContext from SetupPlayerTurn (turn-setup deferral)")],
  batch=5,
  notes="The prompt lives in the power, every turn after the draw.")
c("Tracking", ["apply TrackingPower 50: your card attacks on Weak enemies deal x1.5 (stacks additively)",
               "upgrade: cost -1"],
  powers=["TrackingPower", "WeakPower"], batch=2,
  notes="v0.111.0: TrackingPower multiplies powered card attacks by owner/pets vs Weak targets by 1 + Amount/100; "
        "the card always applies 50 (v0.107.1 applied 2 first, 1 after).")
c("Untouchable", ["Sly", "block 6 (+3)"], kreq=[K_SLY], tier_own=2, batch=3, notes="2 cost.")
c("UpMySleeve", ["create 3 (+1) Shivs, one call each", "this card costs 1 less for the rest of combat"],
  kreq=[K_SHIV, "CardEnergyCost.AddThisCombat (present in sim)", "per-card mutable field TimesPlayedThisCombat (clone + hash)"],
  cards=["Shiv"], tier_own=2, batch=3,
  notes="TimesPlayedThisCombat is written but not read by rules in this file.")
c("WellLaidPlans", ["apply WellLaidPlansPower: your hand is not discarded at end of turn", "upgrade: cost -1"],
  kreq=["Hook.ShouldFlush (present in sim)"], powers=["WellLaidPlansPower"], batch=2,
  notes="Reworked in v0.111.0: Rare 2-cost, whole-hand retain via ShouldFlush=false; v0.107.1 was Uncommon 1-cost "
        "'retain N chosen cards' with a FromHand prompt and SingleplayerOnly.")
c("WraithForm", ["apply Intangible 2 (+1)", "apply WraithFormPower 1: lose 1 Dexterity each turn"],
  powers=["IntangiblePower", "WraithFormPower", "DexterityPower"], batch=2, notes="Ancient rarity.")
