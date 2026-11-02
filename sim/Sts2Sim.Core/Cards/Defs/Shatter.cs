using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Shatter : CardDef
{
    public const string CardId = "SHATTER";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal DamageUpgrade = 4m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Shatter()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var shatter = play.Card;
        DamageCmd.Attack(cs, shatter.Vars.Damage.BaseValue).FromCard(shatter, play).TargetingAllOpponents().Execute();
        var owner = shatter.Owner;
        int orbCount = owner.OrbQueue.Orbs.Count;
        for (int i = 0; i < orbCount; i++)
        {
            OrbCmd.EvokeNext(cs, owner, dequeue: false);
            OrbCmd.EvokeNext(cs, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
