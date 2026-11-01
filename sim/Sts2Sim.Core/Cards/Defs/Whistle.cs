using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Whistle : CardDef
{
    public const string CardId = "WHISTLE";
    public const int Cost = 2;
    public const decimal BaseDamage = 33m;
    public const decimal DamageUpgrade = 11m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Whistle()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
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
        var whistle = play.Card;
        DamageCmd.Attack(cs, whistle.Vars.Damage.BaseValue).FromCard(whistle, play).Targeting(target).Execute();
        if (target is not MonsterState monster)
        {
            throw new InvalidOperationException("Can't stun a player.");
        }
        CreatureCmd.Stun(cs, monster);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
