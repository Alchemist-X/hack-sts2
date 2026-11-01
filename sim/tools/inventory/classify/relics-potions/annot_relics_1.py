from annot_common import R, P

R('Akabeko', 1, 1, None, 2, m='apply Vigor (self)',
  c='AfterSideTurnStart: owner participates and TurnNumber<=1 -> PowerCmd.Apply VigorPower(8) to owner')
R('AlchemicalCoffer', 3, None, 'simple', 7, m='+potion slots; procure random potions',
  r='AfterObtained[pickup]: PlayerCmd.GainMaxPotionCount(4); PotionFactory.CreateRandomPotionsOutOfCombat(4, RunRng.CombatPotionGeneration); TryToProcure each into slot originalCount+i',
  g='RunRng.CombatPotionGeneration (per potion: NextFloat rarity, NextItem)', a='indirect: A4 TightBelt lowers the starting slot count the new slots are added to')
R('AmethystAubergine', 3, None, 'modifier', 8, m='extra gold reward',
  r='TryModifyRewards[rewards]: combat room (not the boss of the last act) -> add GoldReward(15) | AfterModifyingRewards[rewards]: flash',
  n='IsAllowed: IsBeforeAct3TreasureChest; not in shops', a='A10 DoubleBoss: both bosses of the last act are excluded by the CurrentActIndex test')
R('Anchor', 1, 1, None, 1, m='block at combat start',
  c='BeforeCombatStart: GainBlock 10 (Unpowered) on owner')
R('ArcaneScroll', 3, None, 'simple', 8, m='add random Rare character card to deck',
  r='AfterObtained[pickup]: CardFactory.CreateForReward(1, character pool, Uniform odds, Rare only, NoUpgradeRoll) -> CardPileCmd.Add Deck',
  g='PlayerRng.Rewards (CreateForReward NextItem)', k='Hook.ModifyCardRewardCreationOptions + TryModifyCardRewardOptions run inside CreateForReward')
R('ArchaicTooth', 3, None, 'simple', 7, m='transform starter card into its ancient version',
  r='AfterObtained[pickup]: first deck card among Bash/Neutralize/Unleash/FallingStar/Dualcast -> CardCmd.Transform to Break/Suppress/Protector/MeteorShower/Quadcast (keeps upgrade and enchantment) | SetupForPlayer[offer]: stores StarterCard/AncientCard (SavedProperty) when offered by Orobas',
  s='StarterCard, AncientCard (SerializableCard, SavedProperty, display only)',
  q='AfterObtained calls Transform with a null card when the deck has no transcendence starter, and offer-time SetupForPlayer returns false then (Models.Relics/ArchaicTooth.cs:156-168): confirm the ancient never offers it in that case')
R('ArtOfWar', 2, 2, None, 3, m='energy if no attack played last turn',
  c='AfterCardPlayed: owner Attack -> AnyAttacksPlayedThisTurn=true (unless played last turn) | AfterSideTurnEnd: LastTurn=ThisTurn; ThisTurn=false | AfterEnergyReset: TurnNumber>1 and no attack last turn -> GainEnergy 1; then both flags false | AfterCombatEnd: reset flags',
  s='_anyAttacksPlayedLastTurn/_anyAttacksPlayedThisTurn (combat-scoped bools)', n='AfterCombatEnd reset is rule-relevant (stale flags would leak into the next combat)')
R('Astrolabe', 3, None, 'prompt', 9, m='transform 3 deck cards (choice) and upgrade them',
  r='AfterObtained[pickup]: FromDeckForTransformation 3/3 -> each: CardFactory.CreateRandomCardForTransform(isInCombat:false, RunRng.Niche), CardCmd.Upgrade, CardCmd.Transform',
  p=[P('DeckTransform', 'CardSelectCmd.FromDeckForTransformation', 'AfterObtained', 'Deck', '3', '3', 'TransformSelectionPrompt')],
  g='RunRng.Niche (one NextItem per selected card, selection order)')
R('BagOfMarbles', 1, 1, None, 1, m='apply Vulnerable to all enemies turn 1',
  c='BeforeSideTurnStart: owner participates and TurnNumber<=1 -> PowerCmd.Apply VulnerablePower(1) to all hittable enemies')
R('BagOfPreparation', 1, 1, None, 1, m='+2 hand draw turn 1', c='ModifyHandDraw: owner, TurnNumber<=1 -> +2')
R('BeatingRemnant', 1, 1, None, 1, m='cap HP loss per turn',
  c='ModifyHpLostAfterOsty: in combat, target owner -> min(amount, 20 - DamageReceivedThisTurn) | AfterModifyingHpLostAfterOsty: flash | AfterDamageReceived: owner -> DamageReceivedThisTurn += result.UnblockedDamage | BeforeSideTurnStart: owner participates -> reset 0',
  s='_damageReceivedThisTurn (decimal, reset at player side-turn start)', n='accumulates UnblockedDamage (not HP lost); window runs from the player turn start through the enemy turn')
R('BeautifulBracelet', 3, None, 'simple', 7, m='enchant 4 random deck cards with Swift 2',
  r='AfterObtained[pickup]: deck cards Swift.CanEnchant -> TakeRandom(4, RunRng.Niche) -> CardCmd.Enchant<Swift>(2)',
  g='RunRng.Niche (TakeRandom)', k='enchantments (Swift)')
R('Bellows', 2, 2, None, 3, m='upgrade hand turn 1', c='AfterPlayerTurnStart: owner, TurnNumber<=1 -> CardCmd.Upgrade all hand cards')
R('BeltBuckle', 3, 3, None, 4, m='+2 Dexterity while no potions held',
  c='BeforeCombatStart: DexterityApplied=false; no potion held -> apply DexterityPower(2) (sets DexterityApplied) | AfterPotionUsed / AfterPotionDiscarded: in combat, no potion held, not applied -> apply DexterityPower(2) | AfterPotionProcured: in combat, a potion held, applied -> DexterityPower(-2) and clear | AfterCombatVictory: DexterityApplied=false | AfterCombatEnd: status only',
  r='AfterObtained[pickup]: if obtained in combat and no potions -> apply (unreachable in v0.111.0: no mid-combat relic obtain)',
  s='_dexterityApplied (bool, cleared AfterCombatVictory)', k='')
R('BigHat', 3, 3, None, 4, m='generate 2 Ethereal cards turn 1',
  c='AfterSideTurnStart: turn<=1 -> CardFactory.GetDistinctForCombat(2, character pool unlocked cards with Ethereal, RunRng.CombatCardGeneration) -> AddGeneratedCardsToCombat Hand',
  g='RunRng.CombatCardGeneration (TakeRandom)')
R('BigMushroom', 3, 1, 'simple', 7, m='+20 max HP; -2 draw turn 1',
  c='ModifyHandDraw: TurnNumber==1 -> -2', r='AfterObtained[pickup]: GainMaxHp 20 | AfterRoomEntered[room_entry]: visual scale only')
R('BiiigHug', 3, 2, 'prompt', 9, m='remove 4 deck cards (choice); Soot on each shuffle',
  c='AfterShuffle: owner shuffled -> create Soot, AddGeneratedCardToCombat Draw at Random position',
  r='AfterObtained[pickup]: FromDeckForRemoval 4/4 -> CardPileCmd.RemoveFromDeck',
  p=[P('DeckRemove', 'CardSelectCmd.FromDeckForRemoval', 'AfterObtained', 'Deck', '4', '4', 'RemoveSelectionPrompt')],
  g='RunRng.Shuffle (random insert position NextInt(count+1), Commands/CardPileCmd.cs:3431)')
R('BingBong', 3, None, 'simple', 7, m='duplicate every card added to the deck',
  r='AfterCardChangedPiles[deck_change]: card entered Deck, owner, not clonedBy, not a skip-listed clone -> RunState.CloneCard -> CardPileCmd.Add Deck Bottom (clonedBy=this)',
  s='_cardsToSkip (HashSet of clones, transient)', k='Hook.AfterCardChangedPiles (both scope); RunState.CloneCard')
R('BlackBlood', 2, 2, None, 3, m='heal 12 after combat win', c='AfterCombatVictory: owner alive -> Heal 12',
  n='upgraded Burning Blood; obtained through TouchOfOrobas')
R('BlackStar', 3, None, 'modifier', 8, m='extra relic reward from elites',
  r='TryModifyRewards[rewards]: elite room -> add RelicReward(player)', g='PlayerRng.Rewards (relic rarity roll at reward population) + player relic grab bag',
  a='indirect: A1 SwarmingElites (more elites)')
R('BlessedAntler', 2, 2, None, 3, m='+1 max energy; 3 Dazed into draw pile turn 1',
  c='ModifyMaxEnergy: +1 | BeforeHandDraw: TurnNumber==1 -> 3x CreateCard<Dazed> -> AddGeneratedCardsToCombat Draw Random',
  g='RunRng.Shuffle (random insert per card)')
R('BloodSoakedRose', 3, 1, 'simple', 7, m='+1 max energy; Enthralled curse',
  c='ModifyMaxEnergy: +1', r='AfterObtained[pickup]: CardPileCmd.AddCurseToDeck<Enthralled>', k='Enthralled needs AutoPlayType on ShouldPlay')
R('BloodVial', 1, 1, None, 1, m='heal 2 turn 1', c='AfterPlayerTurnStartLate: owner, TurnNumber<=1 -> Heal 2')
R('BoneFlute', 3, 3, None, 5, m='block when Osty attacks', c='AfterAttack: attacker is Osty owned by owner -> GainBlock 2 (Unpowered)', k='Osty pet')
R('BoneTea', 2, 2, None, 3, m='upgrade hand turn 1 (1 combat)',
  c='AfterPlayerTurnStart: not used up, owner, TurnNumber<=1 -> CardCmd.Upgrade each hand card; CombatsLeft--',
  s='CombatsLeft=1 (SavedProperty charges; IsUsedUp at 0)')
R('Bookmark', 2, 2, None, 3, m='retained card costs 1 less',
  c='AfterFlush: owner -> among retained cards with non-X cost>0 pick RunRng.CombatCardSelection.NextItem -> EnergyCost.AddUntilPlayed(-1)',
  g='RunRng.CombatCardSelection', k='Flush retained-cards list on AfterFlush; CardEnergyCost.AddUntilPlayed')
R('BookOfFiveRings', 3, None, 'simple', 7, m='heal 20 every 5 cards added to deck',
  r='AfterCardChangedPiles[deck_change]: owner alive, card entered Deck -> CardsAdded++; every 5th -> Heal 20',
  s='CardsAdded (int, SavedProperty, persistent counter)', n='IsAllowed: IsBeforeAct3TreasureChest', k='Hook.AfterCardChangedPiles (both scope)')
R('BookRepairKnife', 2, 2, None, 3, m='heal per enemy killed by Doom',
  c='AfterDiedToDoom: heal 3 x count of doomed creatures (not owner, all powers ShouldOwnerDeathTriggerFatal)', k='Doom (DoomPower kill at side turn end)')
R('BoomingConch', 2, 2, None, 3, m='elite: +2 draw and +1 energy turn 1',
  c='ModifyHandDraw: turn<=1 in Elite room -> +2 | AfterSideTurnStart: turn<=1 in Elite room -> GainEnergy 1',
  k='combat context: RunState.CurrentRoom.RoomType', a='indirect: A1 SwarmingElites (more elites)')
R('BoundPhylactery', 3, 3, None, 5, m='summon Osty',
  c='BeforeCombatStart: OstyCmd.Summon 1 | AfterEnergyResetLate: owner, TurnNumber!=1 -> OstyCmd.Summon 1', k='Osty pet; OstyCmd.Summon', n='Necrobinder starter')
R('BowlerHat', 3, 2, 'modifier', 8, m='gold gained x1.25',
  c='ModifyGoldGained (both scope): owner -> amount x1.25 | AfterModifyingGoldGained: flash', r='ModifyGoldGained[gold]: same multiplier on run-layer gold',
  n='IsAllowed: IsBeforeAct3TreasureChest; not in shops', k='Hook.ModifyGoldGained; PlayerCmd.GainGold', a='indirect: A3 Poverty reduces encounter gold before the multiplier')
R('Bread', 1, 1, None, 1, m='+1 max energy after turn 1; -2 energy turn 1',
  c='ModifyMaxEnergy: not turn 1 -> +1 | AfterSideTurnStart: owner participates, TurnNumber==1 -> LoseEnergy 2')
R('BrilliantScarf', 1, 1, None, 1, m='5th card each turn is free',
  c='TryModifyEnergyCostInCombatLate / TryModifyStarCost: card in Hand/Play and CardsPlayedThisTurn==4 -> 0 | AfterCardPlayed: non-autoplay owner card -> count++ | BeforeSideTurnStart: reset | AfterCombatEnd: reset (redundant)',
  s='_cardsPlayedThisTurn (turn-scoped)', k='CardPlay.IsAutoPlay; card pile type in cost hooks')
R('Brimstone', 1, 1, None, 1, m='+2 Strength self, +1 Strength enemies each turn',
  c='AfterSideTurnStart: owner participates -> apply StrengthPower(2) to owner; then StrengthPower(1) to all alive opponents')
R('BronzeScales', 2, 2, None, 3, m='3 Thorns at combat start',
  c='AfterRoomEntered (combat room): apply ThornsPower(3) to owner', k='')
R('BurningBlood', 2, 2, None, 3, m='heal 6 after combat win', c='AfterCombatVictory: owner alive -> Heal 6', n='Ironclad starter')
R('BurningSticks', 2, 2, None, 3, m='first exhausted Skill each combat returns as a copy',
  c='AfterCardExhausted: owner Skill, not used this combat -> card.CreateClone -> AddGeneratedCardToCombat Hand; used=true | AfterRoomEntered (combat room): reset | AfterCombatEnd: reset',
  s='_wasUsedThisCombat (combat-scoped)', k='Hook.AfterRoomEntered (combat room); CardModel.CreateClone')
R('Byrdpip', 3, 3, 'simple', 5, m='pet Byrdpip; transform Byrdonis Eggs',
  c='BeforeCombatStart: PlayerCmd.AddPet<Monsters.Byrdpip>',
  r='AfterObtained[pickup]: Skin = new Rng(player,id).NextItem (cosmetic); transform every ByrdonisEgg in deck (and combat piles if in combat) to ByrdSwoop; in combat -> summon pet',
  s='Skin (string, SavedProperty, cosmetic)', g='Rng(player, id) model-seeded (cosmetic skin only)', k='CardCmd.TransformTo')
R('CallingBell', 3, None, 'prompt', 8, m='curse + choose from 3 relic rewards',
  r='AfterObtained[pickup]: AddCurseToDeck<CurseOfTheBell>; RewardsCmd.OfferCustom [RelicReward(Common), RelicReward(Uncommon), RelicReward(Rare)]',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '3 relic rewards', None, None, 'TestMode list differs')],
  g='player relic grab bag (rarity fixed, no roll)')
R('Candelabra', 1, 1, None, 1, m='+2 energy turn 2', c='AfterSideTurnStart: owner participates, TurnNumber==2 -> GainEnergy 2')
R('CaptainsWheel', 1, 1, None, 1, m='18 block on turn 3', c='AfterBlockCleared: owner, TurnNumber==3 -> GainBlock 18')
R('Cauldron', 3, None, 'prompt', 8, m='choose from 5 potion rewards',
  r='AfterObtained[pickup]: RewardsCmd.OfferCustom 5x PotionReward(player)',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '5 potion rewards', None, None, '')],
  g='PlayerRng.Rewards (PotionFactory.CreateRandomPotionOutOfCombat per reward)')
R('CentennialPuzzle', 2, 2, None, 3, m='draw 3 on first unblocked damage each combat',
  c='AfterDamageReceived: in combat, owner, UnblockedDamage>0, not used -> draw 1 three times | AfterCombatEnd: reset', s='_usedThisCombat (combat-scoped)')
R('Chandelier', 1, 1, None, 1, m='+3 energy turn 3', c='AfterSideTurnStart: owner participates, TurnNumber==3 -> GainEnergy 3')
R('CharonsAshes', 1, 1, None, 1, m='3 damage to all enemies per exhaust',
  c='AfterCardExhausted: owner card -> CreatureCmd.Damage all hittable enemies 3 (Unpowered)')
R('ChemicalX', 1, 1, None, 1, m='X costs +2', c='ModifyXValue: owner card -> +2 | BeforeCardPlayed: flash on X cost')
R('ChoicesParadox', 3, 3, None, 6, m='choose 1 of 5 generated cards with Retain turn 1',
  c='AfterPlayerTurnStart: owner, TurnNumber==1 -> GetDistinctForCombat(5, character pool, RunRng.CombatCardGeneration); ApplyKeyword Retain each; FromSimpleGrid 1/1 -> AddGeneratedCardToCombat Hand',
  p=[P('SimpleGrid', 'CardSelectCmd.FromSimpleGrid', 'AfterPlayerTurnStart', 'generated (5)', '1', '1', 'turn-setup deferral (SetupPlayerTurn context)')],
  g='RunRng.CombatCardGeneration', k='CardCmd.ApplyKeyword')
R('ChosenCheese', 2, 2, None, 3, m='+1 max HP after every combat', c='AfterCombatEnd: GainMaxHp 1', n='run-level effect applied at combat end')
R('Circlet', 1, None, None, 1, m='none (fallback relic, stackable)', s='StackCount (stackable)', n='RelicFactory.FallbackRelic; RandomCharacter starter')
R('Claws', 3, None, 'prompt', 9, m='transform up to 6 cards into Maul',
  r='AfterObtained[pickup]: FromDeckForTransformation 0..6 (RequireManualConfirmation, not cancelable) -> CardCmd.Transform each into Maul (keeps upgrade and enchantment)',
  p=[P('DeckTransform', 'CardSelectCmd.FromDeckForTransformation', 'AfterObtained', 'Deck', '0', '6', 'RMC=true; Cancelable=false')],
  g='PlayerRng.Transformations passed but not consumed (fixed replacement, Entities.Cards/CardTransformation.cs:167-173)')
R('CloakClasp', 1, 1, None, 1, m='end of turn block per card in hand',
  c='BeforeSideTurnEnd: owner participates -> GainBlock (int)(hand count x 1) Unpowered')
R('CrackedCore', 3, 3, None, 5, m='channel 1 Lightning turn 1',
  c='BeforeSideTurnStart: owner participates, TurnNumber<=1 -> OrbCmd.Channel<LightningOrb> once', k='OrbQueue; OrbCmd.Channel', n='Defect starter')
R('Crossbow', 3, 3, None, 4, m='each turn: random Attack, free this turn',
  c='AfterSideTurnStart: owner participates -> GetDistinctForCombat(1, character pool Attacks, RunRng.CombatCardGeneration) -> SetToFreeThisTurn -> AddGeneratedCardsToCombat Hand',
  g='RunRng.CombatCardGeneration')
R('CursedPearl', 3, None, 'simple', 7, m='Greed curse; +333 gold', r='AfterObtained[pickup]: AddCurseToDeck<Greed>; GainGold 333', n='Neow curse option')
R('DarkstonePeriapt', 3, None, 'simple', 7, m='+6 max HP per curse added',
  r='AfterCardChangedPiles[deck_change]: owner Curse entered Deck -> GainMaxHp 6', k='Hook.AfterCardChangedPiles (both scope)')
R('DataDisk', 2, 2, None, 3, m='1 Focus at combat start', c='AfterRoomEntered (combat room): apply FocusPower(1) to owner',
  k='Hook.AfterRoomEntered (combat room); FocusPower (orb values)')
R('DaughterOfTheWind', 1, 1, None, 1, m='1 block per Attack played', c='AfterCardPlayed: owner Attack -> GainBlock 1 (fast)')
R('DelicateFrond', 3, 3, None, 4, m='fill empty potion slots at combat start',
  c='BeforeCombatStart: while HasOpenPotionSlots -> CreateRandomPotionOutOfCombat(RunRng.CombatPotionGeneration) -> TryToProcure; stop on failure',
  g='RunRng.CombatPotionGeneration (per potion NextFloat + NextItem)', k='potion slots; PotionCmd.TryToProcure')
R('DemonTongue', 1, 1, None, 1, m='heal first unblocked damage on own turn',
  c='AfterDamageReceived: own side turn, owner, UnblockedDamage>0, not triggered -> Heal UnblockedDamage | BeforeSideTurnStart: reset', s='_triggeredThisTurn (turn-scoped)')
R('DeprecatedRelic', 1, None, None, 10, m='none (save-compatibility placeholder)', n='deprecated; never offered')
R('DiamondDiadem', 1, 1, None, 2, m='20 block + Blur turn 1',
  c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> GainBlock 20; apply BlurPower(1)', k='BlurPower (ShouldClearBlock)')
R('DingyRug', 3, None, 'modifier', 8, m='card rewards include Colorless',
  r='ModifyCardRewardCreationOptions[card_reward]: IsCardReward and no NoCardPoolModifications -> pools = pools Union Colorless')
R('DistinguishedCape', 3, None, 'simple', 7, m='2 random curses + 3 Apparitions',
  r='AfterObtained[pickup]: 2x Niche.NextItem over unlocked CurseCardPool (CanBeGeneratedByModifiers, sorted by Id, without replacement) -> Add Deck; then 3x Apparition -> Add Deck',
  g='RunRng.Niche (2 NextItem)')
R('DivineDestiny', 3, 3, None, 5, m='7 stars turn 1', c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> GainStars 7', k='Stars', n='upgraded Divine Right (TouchOfOrobas)')
R('DivineRight', 3, 3, None, 5, m='3 stars at combat start', c='AfterRoomEntered (combat room): GainStars 3', k='Stars; Hook.AfterRoomEntered (combat room)', n='Regent starter')
R('DollysMirror', 3, None, 'prompt', 9, m='duplicate a deck card (choice)',
  r='AfterObtained[pickup]: FromDeckGeneric 1/1 (filter: not Quest) -> RunState.CloneCard -> Add Deck',
  p=[P('DeckGeneric', 'CardSelectCmd.FromDeckGeneric', 'AfterObtained', 'Deck (non-Quest)', '1', '1', '')])
R('DowsingRod', 3, None, 'simple', 7, m='add Dowsing card', r='AfterObtained[pickup]: CreateCard<Dowsing> -> Add Deck', n='new in v0.111.0')
R('DragonFruit', 3, None, 'simple', 7, m='+1 max HP per gold gain', r='AfterGoldGained[gold]: owner -> GainMaxHp 1', k='Hook.AfterGoldGained (run scope)',
  n='IsAllowed: IsBeforeAct3TreasureChest')
R('DreamCatcher', 3, None, 'modifier', 8, m='card reward after resting',
  r='TryModifyRestSiteHealRewards[rest]: add CardReward(ForRoom Monster, 3) | ModifyExtraRestSiteHealText[rest]: UI text',
  g='PlayerRng.Rewards (card reward generation)', a='indirect: A7 Scarcity card rarity/upgrade odds')
R('Driftwood', 3, None, 'modifier', 8, m='card rewards can be rerolled', r='TryModifyRewardsLate[rewards]: every CardReward.CanReroll=true',
  k='card reward reroll action (CardReward.CanReroll)', q='reroll RNG consumption and the reroll option source live in Rewards/CardReward.cs and are not re-derived here')
R('DustyTome', 3, None, 'simple', 7, m='add an upgraded random Ancient card',
  r='SetupForPlayer[offer]: AncientCard = PlayerRng.Rewards.NextItem(character Ancient-rarity cards minus transcendence cards) | AfterObtained[pickup]: CreateCard(AncientCard), Upgrade, Add Deck',
  s='AncientCard (ModelId, SavedProperty)', g='PlayerRng.Rewards at offer time (Darv)')
R('Ectoplasm', 3, 2, 'modifier', 8, m='+1 max energy; no gold gain',
  c='ModifyMaxEnergy: +1 | ModifyGoldGained: owner -> 0', r='ModifyGoldGained[gold]: 0 for all gold gains | AfterModifyingGoldGained[gold]: flash', k='Hook.ModifyGoldGained')
R('ElectricShrymp', 3, None, 'prompt', 9, m='enchant a card with Imbued',
  r='AfterObtained[pickup]: FromDeckForEnchantment(Imbued, 1) 1/1 -> CardCmd.Enchant Imbued(1)',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment', 'AfterObtained', 'Deck (Imbued.CanEnchant)', '1', '1', 'EnchantSelectionPrompt')], k='enchantments (Imbued: auto-play)')
R('EmberTea', 2, 2, None, 3, m='+2 Strength for 5 combats',
  c='AfterRoomEntered (combat room): not used up -> apply StrengthPower(2); CombatsLeft--', s='CombatsLeft=5 (SavedProperty charges)')
R('EmotionChip', 3, 3, None, 5, m='trigger orb passives if HP lost last turn',
  c='AfterPlayerTurnStart: owner and a DamageReceivedEntry (owner, not fully blocked) happened last player turn -> OrbCmd.Passive each orb (countAffectedByHooks) | AfterDamageReceived / AfterCombatEnd: status only',
  k='combat history (DamageReceivedEntry.HappenedLastPlayerTurn); OrbCmd.Passive')
R('EmptyCage', 3, None, 'prompt', 9, m='remove 2 cards (choice)', r='AfterObtained[pickup]: FromDeckForRemoval 2/2 -> RemoveFromDeck each',
  p=[P('DeckRemove', 'CardSelectCmd.FromDeckForRemoval', 'AfterObtained', 'Deck', '2', '2', 'RemoveSelectionPrompt')])
R('EternalFeather', 3, None, 'simple', 7, m='heal at rest sites by deck size',
  r='AfterRoomEntered[room_entry]: RestSiteRoom -> Heal 3 x floor(deck count / 5)')
R('FakeAnchor', 1, 1, None, 1, m='4 block at combat start', c='BeforeCombatStart: GainBlock 4', n='FakeMerchant relic (MerchantCost 50)')
R('FakeBloodVial', 1, 1, None, 1, m='heal 1 turn 1', c='AfterPlayerTurnStartLate: owner, TurnNumber<=1 -> Heal 1', n='FakeMerchant relic')
R('FakeHappyFlower', 1, 1, None, 1, m='+1 energy every 5 turns',
  c='AfterSideTurnStart: owner participates -> TurnsSeen=(TurnsSeen+1)%5; 0 -> GainEnergy 1 | AfterCombatEnd: status only',
  s='TurnsSeen (SavedProperty, persists across combats)', n='FakeMerchant relic')
R('FakeLeesWaffle', 3, None, 'simple', 7, m='heal 10% max HP on pickup', r='AfterObtained[pickup]: Heal MaxHp x 10/100', n='FakeMerchant relic')
R('FakeMango', 3, None, 'simple', 7, m='+3 max HP', r='AfterObtained[pickup]: GainMaxHp 3', n='FakeMerchant relic')
R('FakeMerchantsRug', 1, None, None, 1, m='none', n='FakeMerchant relic with no effect')
R('FakeOrichalcum', 1, 1, None, 1, m='3 block at end of turn when unblocked',
  c='BeforeSideTurnEndVeryEarly: owner participates, Block==0 -> ShouldTrigger | BeforeSideTurnEnd: ShouldTrigger -> GainBlock 3 | BeforeSideTurnStart: reset',
  s='_shouldTrigger (turn-scoped)', n='FakeMerchant relic')
R('FakeSneckoEye', 1, 1, None, 2, m='Confused at combat start',
  c='BeforeCombatStart: apply ConfusedPower(1) to owner', r='AfterObtained[pickup]: in combat -> apply (unreachable in v0.111.0)',
  g='RunRng.CombatEnergyCosts (inside ConfusedPower)', k='ConfusedPower', n='FakeMerchant relic; TestEnergyCostOverride is a TestMode seam')
R('FakeStrikeDummy', 1, 1, None, 1, m='+1 damage on Strike cards',
  c='ModifyDamageAdditive: powered attack from an owner card tagged Strike -> +1', n='FakeMerchant relic')
R('FakeVenerableTeaSet', 3, 1, 'simple', 7, m='+1 energy in the combat after resting',
  c='AfterEnergyReset: flag set -> GainEnergy 1; flag=false', r='AfterRoomEntered[room_entry]: RestSiteRoom -> GainEnergyInNextCombat=true',
  s='GainEnergyInNextCombat (bool, SavedProperty)', n='FakeMerchant relic')
R('FencingManual', 3, 3, None, 5, m='Forge 10 turn 1', c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> ForgeCmd.Forge 10', k='Forge (SovereignBlade)')
R('FestivePopper', 1, 1, None, 1, m='9 damage to all enemies turn 1',
  c='AfterPlayerTurnStart: owner, TurnNumber==1 -> CreatureCmd.Damage all hittable enemies DamageVar 9 (Unpowered)', k='CreatureCmd.Damage(DamageVar) overload')
R('Fiddle', 1, 1, None, 1, m='+2 hand draw; no other draws on own turn',
  c='ModifyHandDraw: +2 | ShouldDraw: owner, not hand draw, own side turn -> false | AfterPreventingDraw: flash')
R('FishingRod', 3, None, 'simple', 7, m='every 3 normal combats upgrade a random card',
  r='AfterCombatEnd[combat_end]: encounter RoomType Monster -> CombatsSeen++; every 3rd -> Niche.NextItem(upgradable deck cards) -> CardCmd.Upgrade',
  s='CombatsSeen (SavedProperty counter)', g='RunRng.Niche')
R('ForgottenSoul', 1, 1, None, 1, m='1 damage to a random enemy per exhaust',
  c='AfterCardExhausted: owner card -> CombatTargets.NextItem(hittable enemies) -> Damage 1 (Unpowered)', g='RunRng.CombatTargets')
R('FragrantMushroom', 3, None, 'simple', 7, m='lose 15 HP; upgrade 2 random cards',
  r='AfterObtained[pickup]: CreatureCmd.Damage owner 15 (Unblockable|Unpowered); StableShuffle(upgradable deck cards, Niche) take 2 -> Upgrade',
  g='RunRng.Niche (StableShuffle)', q='CreatureCmd.Damage outside combat: confirm HP-loss hooks and death handling in the run layer')
R('FresnelLens', 3, None, 'modifier', 8, m='Nimble 2 on reward, shop and new deck cards',
  r='TryModifyCardRewardOptionsLate[card_reward]: clone+Enchant Nimble(2) valid options | ModifyMerchantCardCreationResults[shop]: same | TryModifyCardBeingAddedToDeck[deck_change]: replace card with enchanted clone',
  k='enchantments (Nimble); Hook.TryModifyCardBeingAddedToDeck', n='Event rarity but listed in the Shared pool')
R('FrozenEgg', 3, None, 'modifier', 8, m='Powers in rewards/shop/deck are upgraded',
  r='TryModifyCardRewardOptionsLate[card_reward]: EggRelicHelper.UpgradeValidCards Power (unless NoHookUpgrades) | ModifyMerchantCardCreationResults[shop]: same | TryModifyCardBeingAddedToDeck[deck_change]: upgradable Power -> upgraded clone',
  n='IsAllowed: IsBeforeAct3TreasureChest')
R('FuneraryMask', 1, 1, None, 2, m='3 Souls into draw pile turn 1',
  c='BeforeHandDraw: owner, TurnNumber==1 -> 3x CreateCard<Soul> -> AddGeneratedCardToCombat Draw Random', g='RunRng.Shuffle (random insert per card)', k='Soul card')
R('FurCoat', 4, 2, 'cross-cutting', 10, m='marks 8 map combats; enemies there start at 1 HP',
  c='BeforeCombatStart: current map point marked -> SetCurrentHp 1 on all hittable enemies (history flag IsAffectedByFurCoat) | AfterCreatureAddedToCombat: enemy added in a marked combat -> SetCurrentHp 1',
  r='AfterObtained[pickup]: FurCoatActIndex=current act; mark points | ModifyGeneratedMapLate[map]: same act -> pick 8 Monster/Elite points with UnstableShuffle(Rng(player,id)) or re-apply saved coords; MapPoint.AddQuest',
  s='FurCoatActIndex, FurCoatCoordCols/Rows, FurCoatCoordsSet (SavedProperty)', g='Rng(player, id) model-seeded (UnstableShuffle of candidate points)',
  k='map points with quests; Hook.ModifyGeneratedMapLate; CreatureCmd.SetCurrentHp')
