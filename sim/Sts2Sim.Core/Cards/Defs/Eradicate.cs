using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Eradicate : CardDef
{
    public const string CardId = "ERADICATE";
    public const int Cost = 0;
    public const decimal BaseDamage = 11m;
    public const decimal DamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    public Eradicate()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy, costsX: true)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var eradicate = play.Card;
        DamageCmd.Attack(cs, eradicate.Vars.Damage.BaseValue).WithHitCount(eradicate.ResolveEnergyXValue(cs)).FromCard(eradicate, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
