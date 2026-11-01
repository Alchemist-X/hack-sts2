namespace Sts2Sim.Core.Cards;

public sealed class Inferno : CardDef
{
    public const string CardId = "INFERNO";
    public const int Cost = 1;
    public const string InfernoPowerId = "INFERNO_POWER";
    public const string InfernoVarName = "InfernoPower";
    public const string IncrementSelfDamageMethod = "IncrementSelfDamage";
    public const decimal BaseInferno = 6m;
    public const decimal InfernoUpgrade = 3m;

    public Inferno()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(InfernoVarName, BaseInferno)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var power = ApplyById(cs, InfernoPowerId, owner, card.Vars[InfernoVarName].BaseValue, owner, card);
        if (power != null)
        {
            ((InfernoPower)power).IncrementSelfDamage();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[InfernoVarName].UpgradeValueBy(InfernoUpgrade);
    }

    private static PowerModel? ApplyById(CombatState cs, string powerId, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (CombatStatus.IsEnding(cs))
        {
            return null;
        }
        if (!CombatStatus.CanReceivePowers(cs, target))
        {
            return null;
        }
        var canonical = PowerRegistry.Create(powerId);
        var power = PowerCmd.FindExistingInstanceForStacking(canonical, target, applier);
        if (power == null)
        {
            power = canonical;
            PowerCmd.Apply(cs, power, target, amount, applier, cardSource);
        }
        else if (PowerCmd.ModifyAmount(cs, power, amount, applier, cardSource) == 0)
        {
            power = null;
        }
        return power;
    }
}
