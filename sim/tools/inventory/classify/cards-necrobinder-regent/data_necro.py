from helpers import C, P, NESTED, SHUF_DRAW, SHUF_RANDPOS, TARGETS_AUTOPLAY
CARDS = {}
def add(c): CARDS[c["cls"]] = c

add(C("Afterlife", ["summon Osty 6 (upg 9)"], ["OstyCmd.Summon"]))
add(C("BansheesCry", ["damage 33 to all enemies",
    "listener AfterCardPlayed: each Ethereal card the owner plays -> this card costs -2 this combat",
    "listener AfterCardEnteredCombat (self, not a clone): cost -2 x Ethereal card plays finished by the owner earlier this combat"],
    ["Card hook listeners", "History.CardPlaysFinished"],
    notes="Upgrade: cost 9 -> 7. Discount uses EnergyCost.AddThisCombat (exists in sim). IsClone check needs the clone flag on combat cards.",
    hooks={"AfterCardEnteredCombat": "Models.Cards/BansheesCry.cs:124", "AfterCardPlayed": "Models.Cards/BansheesCry.cs:139"}))
add(C("BlightStrike", ["damage 8 (upg 10) to target", "apply Doom = total damage dealt by that attack (sum of DamageResult.TotalDamage) to target"], [],
    powers=["DoomPower"], notes="Reads AttackCommand.Results after Execute (sim keeps results)."))
add(C("Bodyguard", ["summon Osty 5 (upg 7)"], ["OstyCmd.Summon"], notes="Necrobinder starter (1 copy)."))
add(C("BoneShards", ["if Osty missing: nothing", "Osty attacks all enemies 9 (upg 12)", "gain 9 (upg 12) block", "kill Osty (if still alive)"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "CreatureCmd.Kill(Osty)"]))
add(C("BorrowedTime", ["gain 4 (upg 6) energy", "apply BorrowedTimePower 1 to self (owner's cards cost +1 until owner's side turn ends)"], [],
    powers=["BorrowedTimePower"]))
add(C("Bury", ["damage 52 (upg 63) to target"], []))
add(C("Cacophony", ["apply CacophonyPower 66 (upg 99) to self (every 33 cards drawn: that much unpowered damage to a random enemy)"], [],
    powers=["CacophonyPower"], rng=["via CacophonyPower: RunRngType.CombatTargets per trigger"], mp=True))
add(C("Calcify", ["apply CalcifyPower 4 (upg 6): +Amount damage on Osty's powered attacks"], [], powers=["CalcifyPower"]))
add(C("CallOfTheVoid", ["apply CallOfTheVoidPower 1: each turn before hand draw, 1 random Ethereal card from the character pool into hand"], [],
    powers=["CallOfTheVoidPower"], rng=["via CallOfTheVoidPower: RunRngType.CombatCardGeneration (one GetDistinctForCombat(count 1) per card, each turn)"],
    notes="Upgrade adds Innate."))
add(C("CaptureSpirit", ["deal 3 (upg 4) unblockable unpowered damage to target (CreatureCmd.Damage, not an attack)", "add 3 (upg 4) Souls to the draw pile at random positions"],
    ["CreatureCmd.Damage(DamageVar)", "CombatState.CreateCard<T>", "CardPileCmd.AddGeneratedCardsToCombat"], cards=["Soul"], rng=[SHUF_RANDPOS]))
add(C("Cleanse", ["summon Osty 3 (upg 5)", "choose 1 card in the draw pile and exhaust it"],
    ["OstyCmd.Summon", "CardSelectCmd.FromCombatPile"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw", 1, 1, then="CardCmd.Exhaust the pick", line=118, prefs="ExhaustSelectionPrompt",
               auto="draw pile empty -> nothing; exactly 1 card -> taken without a decision")]))
add(C("Countdown", ["apply CountdownPower 6 (upg 9): each turn start, that much Doom on a random enemy"], [], powers=["CountdownPower"],
    rng=["via CountdownPower: RunRngType.CombatTargets once per owner turn start"]))
add(C("DanseMacabre", ["apply DanseMacabrePower 4 (upg 6): playing a card with resolved energy cost >= 2 grants that much block"], [], powers=["DanseMacabrePower"]))
add(C("Deathbringer", ["apply Doom 21 (upg 26) to all hittable enemies", "apply Weak 1 to all hittable enemies"], [], powers=["DoomPower", "WeakPower"],
    notes="Order: Doom first, then Weak (await order)."))
add(C("DeathsDoor", ["gain 6 (upg 7) block", "if the owner applied Doom this turn: gain the same block 2 more times (3 total)"],
    ["History.PowerReceived"], powers=["DoomPower"], notes="Block is applied as separate GainBlock calls (each runs the block hooks)."))
add(C("DeathMarch", ["damage (8 + 4 x cards drawn this turn outside the hand draw) to target (upg 9 + 6 x)"],
    ["CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)", "History.CardDrawn"],
    notes="Counts CardDrawnEntry with Actor == owner, FromHandDraw false, this turn; evaluated per hit."))
add(C("Debilitate", ["damage 10 (upg 12) to target", "apply DebilitatePower 2 (upg 3): Vulnerable on the target and Weak on its attacks have double effect"], [],
    powers=["DebilitatePower"], notes="Sim already exposes IVulnerableMultiplierModifier/IWeakMultiplierModifier keyed DEBILITATE_POWER."))
add(C("DefendNecrobinder", ["gain 5 (upg 8) block"], [], notes="Starter (4 copies)."))
add(C("Defile", ["damage 13 (upg 17) to target"], []))
add(C("Defy", ["gain 6 (upg 9) block", "apply Weak 1 to target"], [], powers=["WeakPower"]))
add(C("Delay", ["gain 11 (upg 13) block", "apply EnergyNextTurnPower 1 (upg 2)"], [], powers=["EnergyNextTurnPower"]))
add(C("Demesne", ["apply DemesnePower 1: +1 hand draw and +1 max energy each turn"], [], powers=["DemesnePower"], notes="Upgrade: cost 3 -> 2."))
add(C("DevourLife", ["apply DevourLifePower 1 (upg 2): each Soul the owner plays summons that much"], [], powers=["DevourLifePower"]))
add(C("Dirge", ["X = ResolveEnergyXValue", "summon 3 (upg 4) X times (separate summons)", "add X Souls (upgraded if Dirge is upgraded) to the draw pile at random positions"],
    ["OstyCmd.Summon", "CombatState.CreateCard<T>", "CardCmd.Upgrade", "CardPileCmd.AddGeneratedCardsToCombat"], cards=["Soul"], rng=[SHUF_RANDPOS]))
add(C("DrainPower", ["damage 10 (upg 12) to target", "upgrade 2 (upg 3) random upgradable cards in the discard pile"],
    ["CardCmd.Upgrade"], rng=["RunRngType.CombatCardSelection: UnstableShuffle of the upgradable discard cards (TakeRandom), consumed once after the attack"]))
add(C("Dredge", ["n = min(3, 10 - hand size); if n > 0: choose n cards from the discard pile into hand"], ["CardSelectCmd.FromCombatPile"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Discard", "n", "n", then="CardPileCmd.Add(picks, Hand)", line=114, prefs="SelectionScreenPrompt",
               auto="skipped when n <= 0; discard pile size <= n -> all taken without a decision")],
    notes="Upgrade adds Retain."))
add(C("Eidolon", ["auto-play every card in the exhaust pile that is Ethereal and not Unplayable (snapshot list, in pile order)"],
    ["CardCmd.AutoPlay"], prompts=[NESTED("CardCmd.AutoPlay of each exhausted Ethereal card")], rng=[TARGETS_AUTOPLAY, "plus whatever the auto-played cards consume"],
    notes="CanBeGeneratedInCombat = false. Upgrade: cost 2 -> 1. Exhaust keyword. Auto-play moves each card from the exhaust pile into the play pile (Models/CardModel.cs:839-841); afterwards it goes to its normal result pile (discard unless it exhausts)."))
add(C("EndOfDays", ["apply Doom 29 (upg 37) to each hittable enemy", "DoomKill every hittable enemy whose HP <= its Doom"],
    ["DoomPower.DoomKill from card"], powers=["DoomPower"]))
add(C("EnfeeblingTouch", ["apply EnfeeblingTouchPower 8 (upg 11) to target: -8 Strength until its side turn ends"], [], powers=["EnfeeblingTouchPower"]))
add(C("Eradicate", ["damage 11 (upg 14) to target, X hits (ResolveEnergyXValue)"], [], notes="Retain keyword. X-cost energy is supported by the sim."))
add(C("Fear", ["damage 7 (upg 8) to target", "apply Vulnerable 1 (upg 2) to target"], [], powers=["VulnerablePower"]))
add(C("Fetch", ["if Osty missing: nothing", "Osty attacks target 3 (upg 6)", "if this card has not finished a play earlier this turn: draw 1"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "History.CardPlaysFinished"], rng=[SHUF_DRAW],
    notes="CardPlayFinished is recorded per play after OnPlay (Models/CardModel.cs:735), so the first play each turn draws and a replay in the same series (e.g. after Transfigure) does not."))
add(C("Flatten", ["if Osty missing: nothing", "Osty attacks target 12 (upg 16)",
    "listener AfterAttack: an attack by the owner's Osty -> this card costs 0 this turn",
    "listener AfterCardEnteredCombat (self): if Osty attacked this turn -> cost 0 this turn"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "Card hook listeners", "History.CreatureAttacked"],
    hooks={"AfterCardEnteredCombat": "Models.Cards/Flatten.cs:141", "AfterAttack": "Models.Cards/Flatten.cs:155"}))
add(C("ForbiddenGrimoire", ["apply ForbiddenGrimoirePower 1: after combat, 1 extra card-removal reward"], [], powers=["ForbiddenGrimoirePower"],
    notes="Ancient rarity, Eternal keyword; upgrade: cost 2 -> 1. Excluded from in-combat generation (FilterForCombat drops Ancient)."))
add(C("Friendship", ["apply Strength -2 (upg -1) to self", "apply FriendshipPower 1: +1 max energy"], [], powers=["StrengthPower", "FriendshipPower"]))
add(C("GlimpseBeyond", ["for each living player teammate (self included): add 3 (upg 4) Souls to that player's draw pile at random positions"],
    ["Ally targeting (multiplayer)", "CombatState.CreateCard<T>", "CardPileCmd.AddGeneratedCardsToCombat"], cards=["Soul"], rng=[SHUF_RANDPOS], mp=True))
add(C("Graveblast", ["damage 4 (upg 6) to target", "choose 1 card from the discard pile into hand"], ["CardSelectCmd.FromCombatPile"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Discard", 1, 1, then="CardPileCmd.Add(pick, Hand)", line=125, prefs="SelectionScreenPrompt",
               auto="discard empty -> nothing; exactly 1 card -> taken without a decision")],
    notes="Upgrade removes Exhaust."))
add(C("GraveWarden", ["gain 8 (upg 11) block", "add 1 Soul to the draw pile at a random position"],
    ["CombatState.CreateCard<T>", "CardPileCmd.AddGeneratedCardsToCombat"], cards=["Soul"], rng=[SHUF_RANDPOS]))
add(C("Hang", ["damage 10 (upg 13) to target", "apply HangPower max(2, current HangPower) to target (capped at 999999999 total): doubles Hang's multiplier"], [],
    powers=["HangPower"]))
add(C("Haunt", ["apply HauntPower 7 (upg 9): each Soul the owner plays deals that much unblockable damage to a random enemy"], [], powers=["HauntPower"],
    cards=["Soul"], rng=["via HauntPower: RunRngType.CombatTargets per Soul played"]))
add(C("HighFive", ["playable only while Osty is alive", "Osty attacks all enemies 11 (upg 13)", "apply Vulnerable 2 (upg 3) to all hittable enemies"],
    ["Osty-missing gate", "AttackCommand.FromOsty"], powers=["VulnerablePower"], notes="IsPlayable => !Owner.IsOstyMissing (sim CardDef.IsPlayable exists)."))
add(C("Invoke", ["apply SummonNextTurnPower 2 (upg 3)", "apply EnergyNextTurnPower 2 (upg 3)"], [], powers=["SummonNextTurnPower", "EnergyNextTurnPower"]))
add(C("LegionOfBone", ["for each living player creature: summon 6 (upg 8) for that player"], ["OstyCmd.Summon", "Ally targeting (multiplayer)"], mp=True))
add(C("Lethality", ["apply LethalityPower 50 (upg 75): the owner's first Attack each turn deals +50%"], [], powers=["LethalityPower"], notes="Ethereal."))
add(C("Melancholy", ["gain 13 (upg 17) block",
    "listener AfterDeath: any creature dies (removal not prevented) while this card is in a combat pile -> cost -1 this combat"],
    ["Card hook listeners"], hooks={"AfterDeath": "Models.Cards/Melancholy.cs:112"}, notes="Osty deaths count."))
add(C("Misery", ["snapshot the target's debuffs (TypeForCurrentAmount == Debuff) as (clone, amount); fold temporary powers' amounts into their InternallyAppliedPower entry",
    "damage 7 (upg 9) to target",
    "for each other hittable enemy, for each snapshot entry with amount != 0: stack onto an existing instance (FindExistingInstanceForStacking, original applier) via ModifyAmount, else Apply a fresh clone"],
    ["Generic power copy"], notes="Upgrade adds Retain. Copies arbitrary debuffs incl. instance data, hence cross-cutting.",
    oq=["Misery overwrites values of its snapshot Dictionary while enumerating it (Models.Cards/Misery.cs:108-127). The build uses C# 12 inline arrays (.NET 8+), where overwriting an existing key does not invalidate the enumerator, so the sim (.NET 9) can mirror the two-phase loop; pin the merge result (temporary debuff + its Strength entry) with a unit test against a recorded or MCP-driven combat."]))
add(C("NecroMastery", ["summon 5 (upg 8)", "apply NecroMasteryPower 1: whenever Osty loses HP, that loss is dealt as unblockable damage to all enemies"],
    ["OstyCmd.Summon"], powers=["NecroMasteryPower"]))
add(C("NegativePulse", ["gain 5 (upg 6) block", "apply Doom 7 (upg 11) to each hittable enemy"], [], powers=["DoomPower"]))
add(C("Neurosurge", ["gain 3 (upg 4) energy", "draw 2", "apply NeurosurgePower 3 to self: 3 Doom on yourself at each turn start"], [],
    powers=["NeurosurgePower"], rng=[SHUF_DRAW]))
add(C("NoEscape", ["apply Doom 10 (upg 15) + 5 x floor(target Doom / 10) to target"], ["CalculatedVar family"], powers=["DoomPower"]))
add(C("Oblivion", ["apply OblivionPower 3 (upg 4) to target: every card the owner plays this turn applies that much Doom to it"], [], powers=["OblivionPower"]))
add(C("Pagestorm", ["apply PagestormPower 1: drawing an Ethereal card draws 1 more"], [], powers=["PagestormPower"], rng=["via PagestormPower draws: " + SHUF_DRAW],
    notes="Upgrade: cost 1 -> 0."))
add(C("Parse", ["draw 3 (upg 4)"], [], rng=[SHUF_DRAW], notes="Ethereal."))
add(C("Poke", ["if Osty missing: nothing", "Osty attacks target 6 (upg 9)"], ["Osty-missing gate", "AttackCommand.FromOsty"]))
add(C("Protector", ["if Osty missing: nothing", "Osty attacks target for 10 (upg 15) + Osty max HP"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)"],
    notes="Ancient rarity; upgrade also cost 1 -> 0."))
add(C("PullAggro", ["summon 4 (upg 5)", "gain 7 (upg 9) block"], ["OstyCmd.Summon"]))
add(C("PullFromBelow", ["damage 5 (upg 7) to target, hits = Ethereal card plays finished by the owner this combat (0 hits possible)"],
    ["CalculatedVar family", "History.CardPlaysFinished"]))
add(C("Putrefy", ["apply Weak 2 (upg 3) to target", "apply Vulnerable 2 (upg 3) to target"], [], powers=["WeakPower", "VulnerablePower"], notes="Exhaust."))
add(C("Rattle", ["if Osty missing: nothing", "Osty attacks target 7 (upg 9), hits = 1 + Osty attacks earlier this turn"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "CalculatedVar family", "History.CreatureAttacked"]))
add(C("Reanimate", ["summon 20 (upg 25)"], ["OstyCmd.Summon"], notes="Exhaust."))
add(C("Reap", ["damage 27 (upg 33) to target"], [], notes="Retain."))
add(C("ReaperForm", ["apply ReaperFormPower 1: powered attack damage by the owner or Osty applies that much Doom to the target"], [], powers=["ReaperFormPower"],
    notes="Upgrade adds Retain."))
add(C("Reave", ["damage 10 (upg 13) to target", "add 1 Soul (upgraded if Reave is upgraded) to the draw pile at a random position"],
    ["CombatState.CreateCard<T>", "CardCmd.Upgrade", "CardPileCmd.AddGeneratedCardsToCombat"], cards=["Soul"], rng=[SHUF_RANDPOS]))
add(C("RightHandHand", ["if Osty missing: nothing", "Osty attacks target 4 (upg 6)",
    "listener AfterCardPlayedLate: owner plays a card with EnergyValue >= 2 while this card is in the discard pile -> move it to hand"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "Card hook listeners", "CardPlay.Resources", "Hook.AfterCardChangedPiles"],
    hooks={"AfterCardPlayedLate": "Models.Cards/RightHandHand.cs:209"}))
add(C("Sacrifice", ["if Osty missing: nothing", "block = 3 x Osty max HP (computed before the kill)", "kill Osty", "gain that block"],
    ["Osty-missing gate", "CreatureCmd.Kill(Osty)", "CalculatedVar family"], notes="Retain; upgrade: cost 1 -> 0."))
add(C("Scourge", ["apply Doom 13 (upg 16) to target", "draw 1 (upg 2)"], [], powers=["DoomPower"], rng=[SHUF_DRAW]))
add(C("SculptingStrike", ["damage 9 (upg 12) to target", "choose 1 hand card without a Local Ethereal keyword and make it Ethereal"],
    ["CardSelectCmd.FromHand", "CardCmd.ApplyKeyword", "Keyword sources"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, filt="!GetKeywordsWithSources(Local).Contains(Ethereal)", then="CardCmd.ApplyKeyword(pick, Ethereal)",
               line=90, prefs="SelectionScreenPrompt", auto="no eligible card -> nothing; exactly 1 -> taken without a decision")]))
add(C("Seance", ["choose 1 card in the draw pile and transform it into a Soul"], ["CardSelectCmd.FromCombatPile", "CardCmd.TransformTo<T>"], cards=["Soul"],
    prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw", 1, 1, then="CardCmd.TransformTo<Soul>(pick)", line=77, prefs="TransformSelectionPrompt",
               auto="draw pile empty -> nothing; exactly 1 card -> taken without a decision")],
    notes="Ethereal; upgrade: cost 1 -> 0."))
add(C("SentryMode", ["apply SentryModePower 1: each turn before hand draw, add 1 SweepingGaze to hand"], [], powers=["SentryModePower"],
    cards=["SweepingGaze"], rng=["via SweepingGaze plays: RunRngType.CombatTargets"], notes="Upgrade: cost 2 -> 1."))
add(C("Severance", ["damage 13 (upg 18) to target", "create 3 Souls: one into the draw pile at a random position, one into the discard pile, one into hand (separate adds)"],
    ["CombatState.CreateCard<T>"], cards=["Soul"], rng=[SHUF_RANDPOS + " (first Soul only)"]))
add(C("SharedFate", ["apply Strength -2 to self", "apply Strength -2 (upg -3) to target"], [], powers=["StrengthPower"], notes="Exhaust. Self first, then target."))
add(C("Shroud", ["apply ShroudPower 3 (upg 4): each Doom the owner applies grants that much block"], [], powers=["ShroudPower"]))
add(C("SicEm", ["Osty attacks target 5 (upg 6) (skipped if Osty missing)", "apply SicEmPower 3 (upg 4) to target (even if Osty is missing): Osty hits on it summon that much"],
    ["Osty-missing gate", "AttackCommand.FromOsty"], powers=["SicEmPower"]))
add(C("SleightOfFlesh", ["apply SleightOfFleshPower 9 (upg 13): each non-temporary debuff the owner applies to an enemy deals that much damage to it"], [],
    powers=["SleightOfFleshPower"]))
add(C("Snap", ["Osty attacks target 7 (upg 10) (skipped if Osty missing)", "choose 1 hand card without Retain and give it Retain (even if Osty is missing)"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "CardSelectCmd.FromHand", "CardCmd.ApplyKeyword"],
    prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, filt="!Keywords.Contains(Retain)", then="CardCmd.ApplyKeyword(pick, Retain)", line=106,
               prefs="SelectionScreenPrompt", auto="no eligible card -> nothing; exactly 1 -> taken without a decision")]))
add(C("Soulbound", ["apply SoulboundPower 1 to an ally player: whenever the owner generates a Soul, that ally gets 1 Soul in its draw pile"], ["Ally targeting (multiplayer)"],
    powers=["SoulboundPower"], cards=["Soul"], mp=True, notes="Upgrade adds Innate."))
add(C("SoulStorm", ["damage 9 + 4 (upg 6) x Souls in the owner's exhaust pile to target"], ["CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)"],
    cards=["Soul"]))
add(C("Sow", ["damage 8 (upg 11) to all enemies"], [], notes="Retain."))
add(C("SpiritOfAsh", ["apply SpiritOfAshPower 4 (upg 5): playing an Ethereal card grants that much block"], [], powers=["SpiritOfAshPower"]))
add(C("Spur", ["summon 3 (upg 5)", "heal Osty 5 (upg 7)"], ["OstyCmd.Summon", "CreatureCmd.Heal(Osty)"], notes="Retain. Heal runs after the summon."))
add(C("Squeeze", ["if Osty missing: nothing", "Osty attacks target for 25 (upg 30) + 5 (upg 6) x other OstyAttack cards in the owner's combat piles"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)"]))
add(C("StrikeNecrobinder", ["damage 6 (upg 9) to target"], [], notes="Starter (4 copies)."))
add(C("TheScythe", ["damage CurrentDamage (13 + accumulated) to target", "+5 (upg +7) CurrentDamage on this card and on its DeckVersion"],
    ["DeckVersion mutation", "CardModel.AfterDowngraded"], notes="Exhaust. Growth persists across combats through the deck copy ([SavedProperty] CurrentDamage/IncreasedDamage)."))
add(C("TimesUp", ["damage = target's Doom amount (0 + 1 x Doom)"], ["CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)"], powers=["DoomPower"],
    notes="Upgrade adds Retain."))
add(C("Transfigure", ["choose 1 hand card: if not X-cost and cost >= 0 -> +1 energy cost this combat; BaseReplayCount +1 (plays an extra time)"],
    ["CardSelectCmd.FromHand"], prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", 1, 1, then="EnergyCost.AddThisCombat(1); BaseReplayCount++", line=68,
               prefs="SelectionScreenPrompt", auto="empty hand -> nothing; exactly 1 -> taken without a decision")],
    notes="CanBeGeneratedInCombat = false; upgrade removes Exhaust."))
add(C("Undeath", ["gain 7 (upg 9) block", "add a clone of this card to the discard pile"], ["CardModel.CreateClone"]))
add(C("Underworld", ["apply UnderworldPower 1: allies' powered attack damage applies that much Doom"], [], powers=["UnderworldPower"], mp=True,
    notes="Upgrade removes Exhaust."))
add(C("Unleash", ["if Osty missing: nothing", "Osty attacks target for 6 (upg 9) + Osty current HP"],
    ["Osty-missing gate", "AttackCommand.FromOsty", "CalculatedVar family", "DamageCmd.Attack(CalculatedDamageVar)"], notes="Necrobinder starter (1 copy)."))
add(C("Veilpiercer", ["damage 10 (upg 13) to target", "apply VeilpiercerPower 1: the next Ethereal card played from hand costs 0"], [], powers=["VeilpiercerPower"]))
add(C("Wisp", ["gain 1 energy"], [], notes="Exhaust; upgrade adds Retain."))
