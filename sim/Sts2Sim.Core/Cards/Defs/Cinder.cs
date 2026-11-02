namespace Sts2Sim.Core.Cards;

public sealed class Cinder : CardDef
{
    public const string CardId = "CINDER";
    public const int Cost = 2;
    public const decimal BaseDamage = 18m;
    public const decimal DamageUpgrade = 6m;

    public Cinder()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var pile = card.Owner.Hand;
        var cardModel = cs.Rng.CombatCardSelection.NextItem(pile.Cards);
        if (cardModel != null)
        {
            CardCmd.Exhaust(cs, cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
