using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Greed : CardDef
{
    public const string CardId = "GREED";
    public const int Cost = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Eternal, CardKeyword.Unplayable });

    public Greed()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override bool CanBeGeneratedByModifiers => false;

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }
}
