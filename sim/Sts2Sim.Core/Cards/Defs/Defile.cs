using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Defile : CardDef
{
    public const string CardId = "DEFILE";
    public const int Cost = 1;
    public const decimal BaseDamage = 13m;
    public const decimal DamageUpgrade = 4m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public Defile()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
        var defile = play.Card;
        DamageCmd.Attack(cs, defile.Vars.Damage.BaseValue).FromCard(defile, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
