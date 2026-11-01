using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Spur : CardDef
{
    public const string CardId = "SPUR";
    public const int Cost = 1;
    public const decimal BaseSummon = 3m;
    public const decimal BaseHeal = 5m;
    public const decimal SummonUpgrade = 2m;
    public const decimal HealUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    public Spur()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon),
        new HealVar(BaseHeal)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var spur = play.Card;
        var owner = spur.Owner;
        OstyCmd.Summon(cs, owner, spur.Vars.Summon.BaseValue, null);
        CreatureCmd.Heal(cs, CombatStatus.GetOsty(cs, owner)!, spur.Vars.Heal.BaseValue);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
        card.Vars.Heal.UpgradeValueBy(HealUpgrade);
    }
}
