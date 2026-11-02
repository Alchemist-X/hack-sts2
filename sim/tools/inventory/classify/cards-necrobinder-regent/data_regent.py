from helpers import C, P, NESTED, SHUF_DRAW, SHUF_RANDPOS, TARGETS_AUTOPLAY, GEN_RNG
CARDS = {}
def add(c): CARDS[c["cls"]] = c
SPEND = "Star spend hooks"
GAIN = "PlayerCmd.GainStars"
FORGE = ["ForgeCmd.Forge", "SovereignBlade token"]

add(C("Alignment", ["pay 2 stars", "gain 2 (upg 3) energy"], [SPEND]))
add(C("Arsenal", ["apply ArsenalPower 1: each card the owner generates for combat grants 1 Strength"], [], powers=["ArsenalPower"], notes="Upgrade adds Innate."))
add(C("AstralPulse", ["pay 3 stars", "damage 6 (upg 8) to all enemies, 2 hits"], [SPEND]))
add(C("BeatIntoShape", ["damage 5 (upg 7) to target",
    "Forge 5 (upg 7) + 5 (upg 7) x powered hits the owner dealt to the target earlier this turn (history count after the attack minus this attack's hits)"],
    FORGE + ["History.DamageReceived", "CalculatedVar family"], cards=["SovereignBlade"]))
add(C("Begone", ["choose 1 hand card and transform it into MinionStrike (upgraded if Begone is upgraded)"],
    ["CardSelectCmd.FromHand", "CombatState.CreateCard<T>", "CardCmd.Upgrade", "CardCmd.Transform"], cards=["MinionStrike"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, then="CardCmd.Transform(pick, new MinionStrike)", line=65, prefs="TransformSelectionPrompt",
               auto="empty hand -> nothing; exactly 1 -> taken without a decision")],
    notes="No OnUpgrade body; the upgraded flag only upgrades the MinionStrike."))
add(C("BigBang", ["draw 1", "gain 1 star", "gain 1 energy", "Forge 5"], [GAIN] + FORGE, cards=["SovereignBlade"], rng=[SHUF_DRAW],
    notes="Exhaust; upgrade adds Innate."))
add(C("BlackHole", ["apply BlackHolePower 3 (upg 4): each star gain and each star-spending card play deals that much damage to all enemies"], [],
    powers=["BlackHolePower"]))
add(C("Bombardment", ["damage 18 (upg 24) to target",
    "listener AfterAutoPrePlayPhaseEnteredEarly (owner's turn start): while in the exhaust pile, auto-play itself (random target)"],
    ["Card hook listeners", "Hook.AfterAutoPrePlayPhaseEntered", "CardCmd.AutoPlay"], rng=[TARGETS_AUTOPLAY],
    hooks={"AfterAutoPrePlayPhaseEnteredEarly": "Models.Cards/Bombardment.cs:230"},
    notes="Exhaust keyword, so each auto-play returns it to the exhaust pile; repeats every turn."))
add(C("Bulwark", ["gain 12 (upg 15) block", "Forge 10 (upg 13)"], FORGE, cards=["SovereignBlade"]))
add(C("BundleOfJoy", ["add 3 (upg 4) distinct random colorless cards to hand (one AddGeneratedCardToCombat each)"],
    ["CardFactory.GetDistinctForCombat"], rng=[GEN_RNG], notes="Exhaust. Cards are created lazily while enumerating (create, add, create, add)."))
add(C("CelestialMight", ["damage 6 to target, 3 (upg 4) hits"], []))
add(C("Charge", ["choose 2 cards in the draw pile and transform each into MinionDiveBomb (upgrade each result if Charge is upgraded)"],
    ["CardSelectCmd.FromCombatPile", "CardCmd.TransformTo<T>", "CardCmd.Upgrade"], cards=["MinionDiveBomb"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw", 2, 2, then="CardCmd.TransformTo<MinionDiveBomb> per pick, in pick order", line=77,
               prefs="TransformSelectionPrompt", auto="draw pile <= 2 cards -> all taken without a decision")]))
add(C("ChildOfTheStars", ["apply ChildOfTheStarsPower 2 (upg 3): spending stars grants that much block per star"], [], powers=["ChildOfTheStarsPower"]))
add(C("CloakOfStars", ["pay 1 star", "gain 7 (upg 10) block"], [SPEND]))
add(C("CollisionCourse", ["damage 10 (upg 14) to target", "add 1 Debris to hand"], ["CombatState.CreateCard<T>"], cards=["Debris"]))
add(C("Comet", ["pay 5 stars", "damage 33 (upg 44) to target", "apply Weak 3", "apply Vulnerable 3"], [SPEND], powers=["WeakPower", "VulnerablePower"]))
add(C("Conqueror", ["Forge 3 (upg 5)", "apply ConquerorPower 1 to target: SovereignBlade attacks deal double damage to it this turn"],
    FORGE, powers=["ConquerorPower"], cards=["SovereignBlade"]))
add(C("Constellation", ["pay 2 stars", "target ally player: gain 9 (upg 12) block, gain 1 energy, draw 1"], [SPEND, "Ally targeting (multiplayer)"],
    rng=[SHUF_DRAW], mp=True))
add(C("Convergence", ["apply RetainHandPower 1 (keep the hand this turn)", "apply EnergyNextTurnPower 1", "apply StarNextTurnPower 1 (upg 2)"], [],
    powers=["RetainHandPower", "EnergyNextTurnPower", "StarNextTurnPower"]))
add(C("CosmicIndifference", ["gain 6 (upg 9) block", "choose 1 card in the discard pile and put it on top of the draw pile (if still in draw/discard)"],
    ["CardSelectCmd.FromCombatPile"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Discard", 1, 1, then="CardPileCmd.Add(pick, Draw, Top)", line=143, prefs="SelectionScreenPrompt",
               auto="discard empty -> nothing; exactly 1 -> taken without a decision")]))
add(C("CrashLanding", ["damage 21 (upg 26) to all enemies", "fill the hand with Debris (10 - hand size)"],
    ["CombatState.CreateCard<T>", "CardPileCmd.AddGeneratedCardsToCombat"], cards=["Debris"]))
add(C("CrescentSpear", ["pay 1 star", "damage 8 + 2 (upg 3) x owner's combat cards with a star cost (CanonicalStarCost >= 0 or star X)"],
    [SPEND, "CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)"], notes="Counts all combat piles (AllCards), this card included."))
add(C("CrushUnder", ["capture hittable enemies", "damage 8 (upg 9) to all enemies", "apply CrushUnderPower 1 (upg 2) to the captured enemies: Strength down until their turn ends"],
    [], powers=["CrushUnderPower"]))
add(C("DecisionsDecisions", ["pay 6 stars", "draw 3 (upg 5)", "choose 1 Skill (not Unplayable) in hand", "auto-play it 3 times"],
    [SPEND, "CardSelectCmd.FromHand", "CardCmd.AutoPlay"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, filt="Type == Skill && !Keywords.Contains(Unplayable)", then="CardCmd.AutoPlay(pick, null) x Repeat(3)",
               line=154, prefs="SelectionScreenPrompt (PretendCardsCanBePlayed: UI only)", auto="no eligible card -> nothing; exactly 1 -> taken without a decision"),
             NESTED("CardCmd.AutoPlay of the chosen Skill (3 times)")],
    rng=[SHUF_DRAW, TARGETS_AUTOPLAY], notes="Exhaust. Each AutoPlay re-plays the same instance: OnPlayWrapper(isAutoPlay) first moves it into the play pile from wherever the previous play left it, even the exhaust pile (Models/CardModel.cs:839-841)."))
add(C("DefendRegent", ["gain 5 (upg 8) block"], [], notes="Starter (4 copies)."))
add(C("Devastate", ["pay 4 stars", "damage 35 (upg 45) to target"], [SPEND]))
add(C("DyingStar", ["pay 3 stars", "capture hittable enemies", "damage 9 (upg 11) to all enemies", "apply DyingStarPower 9 (upg 11) to each captured enemy: Strength down until their turn ends"],
    [SPEND], powers=["DyingStarPower"], notes="Ethereal."))
add(C("FallingStar", ["pay 2 stars", "damage 8 (upg 12) to target", "apply Weak 1", "apply Vulnerable 1"], [SPEND], powers=["WeakPower", "VulnerablePower"],
    notes="Regent starter (1 copy)."))
add(C("ForegoneConclusion", ["apply ForegoneConclusionPower 2 (upg 3): next turn, before the hand draw, choose that many draw-pile cards into hand"], [],
    powers=["ForegoneConclusionPower"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw", "Amount", "Amount", then="CardPileCmd.Add(picks, Hand), remove power",
               line=None, prefs="SelectionScreenPrompt", auto="draw pile <= Amount -> all taken without a decision",
               indirect="FOREGONE_CONCLUSION_POWER:BeforeHandDraw (turn-setup deferral; ShuffleIfNecessary first)")]))
add(C("Furnace", ["apply FurnacePower 5 (upg 7): Forge that much at each turn start"], [], powers=["FurnacePower"], cards=["SovereignBlade"]))
add(C("GammaBlast", ["pay 3 stars", "damage 13 (upg 18) to target", "apply Weak 2", "apply Vulnerable 2"], [SPEND], powers=["WeakPower", "VulnerablePower"]))
add(C("GatherLight", ["gain 8 (upg 11) block", "gain 1 star"], [GAIN]))
add(C("Genesis", ["apply GenesisPower 2 (upg 3): gain that many stars after each energy reset"], [], powers=["GenesisPower"]))
add(C("Glimmer", ["draw 3 (upg 4)", "choose 1 hand card and put it on top of the draw pile"], ["CardSelectCmd.FromHand"], rng=[SHUF_DRAW],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, then="CardPileCmd.Add(picks, Draw, Top)", line=115, prefs="SelectionScreenPrompt",
               auto="empty hand -> nothing; exactly 1 -> taken without a decision")]))
add(C("Glitterstream", ["blockNextTurn = Hook.ModifyBlock(5 (upg 7), Move) computed at play", "gain 11 (upg 13) block", "apply BlockNextTurnPower blockNextTurn"], [],
    powers=["BlockNextTurnPower"], notes="Hook.ModifyBlock is called directly by the card (exists in the sim)."))
add(C("Glow", ["gain 1 (upg 2) star", "draw 1", "apply DrawCardsNextTurnPower 1"], [GAIN], powers=["DrawCardsNextTurnPower"], rng=[SHUF_DRAW]))
add(C("Guards", ["choose any number of hand cards; transform each into MinionSacrifice (upgraded if Guards is upgraded)"],
    ["CardSelectCmd.FromHand", "CombatState.CreateCard<T>", "CardCmd.Upgrade", "CardCmd.Transform"], cards=["MinionSacrifice"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 0, 999999999, then="CardCmd.Transform(pick, new MinionSacrifice) per pick", line=105,
               prefs="SelectionScreenPrompt", auto="empty hand -> nothing; otherwise manual confirm (RequireManualConfirmation)", confirm=True)],
    notes="Exhaust."))
add(C("GuidingStar", ["pay 1 star", "damage 12 (upg 13) to target", "apply DrawCardsNextTurnPower 2 (upg 3)"], [SPEND], powers=["DrawCardsNextTurnPower"]))
add(C("HammerTime", ["apply HammerTimePower: when the owner forges, every other living player forges the same amount"], [], powers=["HammerTimePower"],
    cards=["SovereignBlade"], mp=True, notes="Upgrade: cost 2 -> 1."))
add(C("HeavenlyDrill", ["hits = X (ResolveEnergyXValue); if hits >= 4 then hits x 2", "damage 8 (upg 10) to target per hit"], []))
add(C("Hegemony", ["damage 15 (upg 18) to target", "apply EnergyNextTurnPower 2 (upg 3)"], [], powers=["EnergyNextTurnPower"]))
add(C("HeirloomHammer", ["damage 20 (upg 25) to target", "choose 1 colorless hand card (VisualCardPool.IsColorless); add 1 clone of it to hand"],
    ["CardSelectCmd.FromHand", "CardModel.CreateClone"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, filt="VisualCardPool.IsColorless", then="Repeat(1) x AddGeneratedCardToCombat(pick.CreateClone(), Hand)",
               line=126, prefs="SelectionScreenPrompt", auto="no colorless card -> nothing; exactly 1 -> taken without a decision")],
    notes="Needs the card's visual pool (colorless) on the sim card def."))
add(C("HiddenCache", ["gain 1 star", "apply StarNextTurnPower 3 (upg 4)"], [GAIN], powers=["StarNextTurnPower"]))
add(C("IAmInvincible", ["gain 10 (upg 13) block",
    "listener AfterAutoPostPlayPhaseEntered (owner's end of turn): if this card is the top of the draw pile, auto-play it from there"],
    ["Card hook listeners", "Hook.AfterAutoPostPlayPhaseEntered", "CardPileCmd.AutoPlayFromDrawPile"],
    hooks={"AfterAutoPostPlayPhaseEntered": "Models.Cards/IAmInvincible.cs:182"}))
add(C("KinglyKick", ["damage 27 (upg 35) to target", "listener AfterCardDrawn (self): cost -1 this combat"], ["Card hook listeners"],
    hooks={"AfterCardDrawn": "Models.Cards/KinglyKick.cs:115"}))
add(C("KinglyPunch", ["damage 8 (upg 10) to target", "listener AfterCardDrawn (self): damage +4 (upg +6) for this combat (re-applied after downgrade)"],
    ["Card hook listeners", "CardModel.AfterDowngraded"], hooks={"AfterCardDrawn": "Models.Cards/KinglyPunch.cs:131"}))
add(C("KnockoutBlow", ["damage 30 (upg 38) to target", "if any hit killed its target: gain 5 stars"], [GAIN]))
add(C("KnowThyPlace", ["apply Weak 1 to target", "apply Vulnerable 1 to target"], [], powers=["WeakPower", "VulnerablePower"], notes="Upgrade removes Exhaust."))
add(C("Largesse", ["give the target ally player 1 random colorless card (upgraded if Largesse is upgraded) in hand"],
    ["CardFactory.GetDistinctForCombat", "CardCmd.Upgrade", "Ally targeting (multiplayer)"], rng=[GEN_RNG], mp=True))
add(C("LunarBlast", ["damage 4 (upg 5) to target, hits = Skills the owner finished playing this turn"], ["CalculatedVar family", "History.CardPlaysFinished"]))
add(C("MakeItSo", ["damage 6 (upg 9) to target",
    "listener AfterCardPlayedLate: owner plays a Skill while this card is not in hand -> if Skills finished this turn % 3 == 0, move it to hand"],
    ["Card hook listeners", "History.CardPlaysFinished", "Hook.AfterCardChangedPiles"], hooks={"AfterCardPlayedLate": "Models.Cards/MakeItSo.cs:191"},
    notes="Returns from any pile (draw, discard, exhaust). History.CardPlayFinished is recorded before Hook.AfterCardPlayed (Models/CardModel.cs:735-739), so the count includes the Skill just played: it returns on every 3rd Skill of the turn."))
add(C("ManifestAuthority", ["gain 7 (upg 8) block", "add 1 random colorless card (upgraded if this is upgraded) to hand"],
    ["CardFactory.GetDistinctForCombat", "CardCmd.Upgrade"], rng=[GEN_RNG]))
add(C("MeteorShower", ["pay 2 stars", "damage 14 (upg 21) to all enemies", "apply Weak 2 to all hittable enemies", "apply Vulnerable 2 to all hittable enemies"],
    [SPEND], powers=["WeakPower", "VulnerablePower"], notes="Ancient rarity."))
add(C("MonarchsGaze", ["apply MonarchsGazePower 1: the owner's powered attack damage applies 1 temporary Strength down to the target"], [],
    powers=["MonarchsGazePower"], notes="Upgrade: cost 2 -> 1."))
add(C("Monologue", ["apply MonologuePower 1", "set the power's Strength var to Power (1): each card played this turn grants +1 Strength until end of turn"],
    ["PowerModel.DynamicVars"], powers=["MonologuePower"], notes="Upgrade adds Retain."))
add(C("NeutronAegis", ["pay 5 stars", "apply PlatingPower 8 (upg 11)"], [SPEND], powers=["PlatingPower"]))
add(C("Orbit", ["apply OrbitPower 1: every 4 energy the owner spends grants 1 energy"], [], powers=["OrbitPower"], notes="Upgrade: cost 2 -> 1."))
add(C("PaleBlueDot", ["apply PaleBlueDotPower 1 (upg 2): once per turn, after 5 card plays, gain that much DrawCardsNextTurn"], [], powers=["PaleBlueDotPower"]))
add(C("Parry", ["apply ParryPower 10 (upg 14): playing a SovereignBlade grants that much block"], [], powers=["ParryPower"], cards=["SovereignBlade"]))
add(C("ParticleWall", ["pay 2 stars", "gain 9 (upg 12) block", "after the play it returns to hand instead of the discard pile"], [SPEND],
    notes="GetResultLocationForCardPlay override (sim CardDef has the virtual)."))
add(C("Patter", ["gain 8 (upg 10) block", "apply Vigor 2 (upg 3)"], [], powers=["VigorPower"]))
add(C("PhotonCut", ["damage 10 (upg 13) to target", "draw 1 (upg 2)", "choose 1 hand card and put it on top of the draw pile"], ["CardSelectCmd.FromHand"],
    rng=[SHUF_DRAW],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, then="CardPileCmd.Add(picks, Draw, Top)", line=119, prefs="SelectionScreenPrompt",
               auto="empty hand -> nothing; exactly 1 -> taken without a decision")]))
add(C("PillarOfCreation", ["apply PillarOfCreationPower 2 (upg 3): each card the owner generates for combat grants that much block"], [],
    powers=["PillarOfCreationPower"]))
add(C("Plot", ["apply DrawCardsNextTurnPower 2 (upg 3) to each living player teammate"], ["Ally targeting (multiplayer)"], powers=["DrawCardsNextTurnPower"], mp=True))
add(C("Prophesize", ["draw 6 (upg 9)"], [], rng=[SHUF_DRAW]))
add(C("Quasar", ["pay 2 stars", "generate 3 distinct colorless cards (all upgraded if Quasar is upgraded)", "choose 1 (skippable) into hand"],
    [SPEND, "CardFactory.GetDistinctForCombat", "CardCmd.Upgrade", "CardSelectCmd.FromChooseACardScreen"], rng=[GEN_RNG],
    prompts=[P("ChooseCard", "CardSelectCmd.FromChooseACardScreen", "generated", 1, 1, skip=True, then="CardPileCmd.AddGeneratedCardToCombat(pick, Hand)", line=71,
               auto="never automatic while >= 1 option (a forced single choice is still a decision); skip = take nothing")]))
add(C("Radiate", ["damage 3 (upg 4) to all enemies, hits = stars gained by the owner this turn (sum of positive StarsModified)"],
    ["CalculatedVar family", "History.StarsModified", GAIN]))
add(C("RefineBlade", ["Forge 8 (upg 12)", "apply EnergyNextTurnPower 1"], FORGE, powers=["EnergyNextTurnPower"], cards=["SovereignBlade"]))
add(C("Reflect", ["pay 3 stars", "gain 15 (upg 20) block", "apply ReflectPower 1: blocked powered damage is reflected to the attacker until next turn"], [SPEND],
    powers=["ReflectPower"]))
add(C("Resonance", ["pay 2 stars", "gain 1 (upg 2) Strength", "each hittable enemy loses 1 Strength"], [SPEND], powers=["StrengthPower"]))
add(C("Royalties", ["apply RoyaltiesPower 30 (upg 40): after combat, an extra gold reward of that size"], [], powers=["RoyaltiesPower"],
    notes="CanBeGeneratedInCombat = false."))
add(C("RoyalGamble", ["pay 5 stars", "gain 9 stars"], [SPEND, GAIN], notes="Exhaust; upgrade adds Retain."))
add(C("SeekingEdge", ["apply SeekingEdgePower 1: SovereignBlades hit all enemies", "Forge 7 (upg 11)"], FORGE, powers=["SeekingEdgePower"], cards=["SovereignBlade"]))
add(C("SevenStars", ["pay 7 stars", "damage 7 to all enemies, 7 hits"], [SPEND], notes="Upgrade: energy cost 2 -> 1."))
add(C("ShiningStrike", ["damage 8 (upg 11) to target", "gain 2 stars", "after the play it goes to the top of the draw pile instead of the discard pile"], [GAIN],
    notes="GetResultLocationForCardPlay override (Discard -> Draw Top)."))
add(C("SolarStrike", ["damage 9 (upg 10) to target", "gain 1 (upg 2) star"], [GAIN]))
add(C("SpectrumShift", ["apply SpectrumShiftPower 1: each turn before hand draw, add 1 random colorless card to hand"], [], powers=["SpectrumShiftPower"],
    rng=["via SpectrumShiftPower: " + GEN_RNG + ", each turn"], notes="Upgrade: cost 2 -> 1."))
add(C("SpoilsOfBattle", ["Forge 6 (upg 9)", "draw 2"], FORGE, cards=["SovereignBlade"], rng=[SHUF_DRAW]))
add(C("Stardust", ["pay all stars (X = ResolveStarXValue = ModifyXValue(LastStarsSpent))", "damage 5 (upg 7) X hits, each to a random enemy"], [SPEND],
    rng=["RunRngType.CombatTargets once per hit (TargetingRandomOpponents)"], notes="Star X cost and TargetingRandomOpponents exist in the sim."))
add(C("StrikeRegent", ["damage 6 (upg 9) to target"], [], notes="Starter (4 copies)."))
add(C("SummonForth", ["move every owner SovereignBlade not in hand (draw, discard, exhaust, no pile) to hand", "Forge 8 (upg 11)"],
    FORGE + ["Hook.AfterCardChangedPiles"], cards=["SovereignBlade"]))
add(C("Supermassive", ["damage 5 + 3 (upg 4) x cards the owner generated this combat to target"],
    ["CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)", "History.CardGenerated"]))
add(C("SwordSage", ["apply SwordSagePower 1: every SovereignBlade plays 1 extra time"], [], powers=["SwordSagePower"], cards=["SovereignBlade"],
    notes="Upgrade: cost 2 -> 1."))
add(C("Terraforming", ["apply Vigor 7 (upg 10)"], [], powers=["VigorPower"]))
add(C("TheSealedThrone", ["pay 3 stars", "apply TheSealedThronePower 1: each card the owner plays grants 1 star"], [SPEND], powers=["TheSealedThronePower"],
    notes="Ancient rarity; upgrade: energy cost 1 -> 0."))
add(C("TheSmith", ["pay 4 stars", "Forge 30 (upg 40)"], [SPEND] + FORGE, cards=["SovereignBlade"]))
add(C("Tutor", ["target ally player chooses 1 card from its own draw pile into its hand"], ["CardSelectCmd.FromCombatPile", "Ally targeting (multiplayer)"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw (target player)", 1, 1, then="CardPileCmd.Add(pick, Hand)", line=69, prefs="SelectionScreenPrompt",
               auto="draw pile empty -> nothing; exactly 1 -> taken without a decision")],
    mp=True, notes="Upgrade: cost 1 -> 0. Declared 'public class' (not sealed); no subclasses in v0.111.0."))
add(C("Tyranny", ["apply TyrannyPower 1: +1 hand draw; after each turn start choose 1 hand card to exhaust"], [], powers=["TyrannyPower"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", "Amount", "Amount", then="CardCmd.Exhaust per pick", line=None, prefs="ExhaustSelectionPrompt",
               auto="hand size <= Amount -> all taken without a decision", indirect="TYRANNY_POWER:AfterPlayerTurnStart (turn-setup deferral)")],
    notes="Upgrade adds Innate."))
add(C("Venerate", ["gain 2 (upg 3) stars"], [GAIN], notes="Regent starter (1 copy)."))
add(C("VoidForm", ["apply VoidFormPower 2: the first 2 non-auto-play cards each turn cost 0 energy and 0 stars", "end the turn (PlayerCmd.EndTurn, canBackOut false)"],
    ["PlayerCmd.EndTurn"], powers=["VoidFormPower"], notes="Upgrade removes Ethereal."))
add(C("WroughtInWar", ["damage 7 (upg 9) to target", "Forge 7 (upg 9)"], FORGE, cards=["SovereignBlade"]))
