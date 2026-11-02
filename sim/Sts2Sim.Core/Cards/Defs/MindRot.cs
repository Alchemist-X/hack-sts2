namespace Sts2Sim.Core.Cards;

public sealed class MindRot : CardDef
{
    public const string CardId = "MIND_ROT";
    public const int Cost = -1;
    public const string PowerId = "MIND_ROT_POWER";
    public const string PowerVarName = "MindRotPower";
    public const decimal BasePowerAmount = 1m;

    public MindRot()
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
