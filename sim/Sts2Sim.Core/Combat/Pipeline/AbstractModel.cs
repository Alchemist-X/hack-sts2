namespace Sts2Sim.Core.Combat.Pipeline;

public abstract class AbstractModel
{
    public virtual void AfterActEntered(RunState rs)
    {
    }

    public virtual void AfterAddToDeckPrevented(RunState rs, CardInstance card)
    {
    }

    public virtual void BeforeAttack(CombatState cs, AttackCommand command)
    {
    }

    public virtual void AfterAttack(CombatState cs, AttackCommand command)
    {
    }

    public virtual void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterAutoPrePlayPhaseEnteredEarly(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterAutoPrePlayPhaseEnteredLate(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
    }

    public virtual void BeforeBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
    }

    public virtual void AfterBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
    }

    public virtual void AfterBlockBroken(CombatState cs, CreatureState target, CreatureState? breaker)
    {
    }

    public virtual void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
    }

    public virtual void AfterCardChangedPilesLate(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
    }

    public virtual void AfterCardDiscarded(CombatState cs, CardInstance card)
    {
    }

    public virtual void AfterCardDrawnEarly(CombatState cs, CardInstance card, bool fromHandDraw)
    {
    }

    public virtual void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
    }

    public virtual void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
    }

    public virtual void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
    }

    public virtual void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
    }

    public virtual void BeforeCardAutoPlayed(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type)
    {
    }

    public virtual void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
    }

    public virtual void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
    }

    public virtual void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay)
    {
    }

    public virtual void BeforeCombatStart(RunState rs)
    {
    }

    public virtual void BeforeCombatStartLate(RunState rs)
    {
    }

    public virtual void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
    }

    public virtual void BeforeCombatRewardOffered(RunState rs, RewardsSet rewards, CombatRoom room)
    {
    }

    public virtual void AfterCombatVictoryEarly(RunState rs, CombatRoom? room)
    {
    }

    public virtual void AfterCombatVictory(RunState rs, CombatRoom? room)
    {
    }

    public virtual void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
    }

    public virtual void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta)
    {
    }

    public virtual void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
    }

    public virtual void BeforeDamageReceived(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void AfterDamageReceivedLate(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void BeforeDeath(RunState rs, CreatureState creature)
    {
    }

    public virtual void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
    }

    public virtual void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)
    {
    }

    public virtual void AfterEnergyReset(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterEnergyResetLate(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterEnergySpent(CombatState cs, CardInstance card, int amount)
    {
    }

    public virtual void BeforeCardRemoved(RunState rs, CardInstance card)
    {
    }

    public virtual void BeforeFlush(CombatState cs, PlayerState player)
    {
    }

    public virtual void BeforeFlushLate(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterFlush(CombatState cs, PlayerState player, IReadOnlyList<CardInstance> flushedCards, IReadOnlyList<CardInstance> retainedCards)
    {
    }

    public virtual void AfterGoldGained(RunState rs, PlayerState player)
    {
    }

    public virtual void BeforeHandDraw(CombatState cs, PlayerState player)
    {
    }

    public virtual void BeforeHandDrawLate(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterHandEmptied(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterItemPurchased(RunState rs, PlayerState player, MerchantEntry itemPurchased, int goldSpent)
    {
    }

    public virtual void AfterMapGenerated(RunState rs, ActMap map, int actIndex)
    {
    }

    public virtual void AfterModifyingBlockAmount(CombatState cs, decimal modifiedAmount, CardInstance? cardSource, CardPlay? cardPlay)
    {
    }

    public virtual void AfterModifyingCardPlayCount(CombatState cs, CardInstance card)
    {
    }

    public virtual void AfterModifyingCardPlayResultLocation(CombatState cs, CardInstance card, CardLocation cardLocation)
    {
    }

    public virtual void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb)
    {
    }

    public virtual void AfterModifyingCardRewardOptions(RunState rs)
    {
    }

    public virtual void AfterModifyingDamageAmount(RunState rs, CardInstance? cardSource)
    {
    }

    public virtual void AfterModifyingEnergyGain(CombatState cs)
    {
    }

    public virtual void AfterModifyingGoldGained(RunState rs, PlayerState player, decimal amount)
    {
    }

    public virtual void AfterModifyingHandDraw(CombatState cs)
    {
    }

    public virtual void AfterPreventingDraw(CombatState cs)
    {
    }

    public virtual void AfterModifyingHpLostBeforeOsty(RunState rs)
    {
    }

    public virtual void AfterModifyingHpLostAfterOsty(RunState rs)
    {
    }

    public virtual void AfterModifyingPowerAmountReceived(CombatState cs, PowerModel power)
    {
    }

    public virtual void AfterModifyingPowerAmountGiven(CombatState cs, PowerModel power)
    {
    }

    public virtual void AfterModifyingRewards(RunState rs)
    {
    }

    public virtual void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)
    {
    }

    public virtual void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)
    {
    }

    public virtual void AfterOstyRevived(CombatState cs, CreatureState osty)
    {
    }

    public virtual void BeforePotionUsed(RunState rs, PotionModel potion, CreatureState? target)
    {
    }

    public virtual void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target)
    {
    }

    public virtual void AfterPotionDiscarded(RunState rs, PotionModel potion)
    {
    }

    public virtual void AfterPotionProcured(RunState rs, PotionModel potion)
    {
    }

    public virtual void BeforePowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState target, CreatureState? applier, CardInstance? cardSource)
    {
    }

    public virtual void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
    }

    public virtual void AfterPreventingBlockClear(CombatState cs, AbstractModel preventer, CreatureState creature)
    {
    }

    public virtual void AfterPreventingDeath(RunState rs, CreatureState creature)
    {
    }

    public virtual void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)
    {
    }

    public virtual void AfterRestSiteSmith(RunState rs, PlayerState player)
    {
    }

    public virtual void AfterRewardTaken(RunState rs, PlayerState player, Reward reward)
    {
    }

    public virtual void BeforeRoomEntered(RunState rs, AbstractRoom room)
    {
    }

    public virtual void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
    }

    public virtual void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
    }

    public virtual void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)
    {
    }

    public virtual void AfterStarsGained(CombatState cs, int amount, PlayerState gainer)
    {
    }

    public virtual void AfterForge(CombatState cs, decimal amount, PlayerState forger, AbstractModel? source)
    {
    }

    public virtual void AfterSummon(CombatState cs, PlayerState summoner, decimal amount)
    {
    }

    public virtual void AfterTakingExtraTurn(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterTargetingBlockedVfx(CombatState cs, CreatureState blocker)
    {
    }

    public virtual void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void AfterSideTurnStartLate(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void AfterPlayerTurnStartEarly(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterPlayerTurnStartLate(CombatState cs, PlayerState player)
    {
    }

    public virtual void BeforeSideTurnEndVeryEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void BeforeSideTurnEndEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual void AfterSideTurnEndLate(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
    }

    public virtual int ModifyAttackHitCount(CombatState cs, AttackCommand attack, int hitCount) => hitCount;

    public virtual decimal ModifyBlockAdditive(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) => 0m;

    public virtual decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) => 1m;

    public virtual int ModifyCardPlayCount(CombatState cs, CardInstance card, CreatureState? target, int playCount) => playCount;

    public virtual CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation) => cardLocation;

    public virtual int ModifyOrbPassiveTriggerCounts(CombatState cs, OrbModel orb, int triggerCount) => triggerCount;

    public virtual CardCreationOptions ModifyCardRewardCreationOptions(RunState rs, PlayerState player, CardCreationOptions options) => options;

    public virtual CardCreationOptions ModifyCardRewardCreationOptionsLate(RunState rs, PlayerState player, CardCreationOptions options) => options;

    public virtual decimal ModifyCardRewardUpgradeOdds(RunState rs, PlayerState player, CardInstance card, decimal odds) => odds;

    public virtual decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay) => 0m;

    public virtual decimal ModifyDamageCap(RunState rs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay) => decimal.MaxValue;

    public virtual decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay) => 1m;

    public virtual decimal ModifyEnergyGain(CombatState cs, PlayerState player, decimal amount) => amount;

    public virtual decimal ModifyGoldGained(RunState rs, PlayerState player, decimal amount) => amount;

    public virtual ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex) => map;

    public virtual ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex) => map;

    public virtual decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count) => count;

    public virtual decimal ModifyHandDrawLate(CombatState cs, PlayerState player, decimal count) => count;

    public virtual decimal ModifyHpLostBeforeOsty(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyHpLostBeforeOstyLate(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyHpLostAfterOsty(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyHpLostAfterOstyLate(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount) => amount;

    public virtual IEnumerable<CardDef> ModifyMerchantCardPool(RunState rs, PlayerState player, IEnumerable<CardDef> options) => options;

    public virtual CardRarity ModifyMerchantCardRarity(RunState rs, PlayerState player, CardRarity rarity) => rarity;

    public virtual void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)
    {
    }

    public virtual decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal cost) => cost;

    public virtual decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal value) => value;

    public virtual decimal ModifyPowerAmountGivenAdditive(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource) => 0m;

    public virtual decimal ModifyPowerAmountGivenMultiplicative(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource) => 1m;

    public virtual decimal ModifyRestSiteHealAmount(RunState rs, CreatureState creature, decimal amount) => amount;

    public virtual void ModifyShuffleOrder(CombatState cs, PlayerState player, List<CardInstance> cards, bool isInitialShuffle)
    {
    }

    public virtual decimal ModifySummonAmount(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source) => amount;

    public virtual CreatureState ModifyUnblockedDamageTarget(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer) => target;

    public virtual EventModel ModifyNextEvent(RunState rs, EventModel currentEvent) => currentEvent;

    public virtual IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes) => roomTypes;

    public virtual float ModifyOddsIncreaseForUnrolledRoomType(RunState rs, RoomType roomType, float oddsIncrease) => oddsIncrease;

    public virtual int ModifyXValue(CombatState cs, CardInstance card, int originalValue) => originalValue;

    public virtual bool TryModifyCardBeingAddedToDeck(RunState rs, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        return false;
    }

    public virtual bool TryModifyCardBeingAddedToDeckLate(RunState rs, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        return false;
    }

    public virtual bool TryModifyCardRewardAlternatives(RunState rs, PlayerState player, CardReward cardReward, List<CardRewardAlternative> alternatives) => false;

    public virtual bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions) => false;

    public virtual bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions) => false;

    public virtual bool TryModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        return false;
    }

    public virtual bool TryModifyEnergyCostInCombatLate(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        return false;
    }

    public virtual bool TryModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords) => false;

    public virtual bool TryModifyStarCost(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        return false;
    }

    public virtual bool TryModifyPowerAmountReceived(CombatState cs, PowerModel canonicalPower, CreatureState target, decimal amount, CreatureState? applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;
        return false;
    }

    public virtual bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options) => false;

    public virtual bool TryModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked) => false;

    public virtual bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room) => false;

    public virtual bool TryModifyRewardsLate(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room) => false;

    public virtual IReadOnlyList<string> ModifyExtraRestSiteHealText(RunState rs, PlayerState player, IReadOnlyList<string> currentExtraText) => currentExtraText;

    public virtual bool ShouldAddToDeck(RunState rs, CardInstance card) => true;

    public virtual bool ShouldAfflict(CombatState cs, CardInstance card, AfflictionModel affliction) => true;

    public virtual bool ShouldAllowAncient(RunState rs, PlayerState player, AncientEventModel ancient) => true;

    public virtual bool ShouldAllowHitting(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldAllowTargeting(CombatState cs, CreatureState target) => true;

    public virtual bool ShouldAllowSelectingMoreCardRewards(RunState rs, PlayerState player, CardReward cardReward) => false;

    public virtual bool ShouldClearBlock(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldDie(RunState rs, CreatureState creature) => true;

    public virtual bool ShouldDieLate(RunState rs, CreatureState creature) => true;

    public virtual bool ShouldDisableRemainingRestSiteOptions(RunState rs, PlayerState player) => true;

    public virtual bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw) => true;

    public virtual bool ShouldEtherealTrigger(CombatState cs, CardInstance card) => true;

    public virtual bool ShouldFlush(CombatState cs, PlayerState player) => true;

    public virtual bool ShouldGainStars(CombatState cs, decimal amount, PlayerState player) => true;

    public virtual bool ShouldGenerateTreasure(RunState rs, PlayerState player) => true;

    public virtual bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player) => false;

    public virtual bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType) => true;

    public virtual bool ShouldPlayerResetEnergy(CombatState cs, PlayerState player) => true;

    public virtual bool ShouldProceedToNextMapPoint(RunState rs) => true;

    public virtual bool ShouldProcurePotion(RunState rs, PotionModel potion, PlayerState player) => true;

    public virtual bool ShouldPowerBeRemovedOnDeath(CombatState cs, PowerModel power) => true;

    public virtual bool ShouldRefillMerchantEntry(RunState rs, MerchantEntry entry, PlayerState player) => false;

    public virtual bool ShouldAllowMerchantCardRemoval(RunState rs, PlayerState player) => true;

    public virtual bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldStopCombatFromEnding(CombatState cs) => false;

    public virtual bool ShouldTakeExtraTurn(CombatState cs, PlayerState player) => false;

    public virtual bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType) => false;

    public virtual bool ShouldAllowFreeTravel(RunState rs) => false;
}
