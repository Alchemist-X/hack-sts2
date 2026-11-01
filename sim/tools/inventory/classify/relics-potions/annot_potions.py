from annot_common import Q, P

Q('Ambergris', 1, 2, m='heal 50% max HP; AmbergrisPower in combat',
  c='OnUse: Heal target MaxHp x 50/100; in combat -> AmbergrisPower(1) on target', r='OnUse (out of combat)[potion_use]: AnyTime -> heal only (no AmbergrisPower outside combat)',
  k='AmbergrisPower', n='new in v0.111.0; procured by NeowsSacrifice')
Q('Ashwater', 3, 5, m='exhaust any number of hand cards (choice)',
  c='OnUse: FromHand 0..inf (filter none) on target player -> CardCmd.Exhaust each',
  p=[P('HandSelect', 'CardSelectCmd.FromHand', 'OnUse', 'Hand', '0', '999999999', 'UsePotionAction context (inline); RMC')])
Q('AttackPotion', 3, 5, m='choose 1 of 3 random Attacks, free this turn',
  c='OnUse: GetDistinctForCombat(3, character pool Attacks, CombatCardGeneration) -> FromChooseACardScreen (canSkip) -> SetToFreeThisTurn -> AddGeneratedCardToCombat Hand',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'OnUse', 'generated (3)', '1', '1', 'canSkip=true')], g='RunRng.CombatCardGeneration')
Q('BeetleJuice', 1, 1, m='Shrink 4 on an enemy', c='OnUse: ShrinkPower(4) on target enemy', n='DamageDecrease 30 is text only')
Q('BlessingOfTheForge', 2, 3, m='upgrade hand', c='OnUse: CardCmd.Upgrade every upgradable hand card of target player', k='')
Q('BlockPotion', 1, 1, m='12 block', c='OnUse: GainBlock 12 (Unpowered) on target', k='CreatureCmd.GainBlock(BlockVar) overload')
Q('BloodPotion', 1, 1, m='heal 20% max HP', c='OnUse: Heal target MaxHp x 20/100', r='OnUse (out of combat)[potion_use]: AnyTime -> same heal')
Q('BoneBrew', 3, 4, m='summon 15', c='OnUse: OstyCmd.Summon(target player, 15)', k='Osty pet')
Q('BottledPotential', 2, 3, m='hand into draw pile, shuffle, draw 5',
  c='OnUse: CardPileCmd.Add(hand cards, Draw); CardPileCmd.Shuffle; Draw 5', g='RunRng.Shuffle', k='')
Q('Clarity', 1, 2, m='draw 1; Clarity 3', c='OnUse: Draw 1; ClarityPower(3) on target', k='ClarityPower')
Q('ColorlessPotion', 3, 5, m='choose 1 of 3 colorless cards, free this turn',
  c='OnUse: GetDistinctForCombat(3, unlocked Colorless, CombatCardGeneration) -> ChooseCard (canSkip) -> SetToFreeThisTurn -> Hand',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'OnUse', 'generated (3)', '1', '1', 'canSkip=true')], g='RunRng.CombatCardGeneration')
Q('CosmicConcoction', 3, 4, m='3 upgraded colorless cards',
  c='OnUse: GetDistinctForCombat(3, unlocked Colorless, CombatCardGeneration) -> Upgrade -> AddGeneratedCardToCombat Hand', g='RunRng.CombatCardGeneration', k='')
Q('CunningPotion', 2, 3, m='3 upgraded Shivs', c='OnUse: Shiv.CreateInHand(3) then CardCmd.Upgrade each', k='Shiv card; Shiv.CreateInHand')
Q('CureAll', 1, 1, m='+1 energy, draw 2', c='OnUse: GainEnergy 1; Draw 2')
Q('DeprecatedPotion', 1, None, m='none (save placeholder)', n='deprecated; never generated')
Q('DexterityPotion', 1, 1, m='+2 Dexterity', c='OnUse: DexterityPower(2) on target')
Q('DistilledChaos', 3, 4, m='auto-play top 3 of draw pile', c='OnUse: CardPileCmd.AutoPlayFromDrawPile(3, Top, forceExhaust:false)', k='CardCmd.AutoPlay; CardPileCmd.AutoPlayFromDrawPile')
Q('DropletOfPrecognition', 3, 5, m='take a card from the draw pile (choice)',
  c='OnUse: FromCombatPile(Draw) 1/1 -> CardPileCmd.Add Hand',
  p=[P('PileSelect', 'CardSelectCmd.FromCombatPile', 'OnUse', 'Draw (sorted by rarity then id)', '1', '1', '')], k='CardPileCmd.Add (partial)')
Q('Duplicator', 1, 2, m='next card played twice', c='OnUse: DuplicationPower(1) on target', k='DuplicationPower (ModifyCardPlayCount)')
Q('EnergyPotion', 1, 1, m='+2 energy', c='OnUse: GainEnergy 2')
Q('EntropicBrew', 3, 4, m='fill potion slots with random potions',
  c='OnUse: while target player HasOpenPotionSlots -> CreateRandomPotionOutOfCombat(CombatPotionGeneration) -> TryToProcure; stop on failure',
  r='OnUse (out of combat)[potion_use]: AnyTime -> same slot filling', g='RunRng.CombatPotionGeneration (also out of combat)', k='potion slots; PotionCmd.TryToProcure')
Q('EssenceOfDarkness', 3, 4, m='channel Dark orbs to fill capacity', c='OnUse: OrbCmd.Channel<DarkOrb> x OrbQueue.Capacity', k='OrbQueue')
Q('ExplosiveAmpoule', 1, 1, m='10 damage to all enemies', c='OnUse: CreatureCmd.Damage all hittable enemies 10 (Unpowered), dealer owner')
Q('FairyInABottle', 3, 4, m='prevents death once and heals 30%',
  c='ShouldDie: owner -> false | AfterPreventingDeath: OnUseWrapper(Throwing ctx) -> RemoveBeforeUse, BeforePotionUsed, Heal max(MaxHp x 0.3, 1), AfterPotionUsed',
  k='automatic use path (PotionUsage.Automatic: ShouldDie gate + OnUseWrapper from AfterPreventingDeath)', n='cannot be generated in combat; never manually usable')
Q('FirePotion', 1, 1, m='20 damage to an enemy', c='OnUse: CreatureCmd.Damage target 20 (Unpowered)')
Q('FlexPotion', 1, 2, m='+5 temporary Strength', c='OnUse: FlexPotionPower(5) on target', k='FlexPotionPower (temporary Strength)')
Q('FocusPotion', 1, 2, m='+2 Focus', c='OnUse: FocusPower(2) on target', k='FocusPower')
Q('Fortifier', 1, 1, m='triple block', c='OnUse: GainBlock target.Block x 2 (Unpowered)')
Q('FoulPotion', 4, 6, m='in combat 12 damage to everyone; in shops gold',
  c='OnUse (combat, AllEnemies): CreatureCmd.Damage every non-pet creature (player included) 12 (Unpowered)',
  r='OnUse (out of combat, TargetedNoCreature)[shop]: MerchantRoom -> GainGold 100 and merchant reaction; FakeMerchant event room -> event reaction | PassesCustomUsabilityCheck[potion_use]: in combat, or shop/fake merchant with closed inventory',
  k='node-coupled usability (GetFoulPotionMerchantTarget reads NMerchantRoom/NFakeMerchant); merchant/event callbacks', n='TargetType is a getter that depends on combat state')
Q('FruitJuice', 1, 1, m='+5 max HP', c='OnUse: GainMaxHp 5 on target', r='OnUse (out of combat)[potion_use]: AnyTime -> same max HP gain', n='cannot be generated in combat')
Q('FyshOil', 1, 1, m='+1 Strength and Dexterity', c='OnUse: StrengthPower(1); DexterityPower(1) on target')
Q('GamblersBrew', 3, 5, m='discard any number and redraw',
  c='OnUse: FromHandForDiscard 0..inf -> CardCmd.DiscardAndDraw(selected, count)',
  p=[P('HandSelect', 'CardSelectCmd.FromHandForDiscard', 'OnUse', 'Hand', '0', '999999999', 'UsePotionAction context (inline)')], k='CardCmd.DiscardAndDraw')
Q('GhostInAJar', 1, 2, m='1 Intangible', c='OnUse: IntangiblePower(1) on target', k='IntangiblePower')
Q('GigantificationPotion', 1, 2, m='Gigantification', c='OnUse: GigantificationPower(1) on target', k='GigantificationPower')
Q('GlowwaterPotion', 1, 1, m='exhaust hand, draw 10', c='OnUse: CardCmd.Exhaust every hand card; Draw 10')
Q('HeartOfIron', 1, 2, m='7 Plating', c='OnUse: PlatingPower(7) on target', k='PlatingPower')
Q('KingsCourage', 3, 4, m='Forge 15', c='OnUse: ForgeCmd.Forge(15, target player)', k='Forge')
Q('LiquidBronze', 1, 1, m='3 Thorns', c='OnUse: ThornsPower(3) on target')
Q('LiquidMemories', 3, 5, m='take a discard-pile card, free this turn',
  c='OnUse: FromCombatPile(Discard) 1/1 -> SetToFreeThisTurn -> CardPileCmd.Add Hand',
  p=[P('PileSelect', 'CardSelectCmd.FromCombatPile', 'OnUse', 'Discard (pile order)', '1', '1', '')], k='CardModel.SetToFreeThisTurn; CardPileCmd.Add (partial)')
Q('LuckyTonic', 1, 2, m='1 Buffer', c='OnUse: BufferPower(1) on target', k='BufferPower')
Q('MazalethsGift', 1, 2, m='1 Ritual', c='OnUse: RitualPower(1) on target', k='RitualPower')
Q('MockDiscardAndAddShivsPotion', 3, None, m='test mock: discard 2, add 2 Shivs',
  c='OnUse: FromHandForDiscard 2/2 -> CardCmd.Discard; Shiv.CreateInHand(2)',
  p=[P('HandSelect', 'CardSelectCmd.FromHandForDiscard', 'OnUse', 'Hand', '2', '2', 'mock')], n='mock potion (IsMock); not ported')
Q('OrobicAcid', 3, 4, m='random free Attack, Skill and Power',
  c='OnUse: GetDistinctForCombat(1) from character pool Attacks, then Skills, then Powers (CombatCardGeneration) -> SetToFreeThisTurn each -> AddGeneratedCardsToCombat Hand',
  g='RunRng.CombatCardGeneration (three TakeRandom calls: Attack, Skill, Power)', k='')
Q('PoisonPotion', 1, 2, m='6 Poison on an enemy', c='OnUse: PoisonPower(6) on target enemy', k='PoisonPower')
Q('PotOfGhouls', 2, 3, m='2 Souls in hand', c='OnUse: Soul.CreateInHand(2)', k='Soul card; Soul.CreateInHand')
Q('PotionOfBinding', 1, 1, m='Weak 1 then Vulnerable 1 on all enemies',
  c='OnUse: WeakPower on all hittable enemies, then VulnerablePower', n='amounts read from swapped keys (Weak uses DynamicVars["VulnerablePower"], Vulnerable uses ["WeakPower"]); both are 1 (Models.Potions/PotionOfBinding.cs:91, :107)')
Q('PotionOfCapacity', 3, 4, m='+2 orb slots', c='OnUse: OrbCmd.AddSlots(target player, 2)', k='OrbQueue')
Q('PotionOfDoom', 2, 4, m='33 Doom on an enemy', c='OnUse: DoomPower(33) on target enemy', k='DoomPower (doom kill at side turn end, Hook.AfterDiedToDoom)')
Q('PotionShapedRock', 1, 1, m='15 damage to an enemy', c='OnUse: CreatureCmd.Damage target 15 (Unpowered)', n='Token rarity; procured by PetrifiedToad')
Q('PowderedDemise', 1, 2, m='9 Demise on an enemy', c='OnUse: DemisePower(9) on target enemy', k='DemisePower')
Q('PowerPotion', 3, 5, m='choose 1 of 3 random Powers, free this turn',
  c='OnUse: GetDistinctForCombat(3, character pool Powers, CombatCardGeneration) -> ChooseCard (canSkip) -> SetToFreeThisTurn -> Hand',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'OnUse', 'generated (3)', '1', '1', 'canSkip=true')], g='RunRng.CombatCardGeneration')
Q('RadiantTincture', 1, 2, m='+1 energy; 3 Radiance', c='OnUse: GainEnergy 1; RadiancePower(3) on target', k='RadiancePower')
Q('RegenPotion', 1, 2, m='5 Regen', c='OnUse: RegenPower(5) on target', k='RegenPower', n='cannot be generated in combat')
Q('ShacklingPotion', 1, 2, m='enemies lose 7 Strength this turn', c='OnUse: ShacklingPotionPower(7) on all hittable enemies', k='ShacklingPotionPower (temporary Strength loss)')
Q('ShipInABottle', 1, 2, m='10 block now and next turn', c='OnUse: GainBlock 10; BlockNextTurnPower(10) on target', k='BlockNextTurnPower')
Q('SkillPotion', 3, 5, m='choose 1 of 3 random Skills, free this turn',
  c='OnUse: GetDistinctForCombat(3, character pool Skills, CombatCardGeneration) -> ChooseCard (canSkip) -> SetToFreeThisTurn -> Hand',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'OnUse', 'generated (3)', '1', '1', 'canSkip=true')], g='RunRng.CombatCardGeneration')
Q('SneckoOil', 2, 3, m='draw 7, randomize hand costs',
  c='OnUse: Draw 7; every non-X hand card with cost>=0 -> EnergyCost.SetThisTurnOrUntilPlayed(CombatEnergyCosts.NextInt(4))',
  g='RunRng.CombatEnergyCosts (one NextInt(4) per eligible hand card, hand order)', k='CardEnergyCost.SetThisTurnOrUntilPlayed')
Q('SoldiersStew', 2, 3, m='Strike cards replay once more', c='OnUse: every Strike-tagged card in all combat piles -> BaseReplayCount++', k='CardModel.BaseReplayCount (replay plays)')
Q('SpeedPotion', 1, 2, m='+5 temporary Dexterity', c='OnUse: SpeedPotionPower(5) on target', k='SpeedPotionPower (temporary Dexterity)')
Q('StableSerum', 1, 2, m='retain hand for 2 turns', c='OnUse: RetainHandPower(2) on target', k='RetainHandPower (ShouldFlush)')
Q('StarPotion', 3, 4, m='+3 stars', c='OnUse: PlayerCmd.GainStars(3, target player)', k='Stars')
Q('StrengthPotion', 1, 1, m='+2 Strength', c='OnUse: StrengthPower(2) on target')
Q('SwiftPotion', 1, 1, m='draw 3', c='OnUse: Draw 3')
Q('TouchOfInsanity', 3, 5, m='a hand card becomes free this combat (choice)',
  c='OnUse: FromHand 1/1 (cards costing energy or stars) -> SetToFreeThisCombat',
  p=[P('HandSelect', 'CardSelectCmd.FromHand', 'OnUse', 'Hand (CostsEnergyOrStars)', '1', '1', '')], k='CardModel.SetToFreeThisCombat')
Q('VulnerablePotion', 1, 1, m='3 Vulnerable on an enemy', c='OnUse: VulnerablePower(3) on target enemy')
Q('WeakPotion', 1, 1, m='3 Weak on an enemy', c='OnUse: WeakPower(3) on target enemy')
