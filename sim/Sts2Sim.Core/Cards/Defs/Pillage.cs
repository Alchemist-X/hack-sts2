namespace Sts2Sim.Core.Cards;

public sealed class Pillage : CardDef
{
    public const string CardId = "PILLAGE";
    public const int Cost = 1;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 3m;

    public Pillage()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        CardInstance? cardModel;
        do
        {
            cardModel = CardPileCmd.Draw(cs, owner);
        }
        while (cardModel != null && cardModel.Type == CardType.Attack && owner.Hand.Cards.Count < CardPile.MaxCardsInHand);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
