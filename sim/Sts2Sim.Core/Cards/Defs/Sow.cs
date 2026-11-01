using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Sow : CardDef
{
    public const string CardId = "SOW";
    public const int Cost = 1;
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    public Sow()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sow = play.Card;
        DamageCmd.Attack(cs, sow.Vars.Damage.BaseValue).FromCard(sow, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
