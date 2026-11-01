using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Rainbow : CardDef
{
    public const string CardId = "RAINBOW";
    public const int Cost = 2;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Rainbow()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var owner = play.Card.Owner;
        OrbCmd.Channel<LightningOrb>(cs, owner);
        OrbCmd.Channel<FrostOrb>(cs, owner);
        OrbCmd.Channel<DarkOrb>(cs, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
