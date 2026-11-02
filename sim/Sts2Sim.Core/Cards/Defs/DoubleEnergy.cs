using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DoubleEnergy : CardDef
{
    public const string CardId = "DOUBLE_ENERGY";
    public const int Cost = 1;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public DoubleEnergy()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var owner = play.Card.Owner;
        PlayerCmd.GainEnergy(cs, owner.Energy, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
