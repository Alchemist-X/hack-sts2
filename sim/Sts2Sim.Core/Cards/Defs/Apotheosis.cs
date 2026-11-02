using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Apotheosis : CardDef
{
    public const string CardId = "APOTHEOSIS";
    public const int Cost = 2;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust, CardKeyword.Innate });

    public Apotheosis()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var apotheosis = play.Card;
        var piles = apotheosis.Owner.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var current = cards[i];
                if (!ReferenceEquals(current, apotheosis) && current.IsUpgradable)
                {
                    CardCmd.Upgrade(cs, current);
                }
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
