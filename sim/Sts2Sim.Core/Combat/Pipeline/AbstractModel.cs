namespace Sts2Sim.Core.Combat.Pipeline;

public abstract class AbstractModel
{
    public virtual void BeforeCombatStart(CombatState cs)
    {
    }

    public virtual void BeforeCombatStartLate(CombatState cs)
    {
    }

    public virtual void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
    }

    public virtual void BeforeSideTurnStart(CombatState cs, CombatSide side)
    {
    }

    public virtual void AfterSideTurnStart(CombatState cs, CombatSide side)
    {
    }

    public virtual void AfterSideTurnStartLate(CombatState cs, CombatSide side)
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

    public virtual void BeforeSideTurnEndVeryEarly(CombatState cs, CombatSide side)
    {
    }

    public virtual void BeforeSideTurnEndEarly(CombatState cs, CombatSide side)
    {
    }

    public virtual void BeforeSideTurnEnd(CombatState cs, CombatSide side)
    {
    }

    public virtual void AfterSideTurnEnd(CombatState cs, CombatSide side)
    {
    }

    public virtual void AfterSideTurnEndLate(CombatState cs, CombatSide side)
    {
    }

    public virtual void AfterTakingExtraTurn(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterEnergyReset(CombatState cs, PlayerState player)
    {
    }

    public virtual void AfterEnergyResetLate(CombatState cs, PlayerState player)
    {
    }

    public virtual void BeforeHandDraw(CombatState cs, PlayerState player)
    {
    }

    public virtual void BeforeHandDrawLate(CombatState cs, PlayerState player)
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

    public virtual void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
    }

    public virtual void AfterHandEmptied(CombatState cs, PlayerState player)
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

    public virtual void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
    }

    public virtual void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
    }

    public virtual void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay)
    {
    }

    public virtual void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
    }

    public virtual void AfterCardDiscarded(CombatState cs, CardInstance card)
    {
    }

    public virtual void AfterEnergySpent(CombatState cs, CardInstance card, int amount)
    {
    }

    public virtual void BeforeAttack(CombatState cs, AttackCommand command)
    {
    }

    public virtual void AfterAttack(CombatState cs, AttackCommand command)
    {
    }

    public virtual void BeforeDamageReceived(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
    }

    public virtual void AfterDamageReceived(CombatState cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void AfterDamageReceivedLate(CombatState cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void AfterCurrentHpChanged(CombatState cs, CreatureState creature, decimal delta)
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

    public virtual void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
    }

    public virtual void AfterPreventingBlockClear(CombatState cs, AbstractModel preventer, CreatureState creature)
    {
    }

    public virtual void BeforePowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState target, CreatureState? applier, CardInstance? cardSource)
    {
    }

    public virtual void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
    }

    public virtual void BeforeDeath(CombatState cs, CreatureState creature)
    {
    }

    public virtual void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented)
    {
    }

    public virtual void AfterPreventingDeath(CombatState cs, CreatureState creature)
    {
    }

    public virtual void AfterPreventingDraw(CombatState cs)
    {
    }

    public virtual void AfterModifyingDamageAmount(CombatState cs, CardInstance? cardSource)
    {
    }

    public virtual void AfterModifyingBlockAmount(CombatState cs, decimal modifiedAmount, CardInstance? cardSource, CardPlay? cardPlay)
    {
    }

    public virtual void AfterModifyingHpLostBeforeOsty(CombatState cs)
    {
    }

    public virtual void AfterModifyingHpLostAfterOsty(CombatState cs)
    {
    }

    public virtual void AfterModifyingPowerAmountReceived(CombatState cs, PowerModel power)
    {
    }

    public virtual void AfterModifyingPowerAmountGiven(CombatState cs, PowerModel power)
    {
    }

    public virtual void AfterModifyingEnergyGain(CombatState cs)
    {
    }

    public virtual void AfterModifyingHandDraw(CombatState cs)
    {
    }

    public virtual void AfterModifyingCardPlayCount(CombatState cs, CardInstance card)
    {
    }

    public virtual int ModifyAttackHitCount(CombatState cs, AttackCommand attack, int hitCount) => hitCount;

    public virtual int ModifyCardPlayCount(CombatState cs, CardInstance card, CreatureState? target, int playCount) => playCount;

    public virtual int ModifyXValue(CombatState cs, CardInstance card, int originalValue) => originalValue;

    public virtual decimal ModifyDamageAdditive(CombatState cs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay) => 0m;

    public virtual decimal ModifyDamageMultiplicative(CombatState cs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay) => 1m;

    public virtual decimal ModifyDamageCap(CombatState cs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay) => decimal.MaxValue;

    public virtual decimal ModifyBlockAdditive(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) => 0m;

    public virtual decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) => 1m;

    public virtual decimal ModifyHpLostBeforeOsty(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyHpLostBeforeOstyLate(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyHpLostAfterOsty(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual decimal ModifyHpLostAfterOstyLate(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource) => amount;

    public virtual CreatureState ModifyUnblockedDamageTarget(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer) => target;

    public virtual decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count) => count;

    public virtual decimal ModifyHandDrawLate(CombatState cs, PlayerState player, decimal count) => count;

    public virtual decimal ModifyEnergyGain(CombatState cs, PlayerState player, decimal amount) => amount;

    public virtual decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount) => amount;

    public virtual void ModifyShuffleOrder(CombatState cs, PlayerState player, List<CardInstance> cards, bool isInitialShuffle)
    {
    }

    public virtual decimal ModifyPowerAmountGivenAdditive(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource) => 0m;

    public virtual decimal ModifyPowerAmountGivenMultiplicative(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource) => 1m;

    public virtual bool TryModifyPowerAmountReceived(CombatState cs, PowerModel canonicalPower, CreatureState target, decimal amount, CreatureState? applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;
        return false;
    }

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

    public virtual bool TryModifyStarCost(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        return false;
    }

    public virtual bool ShouldAllowHitting(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldAllowTargeting(CombatState cs, CreatureState target) => true;

    public virtual bool ShouldClearBlock(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldDie(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldDieLate(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw) => true;

    public virtual bool ShouldEtherealTrigger(CombatState cs, CardInstance card) => true;

    public virtual bool ShouldPlay(CombatState cs, CardInstance card) => true;

    public virtual bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player) => false;

    public virtual bool ShouldPlayerResetEnergy(CombatState cs, PlayerState player) => true;

    public virtual bool ShouldFlush(CombatState cs, PlayerState player) => true;

    public virtual bool ShouldPowerBeRemovedOnDeath(CombatState cs, PowerModel power) => true;

    public virtual bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => true;

    public virtual bool ShouldStopCombatFromEnding(CombatState cs) => false;

    public virtual bool ShouldTakeExtraTurn(CombatState cs, PlayerState player) => false;
}
