from annot_common import R, P

R('Planisphere', 3, None, 'simple', 7, m='heal 5 entering unknown rooms',
  r='AfterRoomEntered[room_entry]: alive, current map point Unknown and CurrentRoomCount<=1 -> Heal 5', n='IsAllowed: IsBeforeAct3TreasureChest')
R('Pocketwatch', 1, 1, None, 1, m='+3 hand draw after a turn with <=3 cards played',
  c='AfterCardPlayed: owner in combat -> played++ | BeforeSideTurnStart: last=this; this=0 | ModifyHandDraw: not turn 1 and last<=3 -> +3 | AfterModifyingHandDraw: flash | AfterSideTurnStart: display only | AfterCombatEnd: reset (redundant: turn 1 skips the bonus)',
  s='_cardsPlayedThisTurn, _cardsPlayedLastTurn (turn-scoped)')
R('PollinousCore', 1, 1, None, 1, m='+2 hand draw every 4 turns',
  c='BeforeHandDraw: owner -> TurnsSeen++ | ModifyHandDraw: TurnsSeen>=4 -> +2 | AfterModifyingHandDraw: TurnsSeen=0 | AfterCombatEnd: status only',
  s='TurnsSeen (SavedProperty, persists across combats)', n='counter reset happens in AfterModifyingHandDraw, which only runs when this relic modified the draw')
R('Pomander', 3, None, 'prompt', 9, m='upgrade 1 card (choice)', r='AfterObtained[pickup]: FromDeckForUpgrade 1/1 -> CardCmd.Upgrade',
  p=[P('DeckUpgrade', 'CardSelectCmd.FromDeckForUpgrade', 'AfterObtained', 'Deck (IsUpgradable)', '1', '1', 'UpgradeSelectionPrompt')])
R('PotionBelt', 3, None, 'simple', 7, m='+2 potion slots', r='AfterObtained[pickup]: GainMaxPotionCount 2', a='indirect: A4 TightBelt')
R('PowerCell', 2, 2, None, 3, m='2 zero-cost cards from draw pile into hand turn 1',
  c='BeforeSideTurnStart: owner participates, TurnNumber<=1 -> draw-pile cards with non-X local cost 0 -> StableShuffle(CombatCardSelection) take 2 -> CardPileCmd.Add Hand',
  g='RunRng.CombatCardSelection (StableShuffle)', k='CardPileCmd.Add (partial); EnergyCost.GetWithModifiers(Local)')
R('PrayerWheel', 3, None, 'modifier', 8, m='extra card reward from normal fights',
  r='TryModifyRewards[rewards]: Monster room -> add CardReward(ForRoom Monster, 3)', g='PlayerRng.Rewards', a='indirect: A7 Scarcity')
R('PrecariousShears', 3, None, 'prompt', 9, m='remove 2 cards (choice), take 16 damage',
  r='AfterObtained[pickup]: FromDeckForRemoval 2/2 -> RemoveFromDeck each; CreatureCmd.Damage owner 16 (Unpowered, no dealer)',
  p=[P('DeckRemove', 'CardSelectCmd.FromDeckForRemoval', 'AfterObtained', 'Deck', '2', '2', 'RemoveSelectionPrompt')],
  q='out-of-combat CreatureCmd.Damage: confirm block/HP-loss hooks and death at Neow')
R('PreciseScissors', 3, None, 'prompt', 9, m='remove 1 card (choice)', r='AfterObtained[pickup]: FromDeckForRemoval 1/1 -> RemoveFromDeck',
  p=[P('DeckRemove', 'CardSelectCmd.FromDeckForRemoval', 'AfterObtained', 'Deck', '1', '1', 'RemoveSelectionPrompt')])
R('PreservedFog', 3, None, 'prompt', 9, m='remove 3 cards (choice); Folly curse',
  r='AfterObtained[pickup]: FromDeckForRemoval 3/3 -> RemoveFromDeck each; AddCurseToDeck<Folly>',
  p=[P('DeckRemove', 'CardSelectCmd.FromDeckForRemoval', 'AfterObtained', 'Deck', '3', '3', 'RemoveSelectionPrompt')],
  n='Folly has a decompile-lost static array (Models.Cards/Folly.cs:19)')
R('PrismaticGem', 3, 1, 'modifier', 8, m='+1 max energy; card rewards from every character',
  c='ModifyMaxEnergy: +1', r='ModifyCardRewardCreationOptions[card_reward]: IsCardReward, pool modifications allowed, not all-colorless -> pools = unlocked character pools Union pools')
R('PumpkinCandle', 3, 1, 'prompt', 8, m='+1 max energy while lit; Kindle rest option',
  c='ModifyMaxEnergy: KindleCount>0 -> +1', r='AfterObtained[pickup]: Rekindle (+5) | AfterCombatEnd[combat_end]: KindleCount=max(0, n-1) | TryModifyRestSiteOptions[rest]: add KindleRestSiteOption (Rekindle +5)',
  p=[P('RestSiteOption', 'RestSiteOption list', 'TryModifyRestSiteOptions', 'rest site', '1', '1', 'adds Kindle')], s='KindleCount (SavedProperty charges)')
R('PunchDagger', 3, None, 'prompt', 9, m='Momentum 5 on a card', r='AfterObtained[pickup]: FromDeckForEnchantment(Momentum, 5) 1/1 -> Enchant Momentum(5)',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment', 'AfterObtained', 'Deck (Momentum.CanEnchant)', '1', '1', '')], k='enchantments (Momentum)')
R('RadiantPearl', 2, 2, None, 3, m='Luminesce in hand turn 1',
  c='BeforeHandDraw: owner, TurnNumber==1 -> CreateCard<Luminesce> x1 -> AddGeneratedCardsToCombat Hand', k='Luminesce card; CardPileCmd.AddGeneratedCardsToCombat')
R('RainbowRing', 1, 1, None, 1, m='+1 Str/Dex after an Attack, Skill and Power in a turn',
  c='AfterCardPlayed: owner in combat, not yet triggered this turn -> count types; all three >0 -> StrengthPower(1), DexterityPower(1); once per turn | BeforeSideTurnStart: reset | AfterCombatEnd: reset (redundant)',
  s='per-type counters + activation count (turn-scoped)')
R('RazorTooth', 2, 2, None, 3, m='upgrade Attacks/Skills as they are played', c='AfterCardPlayed: owner Attack or Skill, upgradable -> CardCmd.Upgrade', n='listed in Event and Shared pools; Pool resolves to EVENT')
R('RedMask', 1, 1, None, 1, m='Weak to all enemies turn 1', c='BeforeSideTurnStart: owner participates, TurnNumber<=1 -> WeakPower(1) to all hittable enemies')
R('RedSkull', 2, 2, None, 3, m='+3 Strength while at or below 50% HP',
  c='AfterRoomEntered (combat room) / AfterCurrentHpChanged: in combat -> CurrentHp > MaxHp x 50/100 removes (StrengthPower -3) if applied, else applies StrengthPower(3) once | AfterCombatEnd: StrengthApplied=false',
  r='AfterObtained[pickup]: in combat -> evaluate (unreachable in v0.111.0)', s='_strengthApplied (combat-scoped)', k='')
R('Regalite', 1, 1, None, 1, m='4 block on the first generated card each turn',
  c='AfterCardGeneratedForCombat: creator owner, not used this turn -> GainBlock 4 (fast) | BeforeSideTurnStart / AfterCombatEnd: reset', s='_usedThisTurn (turn-scoped)')
R('RegalPillow', 3, None, 'modifier', 8, m='rest heal +15', r='ModifyRestSiteHealAmount[rest]: owner or owner pet -> +15 | AfterRestSiteHeal / AfterRoomEntered[rest]: status only | ModifyExtraRestSiteHealText[rest]: UI',
  k='Hook.ModifyRestSiteHealAmount')
R('ReptileTrinket', 3, 3, None, 4, m='temporary Strength when using a potion',
  c='AfterPotionUsed: owner potion in combat -> ReptileTrinketPower(3)', k='potion use action; Hook.AfterPotionUsed; ReptileTrinketPower (temporary Strength)')
R('RingingTriangle', 1, 1, None, 1, m='retain hand on turn 1', c='ShouldFlush: owner -> TurnNumber>1')
R('RingOfTheDrake', 1, 1, None, 1, m='+2 hand draw for 3 turns', c='ModifyHandDraw: TurnNumber<=3 -> +2', n='upgraded Ring of the Snake (TouchOfOrobas)')
R('RingOfTheSnake', 1, 1, None, 1, m='+2 hand draw turn 1', c='ModifyHandDraw: TurnNumber<=1 -> +2', n='Silent starter')
R('RippleBasin', 3, 3, None, 4, m='4 block at end of a turn without Attacks',
  c='BeforeSideTurnEnd: owner participates and no owner Attack CardPlayFinishedEntry this turn -> GainBlock 4 | AfterCardPlayed / BeforeSideTurnStart / AfterCombatEnd: status only', k='combat history (CardPlayFinishedEntry.HappenedThisTurn)')
R('RoyalPoison', 1, 1, None, 1, m='take 4 damage turn 1', c='AfterPlayerTurnStart: owner, TurnNumber<=1 -> CreatureCmd.Damage owner 4 (Unblockable|Unpowered)', k='CreatureCmd.Damage(DamageVar) overload')
R('RoyalStamp', 3, None, 'prompt', 9, m='Royally Approved on a card',
  r='AfterObtained[pickup]: enchantable deck cards UnstableShuffle(Niche) -> FromDeckForEnchantment(cards, RoyallyApproved, 1) 1/1 -> Enchant RoyallyApproved(1)',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment(cards)', 'AfterObtained', 'shuffled enchantable deck cards', '1', '1', 'candidate order is the Niche shuffle')],
  g='RunRng.Niche (UnstableShuffle before the prompt)', k='enchantments (RoyallyApproved)')
R('RuinedHelmet', 2, 2, None, 3, m='first Strength gain each combat doubled',
  c='TryModifyPowerAmountReceived: StrengthPower on owner, amount>0, unused -> x2 | AfterModifyingPowerAmountReceived: used=true | AfterCombatEnd: reset',
  s='_usedThisCombat (combat-scoped)', n='AfterCombatEnd reset is rule-relevant')
R('RunicCapacitor', 3, 3, None, 5, m='+3 orb slots turn 1', c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> OrbCmd.AddSlots 3', k='OrbQueue; OrbCmd.AddSlots')
R('RunicPyramid', 1, 1, None, 1, m='never discard hand at end of turn', c='ShouldFlush: owner -> false')
R('Sai', 1, 1, None, 1, m='7 block each turn', c='AfterSideTurnStart: owner participates -> GainBlock 7')
R('SandCastle', 3, None, 'simple', 7, m='upgrade 6 random cards', r='AfterObtained[pickup]: StableShuffle(upgradable deck cards, Niche) take 6 -> Upgrade', g='RunRng.Niche')
R('ScreamingFlagon', 1, 1, None, 1, m='20 damage to all enemies when ending turn with empty hand',
  c='BeforeSideTurnEnd: owner participates, hand empty -> Damage all hittable 20 (Unpowered)')
R('ScrollBoxes', 3, None, 'prompt', 9, m='choose 1 of 2 card bundles',
  r='AfterObtained[pickup]: GenerateRandomBundles (PlayerRng.Rewards): 2 bundles of 2 Commons + 1 Uncommon without repeats (Defect: 1% chance per bundle of 3 Claws via NextInt(100)) -> FromChooseABundleScreen -> Add chosen cards to deck',
  p=[P('ChooseBundle', 'CardSelectCmd.FromChooseABundleScreen', 'AfterObtained', '2 bundles of 3', '1', '1', '')],
  g='PlayerRng.Rewards (Defect only: NextInt(100) per bundle first, then NextItem x3 per bundle)',
  n='IsAllowedAtNeow: needs >=4 Commons and >=2 Uncommons unlocked; options pass through Hook.ModifyCardRewardCreationOptions')
R('SealOfGold', 2, 2, None, 3, m='pay 3 gold for +1 energy each turn',
  c='AfterSideTurnStart: owner participates, Gold>=3 -> GainEnergy 1; LoseGold 3', k='Player.Gold in combat; PlayerCmd.LoseGold')
R('SeaGlass', 3, None, 'prompt', 9, m='pick any of 15 cards of a chosen character',
  r='AfterObtained[pickup]: 5 Common + 5 Uncommon + 5 Rare CreateForReward from CharacterId pool (uniform, NoRarityModification, NoCardPoolModifications; upgrade rolls apply) -> FromSimpleGridForRewards 0..15 -> Add chosen to deck',
  p=[P('SimpleGridRewards', 'CardSelectCmd.FromSimpleGridForRewards', 'AfterObtained', 'generated (15)', '0', '15', 'BlockingPlayerChoiceContext')],
  s='CharacterId (ModelId, SavedProperty; set by the offering ancient, defaults to Ironclad)', g='PlayerRng.Rewards (card + upgrade roll per card)', a='indirect: A7 Scarcity upgrade odds')
R('SelfFormingClay', 1, 1, None, 2, m='3 block next turn when damaged',
  c='AfterDamageReceived: in combat, owner, UnblockedDamage>0 -> SelfFormingClayPower(3)', k='SelfFormingClayPower')
R('SereTalon', 3, None, 'simple', 7, m='lose 9 max HP; 3 Wish cards', r='AfterObtained[pickup]: LoseMaxHp 9; 3x Wish -> Add Deck')
R('Shovel', 3, None, 'prompt', 8, m='Dig rest site option', r='TryModifyRestSiteOptions[rest]: add DigRestSiteOption (obtain PullNextRelicFromFront)',
  p=[P('RestSiteOption', 'RestSiteOption list', 'TryModifyRestSiteOptions', 'rest site', '1', '1', 'adds Dig')], g='PlayerRng.Rewards when Dig is chosen', n='IsAllowed: IsBeforeAct3TreasureChest')
R('Shuriken', 1, 1, None, 1, m='+1 Strength per 3 Attacks in a turn',
  c='AfterCardPlayed: owner Attack in combat -> count++; %3==0 -> StrengthPower(1) | BeforeSideTurnStart: reset | AfterCombatEnd: reset (redundant)', s='_attacksPlayedThisTurn (turn-scoped)')
R('SignetRing', 3, None, 'simple', 7, m='+888 gold', r='AfterObtained[pickup]: GainGold 888')
R('SilkenTress', 3, None, 'modifier', 8, m='lose all gold; next card reward gets Glam',
  r='AfterObtained[pickup]: LoseGold(all) | TryModifyCardRewardOptionsLate[card_reward]: IsCardReward, unused -> Glam(1) on every enchantable option | AfterModifyingCardRewardOptions[card_reward]: IsUsed=true',
  s='IsUsed (bool, SavedProperty; IsUsedUp)', k='enchantments (Glam)')
R('SilverCrucible', 3, None, 'modifier', 8, m='first 3 card rewards upgraded; first treasure room empty',
  r='TryModifyCardRewardOptionsLate[card_reward]: TimesUsed<3, IsCardReward -> upgrade every upgradable option | AfterModifyingCardRewardOptions[card_reward]: TimesUsed++ | AfterRoomEntered[treasure]: TreasureRoom -> TreasureRoomsEntered++ | ShouldGenerateTreasure[treasure]: owner -> TreasureRoomsEntered>1',
  s='TimesUsed, TreasureRoomsEntered (SavedProperty)', n='IsAllowed: single player only')
R('SlingOfCourage', 2, 2, None, 3, m='+2 Strength in elite fights', c='AfterRoomEntered (combat room): RoomType Elite -> StrengthPower(2)', k='Hook.AfterRoomEntered (combat room)', a='indirect: A1 SwarmingElites')
R('SmallCapsule', 3, None, 'prompt', 8, m='a relic reward', r='AfterObtained[pickup]: OfferCustom [RelicReward(owner)]',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '1 relic reward', None, None, '')], g='PlayerRng.Rewards (rarity roll) + grab bag')
R('SneckoEye', 1, 1, None, 2, m='+2 hand draw; Confused',
  c='ModifyHandDraw: +2 | BeforeCombatStart: ConfusedPower(1) on owner', r='AfterObtained[pickup]: in combat -> apply (unreachable in v0.111.0)',
  g='RunRng.CombatEnergyCosts (inside ConfusedPower)', k='ConfusedPower')
R('SneckoSkull', 1, 1, None, 2, m='+1 Poison on every Poison applied', c='ModifyPowerAmountGivenAdditive: PoisonPower given by owner -> +1 | AfterModifyingPowerAmountGiven: flash', k='PoisonPower')
R('Sozu', 3, 3, 'modifier', 8, m='+1 max energy; cannot obtain potions', c='ModifyMaxEnergy: +1 | ShouldProcurePotion (both scope): owner -> false',
  r='ShouldProcurePotion[potion]: blocks every potion procurement for the owner', k='Hook.ShouldProcurePotion; potion procurement')
R('SparklingRouge', 1, 1, None, 1, m='+1 Str and Dex on turn 3', c='AfterBlockCleared: owner, TurnNumber==3 -> StrengthPower(1), DexterityPower(1)', n='Event and Shared pools; Pool resolves to EVENT')
R('SpikedGauntlets', 1, 1, None, 1, m='+1 max energy; Powers cost 1 more', c='ModifyMaxEnergy: +1 | TryModifyEnergyCostInCombat: owner Power -> +1')
R('StoneCalendar', 1, 1, None, 1, m='52 damage to all enemies at end of turn 7', c='BeforeSideTurnEnd: owner participates, TurnNumber==7 -> Damage all hittable 52 | AfterSideTurnStart / AfterRoomEntered / AfterCombatEnd: display and status only')
R('StoneCracker', 2, 2, None, 3, m='upgrade 2 random draw-pile cards at combat start',
  c='AfterRoomEntered (combat room): upgradable draw-pile cards StableShuffle(CombatCardSelection) take 2 -> CardCmd.Upgrade', g='RunRng.CombatCardSelection',
  k='Hook.AfterRoomEntered (combat room, after SetUpCombat populated and shuffled the draw pile)')
R('StoneHumidifier', 3, None, 'modifier', 8, m='+5 max HP when resting', r='AfterRestSiteHeal[rest]: owner -> GainMaxHp 5 | ModifyExtraRestSiteHealText[rest]: UI')
R('Storybook', 3, None, 'simple', 7, m='add Brightest Flame', r='AfterObtained[pickup]: CreateCard<BrightestFlame> -> Add Deck')
R('Strawberry', 3, None, 'simple', 7, m='+7 max HP', r='AfterObtained[pickup]: GainMaxHp 7')
R('StrikeDummy', 1, 1, None, 1, m='+3 damage on Strike cards', c='ModifyDamageAdditive: powered attack from an owner Strike-tagged card -> +3')
R('SturdyClamp', 1, 1, None, 1, m='keep up to 10 block', c='ShouldClearBlock: owner -> false | AfterPreventingBlockClear: this relic prevented it -> LoseBlock(Block-10) if Block>10')
R('SwordOfJade', 2, 2, None, 3, m='+3 Strength at combat start', c='AfterRoomEntered (combat room): StrengthPower(3)', k='Hook.AfterRoomEntered (combat room)', n='obtained by SwordOfStone replacement')
R('SwordOfStone', 3, None, 'simple', 8, m='becomes Sword of Jade after 5 elites',
  r='AfterCombatVictory[combat_end]: elite room -> ElitesDefeated++; >=5 -> RelicCmd.Replace(this, SwordOfJade)', s='ElitesDefeated (SavedProperty counter)', k='RelicCmd.Replace at combat victory', a='indirect: A1 SwarmingElites')
R('SymbioticVirus', 3, 3, None, 5, m='channel 1 Dark turn 1', c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> OrbCmd.Channel<DarkOrb>', k='OrbQueue')
R('TanxsWhistle', 3, None, 'simple', 7, m='add Whistle', r='AfterObtained[pickup]: CreateCard<Whistle> -> Add Deck')
R('TeaOfDiscourtesy', 2, 2, None, 3, m='2 Dazed in the next combat',
  c='BeforeCombatStart: CombatsLeft>0 -> AddToCombatAndPreview<Dazed>(2, Draw, Random); CombatsLeft--', s='CombatsLeft=1 (SavedProperty charges)', g='RunRng.Shuffle (random insert)', k='CardPileCmd.AddToCombatAndPreview')
R('TheAbacus', 1, 1, None, 1, m='6 block on shuffle', c='AfterShuffle: owner -> GainBlock 6')
R('TheBoot', 1, 1, None, 1, m='minimum 5 HP loss on owner attacks',
  c='ModifyHpLostAfterOstyLate: dealer owner or Osty, target not owner, powered attack, 1<=amount<5 -> 5 | AfterModifyingHpLostAfterOsty: flash', n='DamageThreshold 4 is text only')
R('TheCourier', 3, None, 'modifier', 8, m='shop prices -20%; restocks', r='ModifyMerchantPrice[shop]: x(1-0.2) | ShouldRefillMerchantEntry[shop]: owner -> true', n='not in shops')
R('ThrowingAxe', 2, 2, None, 3, m='first card each combat played twice',
  c='ModifyCardPlayCount: unused, owner card -> +1 | AfterModifyingCardPlayCount: used | AfterRoomEntered (combat room) / AfterCombatEnd: reset (redundant pair)', s='_usedThisCombat (combat-scoped)',
  k='Hook.AfterRoomEntered (combat room) or Hook.AfterCombatEnd for the per-combat reset')
R('Tingsha', 1, 1, None, 1, m='3 damage to a random enemy per discard on own turn',
  c='AfterCardDiscarded: owner card during own side turn -> CombatTargets.NextItem(hittable) -> Damage 3', g='RunRng.CombatTargets')
R('TinyMailbox', 3, None, 'modifier', 8, m='2 potion rewards when resting', r='TryModifyRestSiteHealRewards[rest]: add 2x PotionReward(player) | ModifyExtraRestSiteHealText[rest]: UI', g='PlayerRng.Rewards')
R('ToastyMittens', 3, 3, None, 6, m='each turn exhaust a hand card (choice), +1 Strength',
  c='AfterPlayerTurnStart: owner -> FromHand 1/1 (ExhaustSelectionPrompt) -> CardCmd.Exhaust; then StrengthPower(1)',
  p=[P('HandSelect', 'CardSelectCmd.FromHand', 'AfterPlayerTurnStart', 'Hand', '1', '1', 'every turn; turn-setup deferral (SetupPlayerTurn context)')])
R('Toolbox', 3, 3, None, 6, m='choose 1 of 3 colorless cards turn 1',
  c='BeforeHandDraw: owner, TurnNumber==1 -> GetDistinctForCombat(3, unlocked Colorless, CombatCardGeneration) -> FromChooseACardScreen (no skip) -> AddGeneratedCardToCombat Hand',
  p=[P('ChooseCard', 'CardSelectCmd.FromChooseACardScreen', 'BeforeHandDraw', 'generated (3)', '1', '1', 'canSkip=false; turn-setup deferral precedes the opening draw')], g='RunRng.CombatCardGeneration')
R('TouchOfOrobas', 3, None, 'simple', 7, m='upgrade the starter relic',
  r='SetupForPlayer[offer]: StarterRelic + UpgradedRelic (BurningBlood->BlackBlood, RingOfTheSnake->RingOfTheDrake, DivineRight->DivineDestiny, BoundPhylactery->PhylacteryUnbound, CrackedCore->InfusedCore, else Circlet) | AfterObtained[pickup]: RelicCmd.Replace(starter, upgraded)',
  s='StarterRelic, UpgradedRelic (ModelId, SavedProperty, set once)', k='RelicCmd.Replace')
R('ToughBandages', 1, 1, None, 1, m='3 block per discard on own turn', c='AfterCardDiscarded: owner card during own side turn -> GainBlock 3')
R('ToxicEgg', 3, None, 'modifier', 8, m='Skills in rewards/shop/deck are upgraded',
  r='TryModifyCardRewardOptionsLate[card_reward]: EggRelicHelper Skill | ModifyMerchantCardCreationResults[shop]: same | TryModifyCardBeingAddedToDeck[deck_change]: upgradable Skill -> upgraded clone', n='IsAllowed: IsBeforeAct3TreasureChest')
R('ToyBox', 3, None, 'prompt', 8, m='5 wax relics; one melts every 3 combats',
  r='AfterObtained[pickup]: 5x PullNextRelicFromFront, IsWax=true -> OfferCustom 5 RelicRewards | AfterCombatEnd[combat_end]: not used up -> CombatsSeen++; %3==0 -> RelicCmd.Melt(first wax relic not melted)',
  p=[P('RewardsScreen', 'RewardsCmd.OfferCustom', 'AfterObtained', '5 wax relic rewards', None, None, '')],
  s='CombatsSeen (SavedProperty; IsUsedUp at 15)', g='PlayerRng.Rewards (5 rarity rolls) + grab bag', k='wax/melt relics (RelicModel.IsWax/IsMelted, melted relics stop listening)')
R('TriBoomerang', 3, None, 'prompt', 9, m='Instinct on 3 cards',
  r='AfterObtained[pickup]: FromDeckForEnchantment(Instinct, 1) 3/3 -> Enchant Instinct(1)',
  p=[P('DeckEnchant', 'CardSelectCmd.FromDeckForEnchantment', 'AfterObtained', 'Deck (Instinct.CanEnchant)', '3', '3', '')], k='enchantments (Instinct)')
R('TungstenRod', 1, 1, None, 1, m='HP loss -1', c='ModifyHpLostAfterOsty: owner -> max(0, amount-1) | AfterModifyingHpLostAfterOsty: flash')
R('TuningFork', 1, 1, None, 1, m='7 block every 10 Skills', c='AfterCardPlayed: owner Skill -> SkillsPlayed++; >=10 -> GainBlock 7; SkillsPlayed-=10', s='SkillsPlayed (SavedProperty, persists across combats)')
R('TwistedFunnel', 1, 1, None, 2, m='4 Poison to all enemies turn 1', c='BeforeSideTurnStart: owner participates, TurnNumber<=1 -> PoisonPower(4) on each hittable enemy', k='PoisonPower')
R('UnceasingTop', 1, 1, None, 1, m='draw when the hand empties', c='AfterHandEmptied: owner, phase AutoPrePlay/Play/AutoPostPlay -> draw 1')
R('UndyingSigil', 1, 1, None, 2, m='doomed attackers deal half damage',
  c='ModifyDamageMultiplicative: powered attack on owner by another creature with CurrentHp <= its Doom -> x0.5', k='DoomPower')
R('UnsettlingLamp', 2, 2, None, 3, m='first debuff card each combat applies double',
  c='BeforePowerAmountChanged: first visible debuff from an owner card on an enemy without Artifact -> remember card | ModifyPowerAmountGivenMultiplicative: that card, not finished, not a doubled temporary-power source -> x2 | AfterCardPlayed: finish | BeforeCombatStart / AfterCombatEnd: reset',
  s='_triggeringCard, _doubledPowers, _isFinishedTriggering (combat-scoped)', k='ITemporaryPower.InternallyAppliedPower; PowerModel.IsVisible; ArtifactPower')
R('Vajra', 2, 2, None, 3, m='+1 Strength at combat start', c='AfterRoomEntered (combat room): StrengthPower(1)', k='Hook.AfterRoomEntered (combat room)')
R('Vambrace', 1, 1, None, 1, m='first block card each combat gives double',
  c='ModifyBlockMultiplicative: card/monster-move block from owner card, not yet used, matching remembered card -> x2 | AfterModifyingBlockAmount: remember card | AfterCardPlayed: used | BeforeCombatStart: reset | AfterCombatEnd: reset (redundant)', s='_triggeringCard, _blockGainedThisCombat (combat-scoped)')
R('VelvetChoker', 1, 1, None, 1, m='+1 max energy; at most 6 cards per turn',
  c='ModifyMaxEnergy: +1 | ShouldPlay: owner card -> cardsPlayedThisTurn<6 | AfterCardPlayed: count | BeforeSideTurnStart / AfterRoomEntered (combat) / AfterCombatEnd: reset (redundant)',
  s='_cardsPlayedThisTurn (turn-scoped)', k='ShouldPlay(card, AutoPlayType)')
R('VenerableTeaSet', 3, 1, 'simple', 7, m='+2 energy in the combat after resting',
  c='AfterEnergyReset: flag -> GainEnergy 2; flag=false', r='AfterRoomEntered[room_entry]: RestSiteRoom -> GainEnergyInNextCombat=true', s='GainEnergyInNextCombat (bool, SavedProperty)')
R('VeryHotCocoa', 1, 1, None, 1, m='+4 energy turn 1', c='AfterSideTurnStart: owner participates, TurnNumber<=1 -> GainEnergy 4', n='Ancient rarity in the Shared pool (treasure bag)')
R('VexingPuzzlebox', 3, 3, None, 4, m='random free character card turn 1',
  c='AfterPlayerTurnStart: owner, TurnNumber==1 -> GetDistinctForCombat(1, character pool, CombatCardGeneration) -> SetToFreeThisTurn -> AddGeneratedCardToCombat Hand', g='RunRng.CombatCardGeneration')
R('VitruvianMinion', 1, 1, None, 1, m='Minion cards deal and block double',
  c='ModifyDamageMultiplicative: owner card tagged Minion -> x2 | ModifyBlockMultiplicative: owner card tagged Minion -> x2')
R('WarHammer', 3, None, 'simple', 7, m='elite win upgrades 4 random cards',
  r='AfterCombatVictory[combat_end]: elite room -> StableShuffle(upgradable deck cards, Niche) take 4 -> Upgrade', g='RunRng.Niche', a='indirect: A1 SwarmingElites')
R('WarPaint', 3, None, 'simple', 7, m='upgrade 2 random Skills', r='AfterObtained[pickup]: StableShuffle(upgradable deck Skills, Niche) take 2 -> Upgrade', g='RunRng.Niche')
R('Whetstone', 3, None, 'simple', 7, m='upgrade 2 random Attacks', r='AfterObtained[pickup]: StableShuffle(upgradable deck Attacks, Niche) take 2 -> Upgrade', g='RunRng.Niche')
R('WhisperingEarring', 4, 4, None, 10, m='+1 max energy; turn 1 is auto-played',
  c='ModifyMaxEnergy: +1 | AfterAutoPrePlayPhaseEnteredLate: owner, TurnNumber<=1 -> push VakuuCardSelector; up to 13 times: first playable hand card -> SpendResources -> CardCmd.AutoPlay(target: first hittable enemy for AnyEnemy, CombatTargets.NextItem of other living players for AnyAlly, owner for AnyPlayer), stop when combat over, player ready or turn changed',
  g='RunRng.CombatTargets (AnyAlly targets only)', k='card selector stack on CombatState (VakuuCardSelector takes the first MaxSelect options); CardCmd.AutoPlay(skipXCapture); CardModel.SpendResources; Hook.AfterAutoPrePlayPhaseEnteredLate',
  n='cross-cutting: turn-1 autopilot suppresses every prompt via the selector; PaelsEye checks for it')
R('WhiteBeastStatue', 3, None, 'modifier', 8, m='combat rewards always include a potion', r='ShouldForcePotionReward[rewards]: combat room -> true', n='IsAllowed: IsBeforeAct3TreasureChest')
R('WhiteStar', 3, None, 'modifier', 8, m='elites give an extra boss-quality card reward',
  r='TryModifyRewards[rewards]: Elite room -> add CardReward(ForRoom Boss, 3)', g='PlayerRng.Rewards', a='indirect: A1 SwarmingElites; A7 Scarcity')
R('WingCharm', 3, None, 'modifier', 8, m='Swift on one random reward card',
  r='TryModifyCardRewardOptionsLate[card_reward]: Niche.NextItem(options Swift can enchant) -> clone + Enchant Swift(1)', g='RunRng.Niche', k='enchantments (Swift)')
R('WingedBoots', 3, None, 'cross-cutting', 8, m='3 free map moves',
  r='ShouldAllowFreeTravel[map]: not used up -> true | AfterRoomEntered[map]: first room of a point reached from a non-parent point -> TimesUsed++',
  s='TimesUsed (SavedProperty charges, 3)', n='IsAllowed: single player only', k='map travel legality (Hook.ShouldAllowFreeTravel); RunState.VisitedMapCoords')
R('WongoCustomerAppreciationBadge', 1, None, None, 1, m='none', n='event relic with no effect')
R('WongosMysteryTicket', 3, None, 'modifier', 8, m='3 relic rewards after 5 combats',
  r='AfterCombatEnd[combat_end]: CombatsFinished++ | TryModifyRewards[rewards]: combat room, not given, 5 combats done -> add 3x RelicReward | AfterModifyingRewards[rewards]: GaveRelic=true',
  s='CombatsFinished, GaveRelic (SavedProperty)', g='PlayerRng.Rewards (3 rarity rolls) + grab bag')
R('YummyCookie', 3, None, 'prompt', 9, m='upgrade 4 cards (choice)', r='AfterObtained[pickup]: FromDeckForUpgrade 4/4 -> Upgrade',
  p=[P('DeckUpgrade', 'CardSelectCmd.FromDeckForUpgrade', 'AfterObtained', 'Deck (IsUpgradable)', '4', '4', 'UpgradeSelectionPrompt')],
  n='icon uses Rng.Chaotic and a static cache (UI only)')
