using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class FiendFire : CardDef
{
    public const string CardId = "FIEND_FIRE";
    public const int Cost = 2;
    public const decimal BaseDamage = 7m;
    public const decimal DamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public FiendFire()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var fiendFire = play.Card;
        var hand = fiendFire.Owner.Hand.Cards.ToList();
        int cardCount = hand.Count;
        foreach (var current in hand)
        {
            CardCmd.Exhaust(cs, current);
        }
        DamageCmd.Attack(cs, fiendFire.Vars.Damage.BaseValue).WithHitCount(cardCount).FromCard(fiendFire, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
