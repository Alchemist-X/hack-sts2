namespace Sts2Sim.Core.Relics;

public sealed class LizardTail : RelicModel
{
    public const string RelicId = "LIZARD_TAIL";

    private bool _wasUsed;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsUsedUp => _wasUsed;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(50m)
    };

    public bool WasUsed
    {
        get => _wasUsed;
        set
        {
            _wasUsed = value;
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    public override bool ShouldDieLate(RunState rs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return true;
        }
        if (WasUsed)
        {
            return true;
        }
        return false;
    }

    public override void AfterPreventingDeath(RunState rs, CreatureState creature)
    {
        WasUsed = true;
        decimal amount = Math.Max(1m, creature.MaxHp * (DynamicVars.Heal.BaseValue / 100m));
        CreatureCmd.Heal(rs, creature, amount);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_wasUsed);
    }
}
