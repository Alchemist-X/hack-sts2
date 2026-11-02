using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class ForbiddenGrimoire : CardDef
{
    public const string CardId = "FORBIDDEN_GRIMOIRE";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const string ForbiddenGrimoirePowerId = "FORBIDDEN_GRIMOIRE_POWER";
    public const decimal PowerAmount = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Eternal });

    public ForbiddenGrimoire()
        : base(CardId, Cost, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var grimoire = play.Card;
        var owner = grimoire.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(ForbiddenGrimoirePowerId), owner, PowerAmount, owner, grimoire);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
