namespace Sts2Sim.Core.Cards;

public sealed class Uproar : CardDef
{
    public const string CardId = "UPROAR";
    public const int Cost = 2;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 2m;
    public const int HitCount = 2;

    public Uproar()
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
        var uproar = play.Card;
        DamageCmd.Attack(cs, uproar.Vars.Damage.BaseValue).FromCard(uproar, play).WithHitCount(HitCount).Targeting(target).Execute();
        var drawPile = uproar.Owner.DrawPile;
        var cardModel = drawPile.Cards.Where(c => c.Type == CardType.Attack && !c.HasKeyword(CardKeyword.Unplayable)).ToList().StableShuffle(cs.Rng.Shuffle).FirstOrDefault();
        if (cardModel == null)
        {
            cardModel = drawPile.Cards.Where(c => c.Type == CardType.Attack).ToList().StableShuffle(cs.Rng.Shuffle).FirstOrDefault();
        }
        if (cardModel != null)
        {
            CardCmd.AutoPlay(cs, cardModel, null);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
