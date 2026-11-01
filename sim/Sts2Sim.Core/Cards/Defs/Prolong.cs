using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Prolong : CardDef
{
    public const string CardId = "PROLONG";
    public const int Cost = 0;
    public const string BlockNextTurnPowerId = "BLOCK_NEXT_TURN_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Prolong()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, BlockNextTurnPowerId, owner, owner.Block, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
