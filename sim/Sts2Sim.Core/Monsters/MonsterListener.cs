namespace Sts2Sim.Core.Monsters;

public sealed class MonsterListener : AbstractModel, IOwnedListener<MonsterState>
{
    private readonly MonsterState _owner;

    public MonsterListener(MonsterState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public MonsterState Owner => _owner;

    public MonsterDef Def => _owner.Def;

    public AbstractModel CloneFor(MonsterState newOwner) => new MonsterListener(newOwner);

    public override void AfterCardChangedPilesLate(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
        => _owner.Def.AfterCardChangedPilesLate(rs, _owner, card, oldPileType, clonedBy);

    public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
        => _owner.Def.AfterCardGeneratedForCombat(cs, _owner, card, creator);

    public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta)
        => _owner.Def.AfterCurrentHpChanged(rs, _owner, creature, delta);

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        => _owner.Def.AfterDamageReceived(rs, _owner, target, result, props, dealer, cardSource);

    public override void BeforeDeath(RunState rs, CreatureState creature)
        => _owner.Def.BeforeDeath(rs, _owner, creature);

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
        => _owner.Def.AfterDeath(rs, _owner, creature, wasRemovalPrevented);
}
