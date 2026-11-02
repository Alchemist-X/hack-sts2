namespace Sts2Sim.Core.Relics;

public sealed class RuinedHelmet : RelicModel
{
    public const string RelicId = "RUINED_HELMET";

    public const decimal StrengthMultiplier = 2m;

    private bool _usedThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    private bool UsedThisCombat
    {
        get => _usedThisCombat;
        set => _usedThisCombat = value;
    }

    public override bool TryModifyPowerAmountReceived(CombatState cs, PowerModel canonicalPower, CreatureState target, decimal amount, CreatureState? applier, out decimal modifiedAmount)
    {
        modifiedAmount = amount;
        if (canonicalPower is not StrengthPower)
        {
            return false;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return false;
        }
        if (amount <= 0m)
        {
            return false;
        }
        if (UsedThisCombat)
        {
            return false;
        }
        modifiedAmount *= StrengthMultiplier;
        return true;
    }

    public override void AfterModifyingPowerAmountReceived(CombatState cs, PowerModel power)
    {
        UsedThisCombat = true;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        UsedThisCombat = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisCombat);
    }
}
