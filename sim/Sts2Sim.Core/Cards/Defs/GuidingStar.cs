namespace Sts2Sim.Core.Cards;

public sealed class GuidingStar : CardDef
{
    public const string CardId = "GUIDING_STAR";
    public const int Cost = 1;
    public const int StarCost = 1;
    public const string DrawCardsNextTurnPowerId = "DRAW_CARDS_NEXT_TURN_POWER";
    public const decimal BaseDamage = 12m;
    public const int BaseCards = 2;
    public const decimal DamageUpgrade = 1m;
    public const decimal CardsUpgrade = 1m;

    public GuidingStar()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var guidingStar = play.Card;
        var owner = guidingStar.Owner;
        DamageCmd.Attack(cs, guidingStar.Vars.Damage.BaseValue).FromCard(guidingStar, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(DrawCardsNextTurnPowerId), owner, guidingStar.Vars.Cards.BaseValue, owner, guidingStar);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
