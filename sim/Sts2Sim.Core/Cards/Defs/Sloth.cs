namespace Sts2Sim.Core.Cards;

public sealed class Sloth : CardDef
{
    public const string CardId = "SLOTH";
    public const int Cost = -1;
    public const string PowerId = "SLOTH_POWER";
    public const string PowerVarName = "SlothPower";
    public const decimal BasePowerAmount = 3m;

    public Sloth()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePowerAmount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public void OnChosen(CombatState cs, CardInstance card)
    {
        var creature = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PowerId), creature, card.Vars[PowerVarName].IntValue, creature, card);
    }
}
