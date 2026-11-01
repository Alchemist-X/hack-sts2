using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class FlickFlack : CardDef
{
    public const string CardId = "FLICK_FLACK";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal DamageUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Sly });

    public FlickFlack()
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
        var flickFlack = play.Card;
        DamageCmd.Attack(cs, flickFlack.Vars.Damage.BaseValue).FromCard(flickFlack, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
