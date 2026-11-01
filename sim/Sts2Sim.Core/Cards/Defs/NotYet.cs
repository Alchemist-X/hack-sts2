using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class NotYet : CardDef
{
    public const string CardId = "NOT_YET";
    public const int Cost = 2;
    public const decimal BaseHeal = 10m;
    public const decimal HealUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public NotYet()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(BaseHeal)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CreatureCmd.Heal(cs, card.Owner, card.Vars.Heal.BaseValue);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Heal.UpgradeValueBy(HealUpgrade);
    }
}
