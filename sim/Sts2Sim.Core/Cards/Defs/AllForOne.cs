namespace Sts2Sim.Core.Cards;

public sealed class AllForOne : CardDef
{
    public const string CardId = "ALL_FOR_ONE";
    public const int Cost = 2;
    public const decimal BaseDamage = 10m;
    public const decimal DamageUpgrade = 4m;

    public AllForOne()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    private static bool Filter(CombatState cs, CardInstance card)
    {
        bool flag = card.EnergyCost.GetWithModifiers(cs, card, CostModifiers.All) == 0 && !card.EnergyCost.CostsX;
        if (flag)
        {
            var type = card.Type;
            flag = type == CardType.Attack || type == CardType.Skill || type == CardType.Power;
        }
        return flag;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var owner = card.Owner;
        var cards = owner.DiscardPile.Cards.Where(c => Filter(cs, c)).ToList();
        foreach (var current in cards)
        {
            CardPileCmd.Add(cs, current, PileType.Hand);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
