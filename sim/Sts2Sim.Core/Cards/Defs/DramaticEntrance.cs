using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DramaticEntrance : CardDef
{
    public const string CardId = "DRAMATIC_ENTRANCE";
    public const int Cost = 0;
    public const decimal BaseDamage = 11m;
    public const decimal DamageUpgrade = 4m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust, CardKeyword.Innate });

    public DramaticEntrance()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var dramaticEntrance = play.Card;
        DamageCmd.Attack(cs, dramaticEntrance.Vars.Damage.BaseValue).FromCard(dramaticEntrance, play).TargetingAllOpponents().Execute();
    }
}
