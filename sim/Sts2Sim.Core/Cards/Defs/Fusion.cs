using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Fusion : CardDef
{
    public const string CardId = "FUSION";
    public const int Cost = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Fusion()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        OrbCmd.Channel<PlasmaOrb>(cs, play.Card.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
