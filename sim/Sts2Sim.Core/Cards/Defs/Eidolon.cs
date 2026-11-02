using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Eidolon : CardDef
{
    public const string CardId = "EIDOLON";
    public const int Cost = 2;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Eidolon()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var eidolon = play.Card;
        var exhaust = eidolon.Owner.ExhaustPile.Cards;
        var cards = new List<CardInstance>(exhaust.Count);
        for (int i = 0; i < exhaust.Count; i++)
        {
            var c = exhaust[i];
            if (c.HasKeyword(CardKeyword.Ethereal) && !c.HasKeyword(CardKeyword.Unplayable))
            {
                cards.Add(c);
            }
        }
        for (int i = 0; i < cards.Count; i++)
        {
            CardCmd.AutoPlay(cs, cards[i], null);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
