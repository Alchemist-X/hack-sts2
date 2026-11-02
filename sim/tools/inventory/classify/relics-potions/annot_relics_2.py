from annot_common import R, P

R('GalacticDust', 3, 3, None, 5, m='block per 10 stars spent',
  c='AfterStarsSpent: owner -> StarsSpent += amount; >=10 -> GainBlock floor(StarsSpent/10) x 10 (Unpowered); StarsSpent %= 10',
  s='StarsSpent (SavedProperty, persists across combats)', k='Stars; Hook.AfterStarsSpent')
R('GamblingChip', 3, 3, None, 6, m='discard any number and redraw turn 1',
  c='AfterPlayerTurnStart: owner, TurnNumber<=1 -> FromHandForDiscard 0..inf -> CardCmd.DiscardAndDraw(selected, count)',
  p=[P('HandSelect', 'CardSelectCmd.FromHandForDiscard', 'AfterPlayerTurnStart', 'Hand', '0', '999999999', 'turn-setup deferral (SetupPlayerTurn context); pause happens even for an empty hand')],
  k='CardCmd.DiscardAndDraw')
R('GamePiece', 1, 1, None, 1, m='draw 1 per Power played', c='AfterCardPlayed: owner Power in combat -> draw 1')
R('GhostSeed', 2, 2, None, 3, m='basic Strikes/Defends become Ethereal',
  c='AfterRoomEntered (combat room): every Basic Strike/Defend in combat piles without local Ethereal -> CardCmd.ApplyKeyword Ethereal | AfterCardEnteredCombat: same for new cards',
  k='Hook.AfterRoomEntered (combat room); CardCmd.ApplyKeyword; GetKeywordsWithSources(Local)')
R('Girya', 3, 2, 'prompt', 8, m='Lift at rest sites; Strength per lift',
  c='AfterRoomEntered (combat room): TimesLifted>0 -> apply StrengthPower(TimesLifted)',
  r='TryModifyRestSiteOptions[rest]: TimesLifted<3 -> add LiftRestSiteOption',
  p=[P('RestSiteOption', 'RestSiteOption list', 'TryModifyRestSiteOptions', 'rest site', '1', '1', 'adds the Lift option')],
  s='TimesLifted (SavedProperty counter, max 3)', n='IsAllowed: IsBeforeAct3TreasureChest; LiftRestSiteOption increments TimesLifted')
R('GlassEye', 4, None, 'prompt', 8, m='5 card rewards of fixed rarities',
  r='AfterObtained[pickup]: RewardsCmd.OfferCustom 5 CardRewards (character pool, uniform odds, NoRarityModification), one per entry of a 5-element CardRarity array',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '5 card rewards x3 cards', None, None, '')],
  g='PlayerRng.Rewards (card reward generation)',
  n='decompile loss: the CardRarity[5] initializer is a lost RuntimeHelpers.InitializeArray blob (Models.Relics/GlassEye.cs:57-58); v0.107.1 has {Common, Common, Uncommon, Uncommon, Rare}',
  q='re-read the GlassEye rarity array from v0.111.0 IL before porting')
R('Glitter', 3, None, 'modifier', 8, m='Glam on reward cards', r='TryModifyCardRewardOptionsLate[card_reward]: every option Glam.CanEnchant -> clone + Enchant Glam(1)', k='enchantments (Glam)')
R('GnarledHammer', 3, None, 'prompt', 9, m='Sharp 3 on up to 3 cards',
  r='AfterObtained[pickup]: FromDeckForEnchantment(Sharp, 3) 0..3 (RMC, not cancelable) -> Enchant Sharp(3)',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment', 'AfterObtained', 'Deck (Sharp.CanEnchant)', '0', '3', 'RMC=true; Cancelable=false')], k='enchantments (Sharp)')
R('GoldenCompass', 4, None, 'cross-cutting', 10, m='current act map becomes the golden path; unknowns are events',
  r='AfterObtained[pickup]: GoldenPathAct=current act; RunManager.GenerateMap() | ModifyGeneratedMap[map]: that act -> new GoldenPathActMap | ModifyUnknownMapPointRoomTypes[map]: that act -> {Event}',
  s='GoldenPathAct (SavedProperty)', k='map regeneration mid-act; GoldenPathActMap',
  n='GoldenPathActMap point types are a decompile loss (Map/GoldenPathActMap.cs:42, design-run-layer 9.2)', q='re-read GoldenPathActMap types from v0.111.0 IL')
R('GoldenPearl', 3, None, 'simple', 7, m='+150 gold', r='AfterObtained[pickup]: GainGold 150')
R('GoldPlatedCables', 3, 3, None, 5, m='front orb passive triggers twice',
  c='ModifyOrbPassiveTriggerCounts: owner front orb -> +1 | AfterModifyingOrbPassiveTriggerCount: flash', k='')
R('Gorget', 2, 2, None, 3, m='4 Plating at combat start', c='AfterRoomEntered (combat room): apply PlatingPower(4)', k='Hook.AfterRoomEntered (combat room); PlatingPower')
R('GremlinHorn', 1, 1, None, 1, m='energy and draw on enemy death',
  c='AfterDeath: target not on owner side -> GainEnergy 1; draw 1')
R('HandDrill', 1, 1, None, 1, m='Vulnerable when breaking block',
  c='AfterBlockBroken: breaker is owner or owner pet, target not a player -> apply VulnerablePower(2) to target')
R('HappyFlower', 1, 1, None, 1, m='+1 energy every 3 turns',
  c='AfterSideTurnStart: owner participates -> TurnsSeen=(TurnsSeen+1)%3; 0 -> GainEnergy 1 | AfterCombatEnd: status only', s='TurnsSeen (SavedProperty, persists across combats)')
R('HeftyTablet', 3, None, 'prompt', 9, m='choose a rare card; Injury curse',
  r='AfterObtained[pickup]: CreateForReward(3, character pool, Uniform, Rare only, NoUpgradeRoll) -> FromChooseACardScreen (canSkip, BlockingPlayerChoiceContext); add chosen (if any) then Injury to deck; unpicked options logged',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'AfterObtained', 'generated (3)', '1', '1', 'canSkip=true')],
  g='PlayerRng.Rewards (3 NextItem)')
R('HelicalDart', 1, 1, None, 2, m='temporary Dexterity per Shiv played',
  c='AfterCardPlayed: owner card tagged Shiv -> apply HelicalDartPower(1)', k='HelicalDartPower (temporary Dexterity)')
R('HistoryCourse', 3, 3, None, 4, m='replay last turn\'s last Attack',
  c='AfterAutoPrePlayPhaseEntered: owner, TurnNumber!=1 -> last CardPlayFinishedEntry of owner last player turn with an Attack that is not a dupe -> CardCmd.AutoPlay(CreateDupe)',
  k='combat history (CardPlayFinishedEntry.HappenedLastPlayerTurn); CardCmd.AutoPlay; CardModel.CreateDupe; Hook.AfterAutoPrePlayPhaseEntered')
R('HornCleat', 1, 1, None, 1, m='14 block on turn 2', c='AfterBlockCleared: owner, TurnNumber==2 -> GainBlock 14')
R('IceCream', 1, 1, None, 1, m='energy carries over', c='ShouldPlayerResetEnergy: owner after turn 1 -> false')
R('InfusedCore', 3, 3, None, 5, m='channel 3 Lightning; Lightning +1',
  c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> OrbCmd.Channel<LightningOrb> x3 | ModifyOrbValue: owner LightningOrb -> +1',
  k='OrbQueue; Hook.ModifyOrbValue', n='upgraded Cracked Core (TouchOfOrobas)')
R('IntimidatingHelmet', 2, 2, None, 3, m='4 block when a card costs >=2 energy',
  c='BeforeCardPlayed: owner card with cardPlay.Resources.EnergyValue>=2 -> GainBlock 4', k='')
R('IronClub', 1, 1, None, 1, m='draw 1 every 4 cards played',
  c='AfterCardPlayed: owner -> CardsPlayed++; in combat and %4==0 -> draw 1', s='CardsPlayed (SavedProperty, persists across combats)')
R('IvoryTile', 2, 2, None, 3, m='+1 energy when a card costs >=3',
  c='AfterCardPlayed: owner card with Resources.EnergyValue>=3 -> GainEnergy 1', k='')
R('JeweledMask', 2, 2, None, 3, m='random Power from draw pile, free, turn 1',
  c='BeforeHandDraw: owner, TurnNumber<=1 -> Powers in draw pile (non-Innate preferred) -> CombatCardSelection.NextItem -> SetToFreeThisTurn -> CardPileCmd.Add Hand',
  g='RunRng.CombatCardSelection', k='CardModel.SetToFreeThisTurn; CardPileCmd.Add (partial)')
R('JewelryBox', 3, None, 'simple', 7, m='add Apotheosis', r='AfterObtained[pickup]: CreateCard<Apotheosis> -> Add Deck')
R('JossPaper', 2, 2, None, 3, m='draw per 5 exhausts',
  c='AfterCardExhausted: owner, not by Ethereal -> CardsExhausted++ then draw floor(CardsExhausted/5) when >=5 (remainder kept); by Ethereal -> EtherealCount++ | AfterSideTurnEnd: owner participates -> add EtherealCount, draw-if-threshold | AfterCombatEnd: EtherealCount=0',
  s='CardsExhausted (SavedProperty SaveIfNotTypeDefault, persists); EtherealCount (combat-scoped)', n='AfterCombatEnd reset of EtherealCount is rule-relevant')
R('JuzuBracelet', 3, None, 'modifier', 8, m='unknown rooms are never fights',
  r='ModifyUnknownMapPointRoomTypes[map]: remove Monster', n='IsAllowed: IsBeforeAct3TreasureChest')
R('Kaleidoscope', 3, None, 'prompt', 8, m='2 card rewards from other characters',
  r='AfterObtained[pickup]: 2x CardReward of 3 cards: StableShuffle(other unlocked character pools, Niche) take 3, one CreateForReward(1, RegularEncounter odds) each; OfferCustom',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '2 card rewards', None, None, '')],
  g='RunRng.Niche (pool shuffle) then PlayerRng.Rewards (rarity, card, upgrade roll), per reward', a='indirect: A7 Scarcity card rarity and upgrade odds',
  n='IsAllowedAtNeow: requires every character card pool unlocked')
R('Kifuda', 3, None, 'prompt', 9, m='Adroit 3 on up to 3 cards',
  r='AfterObtained[pickup]: FromDeckForEnchantment(Adroit, 3) 0..3 (RMC, not cancelable) -> Enchant Adroit(3)',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment', 'AfterObtained', 'Deck (Adroit.CanEnchant)', '0', '3', 'RMC=true; Cancelable=false')], k='enchantments (Adroit)')
R('Kunai', 1, 1, None, 1, m='+1 Dexterity per 3 Attacks in a turn',
  c='AfterCardPlayed: owner Attack in combat -> count++; %3==0 -> apply DexterityPower(1) | BeforeSideTurnStart: reset | AfterCombatEnd: reset (redundant)', s='_attacksPlayedThisTurn (turn-scoped)')
R('Kusarigama', 1, 1, None, 1, m='6 damage to random enemy per 3 Attacks in a turn',
  c='AfterCardPlayed: owner Attack in combat -> count++; %3==0 -> CombatTargets.NextItem(hittable) -> Damage 6 | BeforeCombatStart / AfterSideTurnEnd: reset | AfterCombatEnd: visual',
  s='_attacksPlayedThisTurn (turn-scoped)', g='RunRng.CombatTargets')
R('Lantern', 1, 1, None, 1, m='+1 energy turn 1', c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> GainEnergy 1')
R('LargeCapsule', 3, None, 'simple', 8, m='2 random relics + basic Strike and Defend',
  r='AfterObtained[pickup]: 2x RelicFactory.PullNextRelicFromFront(owner) -> RelicCmd.Obtain; add character basic Strike and basic Defend to deck',
  g='PlayerRng.Rewards (relic rarity roll per relic) + player relic grab bag', k='nested pickup effects (RelicCmd.Obtain inside AfterObtained)')
R('LastingCandy', 3, None, 'modifier', 8, m='every other combat card reward gets an extra Power',
  r='BeforeCombatRewardOffered[rewards]: rewards contain a CardReward -> CombatRewardsSeen++ | TryModifyCardRewardOptions[card_reward]: Encounter source, IsCardReward+IsFromCombat, CombatRewardsSeen odd -> CreateForReward 1 Power not already offered (dupes if none), NoModifyHooks|NoCardPoolModifications -> appended option',
  s='CombatRewardsSeen (SavedProperty counter)', g='PlayerRng.Rewards', n='IsAllowed: not on an Ironclad first-ever run; IsBeforeAct3TreasureChest')
R('LavaLamp', 3, 1, 'modifier', 8, m='upgrade card rewards after a no-damage combat',
  c='AfterDamageReceived: in a combat room, owner, unblocked>0, not Unblockable -> TookDamageThisCombat=true',
  r='AfterRoomEntered[room_entry]: TookDamageThisCombat=false | TryModifyCardRewardOptionsLate[card_reward]: in a combat room without damage taken -> upgrade every upgradable option (clone)',
  s='TookDamageThisCombat (bool, SavedProperty)')
R('LavaRock', 3, None, 'modifier', 8, m='act 1 boss gives 2 extra relics',
  r='TryModifyRewards[rewards]: Boss room in act index 0, not triggered -> add 2x RelicReward(player); HasTriggered=true',
  s='HasTriggered (bool, SavedProperty)', g='PlayerRng.Rewards (relic rarity per reward) + grab bag')
R('LeadPaperweight', 3, None, 'prompt', 9, m='choose 1 of 2 colorless cards',
  r='AfterObtained[pickup]: CreateForReward(2, Colorless, RegularEncounter odds) -> FromChooseACardScreen (canSkip) -> Add Deck',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'AfterObtained', 'generated (2)', '1', '1', 'canSkip=true')],
  g='PlayerRng.Rewards (rarity, card, upgrade roll)', a='indirect: A7 Scarcity card rarity and upgrade odds')
R('LeafyPoultice', 3, None, 'simple', 7, m='lose 12 max HP; transform a Strike and a Defend',
  r='AfterObtained[pickup]: LoseMaxHp 12; first Basic Strike and first Basic Defend -> CardCmd.Transform(random replacement, PlayerRng.Transformations)',
  g='PlayerRng.Transformations (one draw per transformed card)')
R('LeesWaffle', 3, None, 'simple', 7, m='+7 max HP and full heal', r='AfterObtained[pickup]: GainMaxHp 7; Heal MaxHp-CurrentHp')
R('LetterOpener', 1, 1, None, 1, m='5 damage to all enemies per 3 Skills in a turn',
  c='AfterCardPlayed: owner Skill in combat -> count++; %3==0 -> Damage all hittable 5 | BeforeCombatStart / AfterSideTurnStart(turn!=1): reset | AfterCombatEnd: visual', s='_skillsPlayedThisTurn (turn-scoped)')
R('LizardTail', 1, 1, None, 1, m='prevent death once, heal 50%',
  c='ShouldDieLate: owner, not used -> false | AfterPreventingDeath: WasUsed=true; Heal max(1, MaxHp x 50/100)', s='WasUsed (bool, SavedProperty; IsUsedUp)')
R('LoomingFruit', 3, None, 'simple', 7, m='+31 max HP', r='AfterObtained[pickup]: GainMaxHp 31', n='icon depends on profile UniqueId (cosmetic); Ancient rarity in the Shared pool (treasure bag)')
R('LordsParasol', 4, None, 'cross-cutting', 10, m='buy the whole shop for free',
  r='AfterRoomEntered[shop]: MerchantRoom -> PurchaseEverything: character cards, colorless cards, relics, potions (OnTryPurchaseWrapper ignoreCost), then card removal (ignoreCost, not cancelable -> removal prompt)',
  p=[P('DeckRemove', 'MerchantCardRemovalEntry.OnTryPurchaseWrapper', 'AfterRoomEntered', 'Deck', '1', '1', 'cancelable=false; via shop removal entry')],
  k='shop inventory and purchase hooks; card removal purchase flow', n='UI blocking and waits are cosmetic; out-of-stock entries are skipped',
  q='purchase order with full potion slots: does OnTryPurchaseWrapper fail silently or prompt?')
R('LostCoffer', 3, None, 'prompt', 8, m='card reward + potion reward',
  r='AfterObtained[pickup]: OfferCustom [CardReward(character pool, RegularEncounter, 3), PotionReward]',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', 'card reward + potion reward', None, None, '')],
  g='PlayerRng.Rewards (card reward, then potion)', a='indirect: A7 Scarcity')
R('LostWisp', 1, 1, None, 1, m='8 damage to all enemies per Power played', c='AfterCardPlayed: owner Power in combat -> Damage all hittable 8 (Unpowered)')
R('LuckyFysh', 3, None, 'simple', 7, m='+15 gold per card added to deck',
  r='AfterCardChangedPiles[deck_change]: owner card entered Deck -> GainGold 15', n='IsAllowed: IsBeforeAct3TreasureChest; not in shops', k='Hook.AfterCardChangedPiles')
R('LunarPastry', 3, 3, None, 5, m='+1 star at end of turn', c='AfterSideTurnEnd: owner participates -> GainStars 1', k='Stars')
R('Mango', 3, None, 'simple', 7, m='+14 max HP', r='AfterObtained[pickup]: GainMaxHp 14')
R('MassiveScroll', 3, None, 'prompt', 10, m='choose a multiplayer-only card (multiplayer only)',
  r='AfterObtained[pickup]: CreateForReward(3, character+Colorless, MultiplayerOnly filter, RegularEncounter) -> ChooseCard canSkip -> Add Deck',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'AfterObtained', 'generated (3)', '1', '1', 'canSkip=true')],
  n='IsAllowed only with more than 1 player: unreachable in single player')
R('MawBank', 3, None, 'simple', 7, m='+12 gold per room until a purchase',
  r='AfterRoomEntered[room_entry]: room is the map point base room and nothing bought -> GainGold 12 | AfterItemPurchased[shop]: goldSpent>0 -> HasItemBeenBought=true (used up)',
  s='HasItemBeenBought (bool, SavedProperty; IsUsedUp)', k='Hook.AfterItemPurchased; RunState.BaseRoom')
R('MealTicket', 3, None, 'simple', 7, m='heal 15 entering shops', r='AfterRoomEntered[shop]: MerchantRoom, alive -> Heal 15', n='IsAllowed: IsBeforeAct3TreasureChest')
R('MeatCleaver', 3, None, 'prompt', 8, m='Cook rest site option', r='TryModifyRestSiteOptions[rest]: add CookRestSiteOption',
  p=[P('RestSiteOption', 'RestSiteOption list', 'TryModifyRestSiteOptions', 'rest site', '1', '1', 'adds Cook')], k='CookRestSiteOption semantics (Entities.RestSite)')
R('MeatOnTheBone', 2, 2, None, 3, m='heal 12 after winning at <=50% HP',
  c='AfterCombatVictoryEarly: alive and CurrentHp <= (int)(MaxHp x 50/100) -> Heal 12 | BeforeCombatStart / AfterCurrentHpChanged: status only', k='Hook.AfterCombatVictoryEarly')
R('MembershipCard', 3, None, 'modifier', 8, m='shop prices x0.5', r='ModifyMerchantPrice[shop]: owner (LocalContext.IsMe) -> price x 50/100',
  a='indirect: A6 Inflation raises removal price before the multiplier')
R('MercuryHourglass', 1, 1, None, 1, m='3 damage to all enemies each turn', c='AfterPlayerTurnStart: owner -> Damage all hittable 3 (Unpowered)', k='CreatureCmd.Damage(DamageVar) overload')
R('Metronome', 3, 3, None, 5, m='30 damage to all on the 7th orb channeled',
  c='AfterOrbChanneled: owner -> OrbsChanneled++; ==7 -> Damage all hittable 30 | AfterRoomEntered (combat room) / AfterCombatEnd: reset', s='_orbsChanneled (combat-scoped)', k='OrbQueue; Hook.AfterOrbChanneled')
R('MiniatureCannon', 1, 1, None, 1, m='+3 damage on upgraded cards', c='ModifyDamageAdditive: powered attack from an upgraded owner card -> +3')
R('MiniatureTent', 3, None, 'modifier', 8, m='use several rest site options', r='ShouldDisableRemainingRestSiteOptions[rest]: owner -> false')
R('MiniRegent', 3, 3, None, 5, m='+1 Strength on first star spend each turn',
  c='AfterStarsSpent: owner, not used this turn -> apply StrengthPower(1) | BeforeSideTurnStart / AfterCombatEnd: reset', s='_usedThisTurn (turn-scoped)', k='Stars')
R('MoltenEgg', 3, None, 'modifier', 8, m='Attacks in rewards/shop/deck are upgraded',
  r='TryModifyCardRewardOptionsLate[card_reward]: EggRelicHelper Attack | ModifyMerchantCardCreationResults[shop]: same | TryModifyCardBeingAddedToDeck[deck_change]: unupgraded upgradable Attack -> upgraded clone',
  n='IsAllowed: IsBeforeAct3TreasureChest')
R('MrStruggles', 1, 1, None, 1, m='damage all enemies equal to turn number', c='AfterPlayerTurnStart: owner -> Damage all hittable TurnNumber (Unpowered)')
R('MummifiedHand', 2, 2, None, 3, m='Power played -> random hand card free this turn',
  c='AfterCardPlayed: owner Power in combat -> CombatCardSelection.NextItem over cards costing energy/stars (fallbacks: any costing card, then cost>0 list, then hand) -> SetToFreeThisTurn',
  g='RunRng.CombatCardSelection (up to 4 NextItem calls, an empty list consumes nothing)', k='CardModel.SetToFreeThisTurn; CostsEnergyOrStars(includeGlobalModifiers)')
R('MusicBox', 2, 2, None, 3, m='first Attack each turn adds an Ethereal copy',
  c='BeforeCardPlayed: first owner Attack this turn -> remember | AfterCardPlayed: that card -> CreateClone + ApplyKeyword Ethereal -> AddGeneratedCardToCombat Hand | BeforeSideTurnStart / AfterCombatEnd: reset',
  s='_wasUsedThisTurn, _cardBeingPlayed (turn-scoped)', k='CardModel.CreateClone; CardCmd.ApplyKeyword')
R('MysticLighter', 3, 3, None, 4, m='+9 damage on enchanted cards', c='ModifyDamageAdditive: powered attack from an owner card with an enchantment -> +9', k='enchantments on combat cards')
R('NeowsBones', 3, None, 'prompt', 8, m='choose 2 Neow relics; 1 random curse',
  r='AfterObtained[pickup]: relics of Neow options allowed at Neow (except NeowsBones), PlayerRng.Rewards.Shuffle, take 2 -> RewardsSet.WithCustomRewards.WithSkippingDisallowed().Offer(); then 1 Niche.NextItem curse (CurseCardPool, sorted) -> Add Deck',
  p=[P('RewardsScreen', 'RewardsSet.Offer', 'AfterObtained', '2 relic rewards', None, None, 'skipping disallowed')],
  g='PlayerRng.Rewards (Shuffle) then RunRng.Niche (curse)')
R('NeowsSacrifice', 3, None, 'simple', 7, m='Ambergris potion + Guilty curse',
  r='AfterObtained[pickup]: PotionCmd.TryToProcure<Ambergris>; CreateCard<Guilty> -> Add Deck', n='new in v0.111.0')
R('NeowsTalisman', 3, None, 'simple', 7, m='upgrade the last Strike and Defend', r='AfterObtained[pickup]: last Basic Strike and last Basic Defend in deck -> CardCmd.Upgrade')
R('NeowsTorment', 3, None, 'simple', 7, m="add Neow's Fury", r='AfterObtained[pickup]: CreateCard<NeowsFury> -> Add Deck')
R('NewLeaf', 3, None, 'prompt', 9, m='transform 1 card (choice)',
  r='AfterObtained[pickup]: FromDeckForTransformation 1/1 -> CardCmd.TransformToRandom(card, RunRng.Niche)',
  p=[P('DeckTransform', 'CardSelectCmd.FromDeckForTransformation', 'AfterObtained', 'Deck', '1', '1', 'TransformSelectionPrompt')], g='RunRng.Niche')
R('NinjaScroll', 2, 2, None, 3, m='3 Shivs turn 1', c='BeforeHandDraw: owner, TurnNumber<=1 -> Shiv.CreateInHand(3)', k='Shiv card; Shiv.CreateInHand (AddGeneratedCardsToCombat)')
R('Nunchaku', 1, 1, None, 1, m='+1 energy every 10 Attacks', c='AfterCardPlayed: owner Attack -> AttacksPlayed++; in combat and %10==0 -> GainEnergy 1', s='AttacksPlayed (SavedProperty, persists across combats)')
R('NutritiousOyster', 3, None, 'simple', 7, m='+11 max HP', r='AfterObtained[pickup]: GainMaxHp 11')
R('NutritiousSoup', 3, None, 'simple', 7, m="Tezcatara's Ember on basic Strikes",
  r="AfterObtained[pickup]: every Basic Strike that TezcatarasEmber.CanEnchant -> Enchant TezcatarasEmber(1)", k='enchantments (TezcatarasEmber)')
R('OddlySmoothStone', 2, 2, None, 3, m='1 Dexterity at combat start', c='AfterRoomEntered (combat room): apply DexterityPower(1)', k='Hook.AfterRoomEntered (combat room)')
R('OldCoin', 3, None, 'simple', 7, m='+300 gold', r='AfterObtained[pickup]: GainGold 300', n='IsAllowed: IsBeforeAct3TreasureChest; not in shops')
R('OrangeDough', 3, 3, None, 4, m='2 colorless cards turn 1',
  c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> GetDistinctForCombat(2, unlocked Colorless, CombatCardGeneration) -> AddGeneratedCardsToCombat Hand', g='RunRng.CombatCardGeneration')
R('Orichalcum', 1, 1, None, 1, m='6 block at end of turn when unblocked',
  c='BeforeSideTurnEndVeryEarly: Block==0 -> ShouldTrigger | BeforeSideTurnEnd: -> GainBlock 6 | BeforeSideTurnStart: reset', s='_shouldTrigger (turn-scoped)')
R('OrnamentalFan', 1, 1, None, 1, m='4 block per 3 Attacks in a turn',
  c='AfterCardPlayed: owner Attack in combat -> count++; %3==0 -> GainBlock 4 | BeforeSideTurnStart: reset | AfterCombatEnd: visual', s='_attacksPlayedThisTurn (turn-scoped)')
R('Orrery', 3, None, 'prompt', 8, m='5 card rewards', r='AfterObtained[pickup]: OfferCustom 5x CardReward(character pool, RegularEncounter, 3)',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '5 card rewards', None, None, '')], g='PlayerRng.Rewards', a='indirect: A7 Scarcity')
R('PaelsBlood', 1, 1, None, 1, m='+1 hand draw', c='ModifyHandDraw: +1')
R('PaelsClaw', 3, None, 'simple', 7, m='Goopy on every enchantable card',
  r='AfterObtained[pickup]: every deck card Goopy.CanEnchant -> Enchant Goopy(1)', k='enchantments (Goopy)', n='CardsVar 3 and _enchantmentAmount=2 are display-only; the enchant amount is 1 (Models.Relics/PaelsClaw.cs:46)')
R('PaelsEye', 3, 3, None, 4, m='extra turn if no card was played (once per combat)',
  c='BeforeSideTurnEndEarly: owner participates, unused, no non-autoplay card played this turn, owner was in last player turn -> exhaust every hand card | ShouldTakeExtraTurn: same condition -> true | AfterTakingExtraTurn: UsedThisCombat=true | AfterSideTurnStart: tracks participation | BeforeCardPlayed: status only | AfterCombatEnd: reset',
  r='AfterObtained[pickup]: WasOwnerPartOfLastPlayerTurn = IsPartOfPlayerTurn (combat only)',
  s='_usedThisCombat (combat-scoped), _wasOwnerPartOfLastPlayerTurn', k='combat history (CardPlayFinishedEntry.HappenedThisTurn); extra turns (Hook.ShouldTakeExtraTurn); reads WhisperingEarring presence on turn 1')
R('PaelsFlesh', 1, 1, None, 1, m='+1 max energy from turn 3', c='ModifyMaxEnergy: TurnNumber>=3 -> +1 | BeforeCombatStart / BeforeSideTurnStart / AfterSideTurnStart / AfterCombatEnd: display and status only (Status gates only the flash)')
R('PaelsGrowth', 3, None, 'prompt', 9, m='Clone 4 on a card; Clone rest option',
  r='AfterObtained[pickup]: FromDeckForEnchantment(Clone, 1) 1/1 -> Enchant Clone(4) | TryModifyRestSiteOptions[rest]: add CloneRestSiteOption',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment', 'AfterObtained', 'Deck (Clone.CanEnchant)', '1', '1', ''), P('RestSiteOption', 'RestSiteOption list', 'TryModifyRestSiteOptions', 'rest site', '1', '1', 'adds Clone')],
  k='enchantments (Clone); CloneRestSiteOption')
R('PaelsHorn', 3, None, 'simple', 7, m='add 2 Relax', r='AfterObtained[pickup]: 2x CreateCard<Relax> -> Add Deck')
R('PaelsLegion', 3, 3, 'simple', 5, m='pet; doubled card block with a 2-turn cooldown',
  c='BeforeCombatStart: PlayerCmd.AddPet<Monsters.PaelsLegion> | ModifyBlockMultiplicative: card/monster-move block from owner card, Cooldown<=0 -> x2 | AfterModifyingBlockAmount: record the card play | AfterCardPlayed: affected play -> Cooldown=2 | AfterSideTurnStart: Cooldown-- | AfterCombatEnd: reset',
  r='AfterObtained[pickup]: Skin via Rng(player,id) (cosmetic); in combat -> summon pet',
  s='_cooldown, _triggeredBlockLastTurn, _affectedCardPlay (combat-scoped); Skin (SavedProperty, cosmetic)', k='')
R('PaelsTears', 2, 2, None, 3, m='+2 energy after ending a turn with energy left',
  c='BeforeSideTurnEnd: owner participates -> HadLeftoverEnergy = Energy>0 | AfterSideTurnStart: flag -> GainEnergy 2 | AfterCombatEnd: reset', s='_hadLeftoverEnergy (combat-scoped)', n='AfterCombatEnd reset is rule-relevant')
R('PaelsTooth', 3, None, 'prompt', 9, m='remove 5 upgradable cards; one returns upgraded after each combat',
  r='AfterObtained[pickup]: FromDeckForRemoval 5/5 (upgradable only) -> store clones sorted by Id (ordinal), RemoveFromDeck | AfterCombatEnd[combat_end]: alive and cards stored -> PlayerRng.Rewards.NextItem -> add back to deck upgraded',
  p=[P('DeckRemove', 'CardSelectCmd.FromDeckForRemoval', 'AfterObtained', 'Deck (IsUpgradable)', '5', '5', 'RemoveSelectionPrompt')],
  s='SerializableCards (list, SavedProperty)', g='PlayerRng.Rewards (one NextItem per combat end)')
R('PaelsWing', 3, None, 'prompt', 8, m='sacrifice card rewards; relic every 2 sacrifices',
  r='TryModifyCardRewardAlternatives[card_reward]: add SACRIFICE alternative (ends selection, completes reward) | OnSacrifice[card_reward]: RewardsSacrificed++; %2==0 -> RelicCmd.Obtain(PullNextRelicFromFront)',
  p=[P('CardRewardAlternative', 'CardRewardAlternative SACRIFICE', 'TryModifyCardRewardAlternatives', 'card reward screen', '1', '1', 'extra option on every card reward')],
  s='RewardsSacrificed (SavedProperty counter)', g='PlayerRng.Rewards (relic rarity) + grab bag')
R('PandorasBox', 3, None, 'simple', 7, m='transform every Strike and Defend',
  r='AfterObtained[pickup]: every removable basic Strike/Defend -> CreateRandomCardForTransform(isInCombat:false, Niche) -> CardCmd.Transform batch',
  g='RunRng.Niche (one NextItem per card, deck order)', n='also used by modifiers Draft/Insanity/SealedDeck')
R('Pantograph', 2, 2, None, 3, m='heal 25 at boss fights', c='BeforeCombatStart: alive and CurrentRoom.RoomType==Boss -> Heal 25',
  r='AfterRoomEntered[room_entry]: status only (next to boss)', k='combat context: RunState.CurrentRoom.RoomType')
R('PaperKrane', 2, 2, None, 3, m='weakened enemies deal 15% less to owner',
  c='consulted by WeakPower: ModifyWeakMultiplier(target=owner, powered attack) -> amount - 0.15', k='WeakPower must query the owner PaperKrane (Models.Powers/WeakPower.cs), no hook', n='no AbstractModel hook: direct cross-model query')
R('PaperPhrog', 2, 2, None, 3, m='Vulnerable on enemies is +25%',
  c='consulted by VulnerablePower: ModifyVulnerableMultiplier(target!=owner, powered attack) -> amount + 0.25', k='VulnerablePower must query PaperPhrog (Models.Powers/VulnerablePower.cs), no hook')
R('ParryingShield', 1, 1, None, 1, m='6 damage to a random enemy at end of turn with >=10 block',
  c='AfterSideTurnEnd: owner participates, Block>=10 -> CombatTargets.NextItem(hittable) -> Damage 6', g='RunRng.CombatTargets')
R('Pear', 3, None, 'simple', 7, m='+10 max HP', r='AfterObtained[pickup]: GainMaxHp 10')
R('Pendulum', 1, 1, None, 1, m='+1 hand draw every 3rd turn',
  c='BeforeHandDraw: owner -> TurnsSeen=(TurnsSeen+1)%3 | ModifyHandDraw: TurnsSeen==0 -> +1 | AfterCombatEnd: status only', s='TurnsSeen (SavedProperty, persists across combats)')
R('PenNib', 1, 1, None, 1, m='every 10th Attack deals double damage',
  c='BeforeCardPlayed: owner Attack -> AttacksPlayed=(n+1)%10; 0 -> AttackToDouble=card | ModifyDamageMultiplicative: powered attack by owner or Osty: AttackToDouble==card -> x2; no pending double, card not in Play pile and AttacksPlayed==9 -> x2 | AfterCardPlayed: clear',
  s='AttacksPlayed (SavedProperty, mod 10, persists); _attackToDouble (play-scoped)', n='the AttacksPlayed==9 branch doubles damage previews and non-Play-pile attack damage')
R('Permafrost', 2, 2, None, 3, m='7 block on first Power each combat',
  c='AfterCardPlayed: owner Power in combat, not activated -> GainBlock 7 | AfterRoomEntered (combat room): reset', s='_activatedThisCombat (combat-scoped)', k='Hook.AfterRoomEntered (combat room)')
R('PetrifiedToad', 3, 3, None, 4, m='Potion-Shaped Rock each combat', c='BeforeCombatStartLate: PotionCmd.TryToProcure<PotionShapedRock>', k='potion slots; PotionCmd.TryToProcure')
R('PhialHolster', 3, None, 'simple', 7, m='+1 potion slot; 2 random potions',
  r='AfterObtained[pickup]: GainMaxPotionCount 1; CreateRandomPotionsOutOfCombat(2, CombatPotionGeneration) -> TryToProcure each',
  g='RunRng.CombatPotionGeneration', a='indirect: A4 TightBelt')
R('PhilosophersStone', 2, 2, None, 3, m='+1 max energy; enemies +1 Strength',
  c='ModifyMaxEnergy: +1 | AfterRoomEntered (combat room): alive opponents StrengthPower(1) | AfterCreatureAddedToCombat: new enemy -> StrengthPower(1)', k='Hook.AfterRoomEntered (combat room)')
R('PhylacteryUnbound', 3, 3, None, 5, m='summon 5 at start, 2 each turn',
  c='BeforeCombatStart: OstyCmd.Summon 5 | AfterSideTurnStart: owner participates -> OstyCmd.Summon 2', k='Osty pet', n='upgraded Bound Phylactery (TouchOfOrobas)')
