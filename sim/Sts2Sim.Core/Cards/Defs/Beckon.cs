namespace Sts2Sim.Core.Cards;

public sealed class Beckon : CardDef
{
    public const string CardId = "BECKON";
    public const int Cost = 1;
    public const decimal BaseHpLoss = 6m;

    public Beckon()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override bool HasTurnEndInHandEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss)
    };

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        CreatureCmd.Damage(cs, card.Owner, card.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, null);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }
}
