from data_common import *  # noqa: F401,F403

D = {}


def c(cls, mech, *, kreq=(), powers=(), cards=(), orbs=(), ench=(), prompts=(), rng_=(), tier_own=1, batch=1,
      notes="", oq=()):
    D[cls] = {
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


ORB_CORE = [K_ORBQ, K_CHANNEL, K_ORBVAL]

c("StrikeDefect", ["damage 6 (+3) to target"], batch=1, notes="Starter (x4). CardTag.Strike.")
c("DefendDefect", ["block 5 (+3)"], batch=1, notes="Starter (x4). CardTag.Defend.")
c("Zap", ["channel 1 Lightning", "upgrade: cost -1"], kreq=ORB_CORE, orbs=["LightningOrb"], tier_own=3, batch=6,
  notes="Defect starter: a Defect run needs the orb subsystem from turn 1 (CrackedCore also channels Lightning).")
c("Dualcast", ["evoke the front orb twice (first evoke keeps it, second removes it)", "upgrade: cost -1"],
  kreq=[K_ORBQ, K_EVOKE, "OrbCmd.EvokeNext(dequeue: false)"], tier_own=3, batch=6,
  notes="Defect starter. No-op with an empty queue. OrbEvokeType.Front is UI-only (hover tip and orb highlight, "
        "Nodes.Orbs/NOrbManager.cs:304).")
c("AdaptiveStrike", ["damage 18 (+5) to target", "add a 0-cost (this combat) copy of this card to the discard pile"],
  kreq=["CardModel.CreateClone (copies upgrade state)", "CardEnergyCost.SetThisCombat (present in sim)",
        "CardPileCmd.AddGeneratedCardToCombat (ported)"],
  tier_own=2, batch=3, notes="CardTag.Strike. The clone is generated (History.CardGenerated, AfterCardGeneratedForCombat).")
c("AllForOne", ["damage 10 (+4) to target", "return every 0-cost Attack/Skill/Power from discard to hand"],
  kreq=["CardPileCmd.Add(card, Hand) for an existing combat card (full hand redirects to discard, Commands/CardPileCmd.cs:3405)",
        "CardEnergyCost.GetWithModifiers(CostModifiers.All)"],
  batch=1,
  notes="Snapshot of the discard pile filtered by cost 0 with all modifiers, not X cost, and type Attack, Skill or Power.")
c("BallLightning", ["damage 7 (+3) to target", "channel 1 Lightning"], kreq=ORB_CORE, orbs=["LightningOrb"],
  tier_own=3, batch=6)
c("Barrage", ["damage 5 (+2) to target x (orbs in queue)"], kreq=[K_CALC, K_ORBQ], tier_own=3, batch=6,
  notes="Hit count = OrbQueue.Orbs.Count at play time; 0 orbs -> 0 hits. Defect5Epoch-gated.")
c("BeamCell", ["damage 3 (+1) to target", "apply Vulnerable 1 (+1)"], powers=["VulnerablePower"], batch=1,
  notes="0 cost.")
c("BiasedCognition", ["gain Focus 5 (+1)", "apply BiasedCognitionPower 1: lose 1 Focus each turn"],
  kreq=[K_FOCUS, K_ORBVAL], powers=["FocusPower", "BiasedCognitionPower"], tier_own=1, batch=6,
  notes="Ancient rarity. Focus 4->5 in v0.111.0. Order: Focus first, then BiasedCognitionPower.")
c("BoostAway", ["block 6 (+3)", "add a Dazed to the discard pile"], kreq=[K_STATUS_GEN], cards=["Dazed"], batch=2,
  notes="0 cost. Dazed is a status (Ethereal, Unplayable).")
c("BootSequence", ["Innate", "Exhaust", "block 10 (+3)"], batch=1, notes="0 cost.")
c("Buffer", ["apply BufferPower 1 (+1): prevent the next HP loss"], powers=["BufferPower"], batch=2,
  notes="BufferPower.ModifyHpLostAfterOstyLate returns 0 for the owner, then decrements.")
c("BulkUp", ["lose 1 orb slot", "gain Strength 2 (+1)", "gain Dexterity 2 (+1)"],
  kreq=[K_ORBQ, "OrbCmd.RemoveSlots (Commands/OrbCmd.cs:659: removes capacity from the back; orbs in removed slots are dropped without evoke)"],
  powers=["StrengthPower", "DexterityPower"], tier_own=3, batch=7)
c("Capacitor", ["gain 2 (+1) orb slots"], kreq=[K_ORBQ, "OrbCmd.AddSlots (Commands/OrbCmd.cs:647, capacity capped at 10)"],
  tier_own=3, batch=7)
c("Chaos", ["channel 1 (+1) random orb (Lightning/Frost/Dark/Plasma/Glass)"],
  kreq=ORB_CORE + ["OrbModel.GetRandomOrb over 5 ids incl. Glass (Models/OrbModel.cs:402)"],
  orbs=["LightningOrb", "FrostOrb", "DarkOrb", "PlasmaOrb", "GlassOrb"],
  rng_=[rng("CombatOrbGeneration", "OrbModel.GetRandomOrb -> NextItem(_validOrbs) once per channel")],
  tier_own=3, batch=7, notes="GetRandomOrb includes GlassOrb although ModelDb.Orbs lists only 4.")
c("ChargeBattery", ["block 7 (+3)", "apply EnergyNextTurnPower 1"], powers=["EnergyNextTurnPower"], batch=2)
c("Chill", ["Exhaust (upgrade removes)", "channel 1 Frost per hittable enemy"], kreq=ORB_CORE, orbs=["FrostOrb"],
  tier_own=3, batch=6, notes="0 cost. Count is the HittableEnemies snapshot at play time.")
c("Claw", ["damage 3 (+1) to target", "every Claw in combat gains +2 (+1) damage"],
  kreq=[K_ALLCARDS, "CardModel.AfterDowngraded (re-applies the bonus)", "per-card mutable field ExtraDamageFromClawPlays (clone + hash)"],
  tier_own=2, batch=3,
  notes="0 cost. The bonus uses the playing Claw's Increase var and applies to all Claw instances in combat piles "
        "(including itself). Combat copies only; nothing reaches the deck.")
c("ColdSnap", ["damage 6 (+3) to target", "channel 1 Frost"], kreq=ORB_CORE, orbs=["FrostOrb"], tier_own=3, batch=6)
c("Compact", ["block 6 (+1)", "transform every transformable Status in hand into Fuel (upgraded if Compact is)"],
  kreq=["CardCmd.Transform(IEnumerable<CardTransformation>, rng: null) (Commands/CardCmd.cs:1988)", K_UPGRADE],
  cards=["Fuel"], tier_own=2, batch=3,
  notes="Block first. v0.111.0 batch (Commands/CardCmd.cs:1988-2145): remove every original, then per original sorted by "
        "(pile type, index) insert the Fuel at the old index with History.CardGenerated, AfterCardEnteredCombat, "
        "AfterCardChangedPiles; afterwards AfterCardGeneratedForCombat per Fuel and RemoveFromState per original. v0.107.1 "
        "transformed one card at a time. Fuel is a Token Skill, so Status-generation listeners do not fire.")
c("CompileDriver", ["damage 7 (+3) to target", "draw 1 per distinct orb type in queue"], kreq=[K_CALC, K_ORBQ],
  tier_own=3, batch=6, notes="Distinct = GroupBy(orb.Id).Count at play time.")
c("ConsumingShadow", ["channel 2 (+1) Dark", "apply ConsumingShadowPower 1: evoke your last orb at end of turn"],
  kreq=ORB_CORE + ["OrbCmd.EvokeLast (Commands/OrbCmd.cs:714)"], powers=["ConsumingShadowPower"], orbs=["DarkOrb"],
  tier_own=3, batch=7, notes="Defect2Epoch-gated.")
c("Coolant", ["apply CoolantPower 2 (+1): at turn start gain N block per distinct orb type"],
  kreq=[K_ORBQ], powers=["CoolantPower"], batch=7)
c("Coolheaded", ["channel 1 Frost", "draw 1 (+1)"], kreq=ORB_CORE, orbs=["FrostOrb"], tier_own=3, batch=6)
c("CreativeAi", ["apply CreativeAiPower 1: add a random Power card to hand each turn", "upgrade: cost -1"],
  kreq=["CardFactory.GetDistinctForCombat (Factories/CardFactory.cs:93-102)",
        "CardPoolModel.GetUnlockedCards(UnlockState, SingleplayerOnly) (Models/CardPoolModel.cs:68)", "UnlockState input"],
  powers=["CreativeAiPower"],
  rng_=[rng("CombatCardGeneration", "CreativeAiPower.BeforeHandDraw: GetDistinctForCombat TakeRandom(1) per Amount",
            site="power:CreativeAiPower")],
  batch=8)
c("Darkness", ["channel 1 Dark", "trigger the passive of every Dark orb 1 (2 if upgraded) time(s)"],
  kreq=ORB_CORE + [K_PASSIVE], orbs=["DarkOrb"], tier_own=3, batch=7,
  notes="No OnUpgrade override (IsUpgraded sets the trigger count). Dark passive adds its (focused) passive value to "
        "its own evoke value.")
c("Defragment", ["gain Focus 1 (+1)"], kreq=[K_FOCUS, K_ORBVAL], powers=["FocusPower"], batch=6)
c("DoubleEnergy", ["Exhaust", "gain energy equal to your current energy", "upgrade: cost -1"], batch=1,
  notes="Reads PlayerCombatState.Energy after this card's cost was paid.")
c("EchoForm", ["Ethereal (upgrade removes)", "apply EchoFormPower 1: the first N cards each turn are played twice"],
  kreq=["CombatHistory.CardPlaysStarted (IsFirstInSeries, HappenedThisTurn)", "Hook.ModifyCardPlayCount (present in sim)"],
  powers=["EchoFormPower"], batch=4)
c("EnergySurge", ["Exhaust", "every alive teammate player gains 2 (+1) energy"], kreq=[K_MP], batch=99,
  notes="MultiplayerOnly, TargetType AllAllies.")
c("Feral", ["apply FeralPower 1: the first N 0-energy Attacks each turn return to the top of your hand", "upgrade: cost -1"],
  kreq=["Hook.ModifyCardPlayResultLocation + AfterModifyingCardPlayResultLocation (missing in sim)"],
  powers=["FeralPower"], batch=3,
  notes="v0.111.0 FeralPower checks ResourceInfo.EnergyValue == 0 and !IsDupe, counts per turn in internal data.")
c("FightThrough", ["block 13 (+4)", "add 2 Wounds to the discard pile"], kreq=[K_STATUS_GEN], cards=["Wound"], batch=2)
c("FlakCannon", ["exhaust every Status card not already exhausted", "damage 8 (+3) to random enemies, once per Status"],
  kreq=[K_CALC, K_ALLCARDS, "CardCmd.Exhaust (ported)", "AttackCommand.TargetingRandomOpponents (ported)"],
  rng_=[rng("CombatTargets", "AttackCommand.TargetingRandomOpponents per hit")],
  tier_own=2, batch=3,
  notes="Status count is taken before exhausting (draw, hand, discard and play piles). Defect5Epoch-gated.")
c("FocusedStrike", ["damage 9 (+2) to target", "gain Focus 1 (+1) this turn"],
  kreq=[K_TMPFOCUS, K_ORBVAL], powers=["FocusedStrikePower", "FocusPower"], batch=6, notes="CardTag.Strike.")
c("Ftl", ["damage 5 (+1) to target", "draw 1 if you played fewer than 3 (+1) cards this turn"],
  kreq=[K_HIST_PLAYS], tier_own=3, batch=4,
  notes="0 cost. The count excludes this card (not finished yet). Glow is UI.")
c("Fusion", ["Exhaust (upgrade removes)", "channel 1 Plasma"], kreq=ORB_CORE, orbs=["PlasmaOrb"], tier_own=3, batch=6)
c("GeneticAlgorithm", ["Exhaust", "block = CurrentBlock (starts at 1)",
                       "permanently +3 (+1) block on this card and its deck version"],
  kreq=["CardModel.DeckVersion link (deck mutation from combat; sim plans CardInstance.DeckVersionId + RunEffects)",
        "[SavedProperty] CurrentBlock / IncreasedBlock (save import/export)", "CardModel.AfterDowngraded"],
  tier_own=4, batch=8,
  notes="Cross-cutting: grows across combats through the deck card. CurrentBlock = 1 + IncreasedBlock and also "
        "rewrites the Block var base value.")
c("Glacier", ["block 6 (+3)", "channel 2 Frost"], kreq=ORB_CORE, orbs=["FrostOrb"], tier_own=3, batch=6)
c("Glasswork", ["block 5 (+3)", "channel 1 Glass"], kreq=ORB_CORE, orbs=["GlassOrb"], tier_own=3, batch=6)
c("GoForTheEyes", ["damage 3 (+1) to target", "if the target intends to attack: apply Weak 1 (+1)"],
  kreq=["MonsterModel.IntendsToAttack (current intent)"], powers=["WeakPower"], batch=1,
  notes="0 cost. Intent is read after the attack resolves. Glow is UI.")
c("GunkUp", ["damage 4 (+1) to target, 3 hits", "add a Slimed to the discard pile"], kreq=[K_STATUS_GEN],
  cards=["Slimed"], batch=1, notes="Slimed is already ported.")
c("Hailstorm", ["apply HailstormPower 6 (+2): end of turn, with a Frost orb, deal N to all enemies"],
  kreq=[K_ORBQ], powers=["HailstormPower"], batch=7,
  notes="HailstormPower.BeforeSideTurnEnd needs >= 1 FrostOrb (FrostOrbs var = 1).")
c("HelixDrill", ["damage 3 (+2) to target x (energy you spent this turn)"],
  kreq=["CombatHistory.EnergySpentEntry (HappenedThisTurn, sum of Amount)", K_CALC], tier_own=3, batch=4,
  notes="0 cost. Subtracts this card's own cost while it is in the play pile. Defect7Epoch-gated.")
c("Hibernate", ["apply HibernatePower 1", "channel 2 (+1) Frost (Frost evokes also shield teammates)"],
  kreq=[K_MP] + ORB_CORE, powers=["HibernatePower"], orbs=["FrostOrb"], tier_own=3, batch=99,
  notes="New in v0.111.0; MultiplayerOnly. FrostOrb.Evoke grants block to other players while the owner has HibernatePower.")
c("Hologram", ["block 3 (+2)", "put a chosen card from the discard pile into your hand", "Exhaust (upgrade removes)"],
  kreq=["CardSelectCmd.FromCombatPile (Discard)", "CardPileCmd.Add(card, Hand)"],
  prompts=[prompt("PileSelect", "CardSelectCmd.FromCombatPile", "Discard", 1, 1, key="SelectionScreenPrompt",
                  cite="Commands/CardSelectCmd.cs:3353", answer="cards from the discard pile in pile order")],
  tier_own=3, batch=5)
c("Hotfix", ["Exhaust (upgrade removes)", "gain Focus 2 this turn"], kreq=[K_TMPFOCUS, K_ORBVAL],
  powers=["HotfixPower", "FocusPower"], batch=6, notes="0 cost.")
c("Hyperbeam", ["damage 24 (+6) to all enemies", "lose 3 Focus this turn"],
  kreq=[K_TMPFOCUS, K_ORBVAL], powers=["HyperbeamFocusDownPower", "FocusPower"], batch=6,
  notes="v0.111.0: the Focus loss is temporary (HyperbeamFocusDownPower, IsPositive=false); v0.107.1 was a permanent "
        "-3 FocusPower and 28 (+8) damage. BeforeDamage lambda is VFX only.")
c("IceLance", ["damage 19 (+5) to target", "channel 3 Frost"], kreq=ORB_CORE, orbs=["FrostOrb"], tier_own=3, batch=6)
c("Ignition", ["Exhaust (upgrade removes)", "an ally channels 1 Plasma"], kreq=[K_MP] + ORB_CORE, orbs=["PlasmaOrb"],
  tier_own=3, batch=99, notes="MultiplayerOnly, TargetType AnyAlly; rarity Rare->Uncommon in v0.111.0.")
c("ImitationLearning", ["Exhaust", "apply/extend ImitationLearningPower 2 (+1) targeting an ally"],
  kreq=[K_MP, "PowerCmd.ModifyAmount (ported)"], powers=["ImitationLearningPower"], tier_own=2, batch=99,
  notes="New in v0.111.0; MultiplayerOnly. The power auto-plays copies (CardCmd.AutoPlay).")
c("Iteration", ["apply IterationPower 2 (+1): the first Status drawn each turn draws N"],
  kreq=["CombatHistory.CardDrawnEntry (HappenedThisTurn, Status)"], powers=["IterationPower"], batch=4)
c("Leap", ["block 9 (+3)"], batch=1)
c("LightningRod", ["block 4 (+3)", "apply LightningRodPower 2: channel Lightning at energy reset, 2 turns"],
  kreq=ORB_CORE, powers=["LightningRodPower"], orbs=["LightningOrb"], batch=7,
  notes="LightningRodPower.AfterEnergyReset channels with ThrowingPlayerChoiceContext, then decrements.")
c("Loop", ["apply LoopPower 1 (+1): at turn start trigger the front orb's passive N times"],
  kreq=[K_ORBQ, K_PASSIVE], powers=["LoopPower"], batch=7, notes="Defect2Epoch-gated.")
c("MachineLearning", ["apply MachineLearningPower 1: +1 card drawn each turn", "upgrade: gain Innate"],
  powers=["MachineLearningPower"], batch=2)
c("MeteorStrike", ["damage 24 (+6) to target", "channel 3 Plasma"], kreq=ORB_CORE, orbs=["PlasmaOrb"],
  tier_own=3, batch=6, notes="5 cost. CardTag.Strike.")
c("Modded", ["gain 1 orb slot", "draw 1 (+1)", "this card costs 1 more for the rest of combat"],
  kreq=[K_ORBQ, "OrbCmd.AddSlots", "CardEnergyCost.AddThisCombat (present in sim)"], tier_own=3, batch=7,
  notes="0 cost.")
c("MomentumStrike", ["damage 11 (+4) to target", "this card costs 0 for the rest of combat"],
  kreq=["CardEnergyCost.SetThisCombat (present in sim)"], batch=1,
  notes="CardTag.Strike. Damage 10 (+3) -> 11 (+4) in v0.111.0.")
c("MultiCast", ["X cost (+1 if upgraded): evoke the front orb X times, removing it on the last evoke"],
  kreq=[K_X, K_ORBQ, K_EVOKE], tier_own=3, batch=7,
  notes="No OnUpgrade override. X = 0 evokes nothing. OrbEvokeType.All is UI-only.")
c("Null", ["damage 10 (+3) to target", "apply Weak 1 (+1)", "channel 1 Dark"], kreq=ORB_CORE,
  powers=["WeakPower"], orbs=["DarkOrb"], tier_own=3, batch=6,
  notes="Order: attack, Weak, channel. Weak 2 -> 1 in v0.111.0. Defect2Epoch-gated.")
c("OneForAll", ["every player gains OneForAllPower 3 (+1)"], kreq=[K_MP], powers=["OneForAllPower"], batch=99,
  notes="New in v0.111.0; MultiplayerOnly, TargetType AllAllies.")
c("Overclock", ["draw 2 (+1)", "add a Burn to the discard pile"], kreq=[K_STATUS_GEN], cards=["Burn"], batch=2,
  notes="0 cost. Burn deals 2 to its owner at turn end in hand (CardDef.OnTurnEndInHand exists in sim).")
c("Quadcast", ["evoke the front orb 4 times, removing it on the last", "upgrade: cost -1"],
  kreq=[K_ORBQ, K_EVOKE], tier_own=3, batch=7, notes="Ancient rarity. No-op with an empty queue.")
c("Rainbow", ["Exhaust (upgrade removes)", "channel Lightning, Frost, Dark"], kreq=ORB_CORE,
  orbs=["LightningOrb", "FrostOrb", "DarkOrb"], tier_own=3, batch=6)
c("Reboot", ["Exhaust", "put your hand into the draw pile", "shuffle the discard and draw piles together",
             "draw 4 (+2)"],
  kreq=["CardPileCmd.Add(card, Draw) (bottom)", "CardPileCmd.Shuffle (ported: discard + draw StableShuffle, ModifyShuffleOrder)"],
  rng_=[rng("Shuffle", "CardPileCmd.Shuffle -> StableShuffle(Rng.Shuffle) of discard+draw")],
  batch=1, notes="0 cost. The shuffle includes the discard pile, not only the returned hand.")
c("Refract", ["damage 10 (+3) to target, 2 hits", "channel 2 Glass"], kreq=ORB_CORE, orbs=["GlassOrb"],
  tier_own=3, batch=6, notes="Damage 9 -> 10 in v0.111.0.")
c("RocketPunch", ["damage 13 (+1) to target", "draw 1 (+1)", "costs 1 less until played per Status you create"],
  kreq=[K_LISTENER, "CardEnergyCost.AddUntilPlayed (present in sim)"], tier_own=2, batch=3,
  notes="Card listener AfterCardGeneratedForCombat (creator == owner, Status). v0.111.0 stacks -1 per Status; "
        "v0.107.1 set the cost to 0.")
c("Scavenge", ["exhaust a chosen card from hand", "apply EnergyNextTurnPower 2 (+1)"],
  kreq=["CardSelectCmd.FromHand", "CardCmd.Exhaust (ported)"], powers=["EnergyNextTurnPower"],
  prompts=[prompt("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, key="ExhaustSelectionPrompt",
                  cite="Commands/CardSelectCmd.cs:3668")],
  tier_own=3, batch=5, notes="The energy is granted even when nothing was exhausted. Defect7Epoch-gated.")
c("Scrape", ["damage 7 (+3) to target", "draw 4 (+1)", "discard the drawn cards that do not cost 0"],
  kreq=[K_DISCARD, K_SLY, "CardEnergyCost.GetWithModifiers(CostModifiers.All)"], tier_own=2, batch=3,
  notes="Filter keeps cost == 0 (all modifiers) and not X; v0.107.1 used CostModifiers.Local.")
c("ShadowShield", ["block 11 (+4)", "channel 1 Dark"], kreq=ORB_CORE, orbs=["DarkOrb"], tier_own=3, batch=6)
c("Shatter", ["Exhaust", "damage 7 (+4) to all enemies", "evoke every orb twice (front orb, keep then remove)"],
  kreq=[K_ORBQ, K_EVOKE, "OrbCmd.EvokeNext(dequeue: false)"], tier_own=3, batch=7,
  notes="Loop count = orb count before evoking. Exhaust is new in v0.111.0.")
c("SignalBoost", ["Exhaust", "apply SignalBoostPower 1: the next Power card plays twice", "upgrade: cost -1"],
  powers=["SignalBoostPower"], batch=2)
c("Skim", ["draw 3 (+1)"], batch=1)
c("Smokestack", ["apply SmokestackPower 5 (+2): each Status you create deals N to all enemies"],
  powers=["SmokestackPower"], batch=2, notes="Defect5Epoch-gated.")
c("Spinner", ["if upgraded: channel 1 Glass", "apply SpinnerPower 1: channel Glass at each energy reset"],
  kreq=ORB_CORE, powers=["SpinnerPower"], orbs=["GlassOrb"], tier_own=3, batch=7,
  notes="No OnUpgrade override; the upgraded channel uses ThrowingPlayerChoiceContext.")
c("Storm", ["apply StormPower 1 (+1): channel Lightning per Power card played"], kreq=ORB_CORE,
  powers=["StormPower"], orbs=["LightningOrb"], batch=7)
c("Subroutine", ["apply SubroutinePower 1: gain 1 energy per Power card played", "upgrade: cost -1"],
  powers=["SubroutinePower"], batch=2)
c("Sunder", ["damage 26 (+8) to target", "if it kills: gain 3 energy"], kreq=[K_RESULTS], batch=1,
  notes="Damage 24 -> 26 in v0.111.0.")
c("Supercritical", ["Exhaust", "gain 4 (+2) energy"], batch=1, notes="0 cost.")
c("SweepingBeam", ["damage 6 (+3) to all enemies", "draw 1"], batch=1, notes="BeforeDamage delegate is VFX only.")
c("Synchronize", ["gain Focus this turn = 1 (+1) x distinct orb types"],
  kreq=[K_CALC, K_TMPFOCUS, K_ORBQ], powers=["SynchronizePower", "FocusPower"], batch=6,
  notes="v0.111.0: no Exhaust, multiplier 1 (+1); v0.107.1: Exhaust (upgrade removes), multiplier 2.")
c("Synthesis", ["damage 14 (+6) to target", "apply FreePowerPower 1: next Power costs 0"], powers=["FreePowerPower"],
  batch=2)
c("Tempest", ["X cost (+1 if upgraded): channel X Lightning"], kreq=[K_X] + ORB_CORE, orbs=["LightningOrb"],
  tier_own=3, batch=6, notes="No OnUpgrade override.")
c("TeslaCoil", ["damage 3 (+1) to target", "every Lightning orb's passive hits the target (twice if upgraded)"],
  kreq=[K_ORBQ, K_PASSIVE, "LightningOrb.Passive with an explicit target"], orbs=["LightningOrb"], tier_own=3, batch=7,
  notes="0 cost. Snapshot of Lightning orbs; passive damage goes to cardPlay.Target, not a random enemy. Once the target "
        "is dead later passives do nothing (CreatureCmd.Damage skips dead targets) and draw no RNG.")
c("Thunder", ["apply ThunderPower 8 (+3): Lightning evokes deal N extra to the creatures they hit"],
  kreq=[K_ORBQ, "Hook.AfterOrbEvoked (missing in sim)"], powers=["ThunderPower"], orbs=["LightningOrb"], batch=7,
  notes="ThunderPower 6 (+2) -> 8 (+3) in v0.111.0.")
c("TrashToTreasure", ["apply TrashToTreasurePower: channel a random orb per Status you create", "upgrade: cost -1"],
  kreq=ORB_CORE + ["OrbModel.GetRandomOrb"], powers=["TrashToTreasurePower"],
  rng_=[rng("CombatOrbGeneration", "TrashToTreasurePower.AfterCardGeneratedForCombat GetRandomOrb per Status x Amount",
            site="power:TrashToTreasurePower")],
  batch=7, notes="Upgrade changed in v0.111.0 (was Innate).")
c("Turbo", ["gain 2 (+1) energy", "add a Void to the discard pile"], kreq=[K_STATUS_GEN], cards=["Void"], batch=3,
  notes="0 cost. Void (Unplayable, Ethereal) makes you lose 1 energy when drawn (its own AfterCardDrawn listener). "
        "Defect7Epoch-gated.")
c("Uproar", ["damage 6 (+2) to target, 2 hits", "auto-play a random Attack from the draw pile"],
  kreq=[K_AUTOPLAY, "ListExtensions.StableShuffle (List.Sort semantics)"],
  rng_=[rng("Shuffle", "StableShuffle of draw-pile Attacks without Unplayable; second StableShuffle over all Attacks "
                       "when the first list is empty", cite="Models.Cards/Uproar.cs"),
        rng("CombatTargets", "CardCmd.AutoPlay(target: null) for an AnyEnemy attack", explicit=False)],
  tier_own=2, batch=3)
c("Voltaic", ["Exhaust (upgrade removes)", "channel Lightning once per Lightning channeled this combat"],
  kreq=ORB_CORE + ["CombatHistory.OrbChanneledEntry (whole combat, Orb is LightningOrb)", K_CALC], orbs=["LightningOrb"],
  tier_own=3, batch=7, notes="Count taken before channeling.")
c("WhiteNoise", ["Exhaust", "add a random Power card from your pool to hand; it costs 0 this turn", "upgrade: cost -1"],
  kreq=["CardFactory.GetDistinctForCombat (FilterForCombat: CanBeGeneratedInCombat, not Basic/Ancient/Event)",
        "CardPoolModel.GetUnlockedCards(UnlockState, SingleplayerOnly)", "UnlockState input", "CardModel.SetToFreeThisTurn"],
  rng_=[rng("CombatCardGeneration", "CardFactory.GetDistinctForCombat TakeRandom(1)")],
  tier_own=3, batch=8,
  notes="Pool = owner's character pool Powers after epoch and multiplayer filters (MultiplayerOnly cards excluded).")
