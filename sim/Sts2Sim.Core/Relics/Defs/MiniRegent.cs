namespace Sts2Sim.Core.Relics;

public sealed class MiniRegent : RelicModel
{
    public const string RelicId = "MINI_REGENT";

    private bool _usedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(1m)
    };

    private bool UsedThisTurn
    {
        get => _usedThisTurn;
        set => _usedThisTurn = value;
    }

    public override void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)
    {
        if (ReferenceEquals(spender, Owner) && !UsedThisTurn)
        {
            UsedThisTurn = true;
            PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars.Strength.BaseValue, Owner, null);
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        UsedThisTurn = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        UsedThisTurn = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisTurn);
    }
}
