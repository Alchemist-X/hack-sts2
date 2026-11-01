namespace Sts2Sim.Core.Cards;

public sealed class PreciseCut : CardDef
{
    public const string CardId = "PRECISE_CUT";
    public const int Cost = 0;
    public const decimal BaseCalculationBase = 13m;
    public const decimal BaseExtraDamage = 2m;
    public const decimal CalculationBaseUpgrade = 3m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> HandMultiplier = static (_, card, _) =>
    {
        var owner = card.Owner;
        int num = owner.Hand.Cards.Count;
        var pile = owner.FindPileContaining(card);
        if (pile != null && pile.Type == PileType.Hand)
        {
            num--;
        }
        return -num;
    };

    public PreciseCut()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(HandMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var preciseCut = play.Card;
        DamageCmd.Attack(cs, (c, t) => preciseCut.Vars.CalculatedDamage.Calculate(c, preciseCut, t)).FromCard(preciseCut, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
    }
}
