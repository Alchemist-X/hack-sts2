namespace Sts2Sim.Core.Cards;

public sealed class CrimsonMantle : CardDef
{
    public const string CardId = "CRIMSON_MANTLE";
    public const int Cost = 1;
    public const string CrimsonMantlePowerId = "CRIMSON_MANTLE_POWER";
    public const string PowerVarName = "CrimsonMantlePower";
    public const string IncrementSelfDamageMember = "IncrementSelfDamage";
    public const decimal BaseCrimsonMantle = 7m;
    public const decimal CrimsonMantleUpgrade = 3m;

    public CrimsonMantle()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseCrimsonMantle)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var crimsonMantle = play.Card;
        var owner = crimsonMantle.Owner;
        var power = ApplyAndReturn(cs, owner, crimsonMantle.Vars[PowerVarName].BaseValue, crimsonMantle);
        if (power != null)
        {
            IncrementSelfDamage(power);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(CrimsonMantleUpgrade);
    }

    private static PowerModel? ApplyAndReturn(CombatState cs, PlayerState owner, decimal amount, CardInstance cardSource)
    {
        if (CombatStatus.IsEnding(cs) || !CombatStatus.CanReceivePowers(cs, owner))
        {
            return null;
        }
        var canonical = PowerRegistry.Create(CrimsonMantlePowerId);
        var power = PowerCmd.FindExistingInstanceForStacking(canonical, owner, owner);
        if (power == null)
        {
            power = canonical;
            PowerCmd.Apply(cs, power, owner, amount, owner, cardSource);
        }
        else if (PowerCmd.ModifyAmount(cs, power, amount, owner, cardSource) == 0)
        {
            power = null;
        }
        return power;
    }

    private static void IncrementSelfDamage(PowerModel power)
    {
        var method = power.GetType().GetMethod(IncrementSelfDamageMember, System.Type.EmptyTypes)
            ?? throw new NotImplementedException("W3 gap: CrimsonMantlePower.IncrementSelfDamage");
        method.Invoke(power, null);
    }
}
