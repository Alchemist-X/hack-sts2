namespace Sts2Sim.Core.Cards;

public sealed class PactsEnd : CardDef
{
    public const string CardId = "PACTS_END";
    public const int Cost = 0;
    public const decimal BaseDamage = 18m;
    public const int BaseCards = 3;
    public const decimal DamageUpgrade = 6m;

    public PactsEnd()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public static bool CanDealDamage(CardInstance card) => card.Owner.ExhaustPile.Cards.Count >= card.Vars.Cards.IntValue;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        if (CanDealDamage(card))
        {
            DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).TargetingAllOpponents().Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
