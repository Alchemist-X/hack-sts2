using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Ricochet : CardDef
{
    public const string CardId = "RICOCHET";
    public const int Cost = 2;
    public const decimal BaseDamage = 3m;
    public const int BaseRepeat = 4;
    public const decimal RepeatUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Sly });

    public Ricochet()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.RandomEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var ricochet = play.Card;
        DamageCmd.Attack(cs, ricochet.Vars.Damage.BaseValue).WithHitCount(ricochet.Vars.Repeat.IntValue).FromCard(ricochet, play).TargetingRandomOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
