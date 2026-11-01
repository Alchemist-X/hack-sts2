using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Alchemize : CardDef
{
    public const string CardId = "ALCHEMIZE";
    public const int Cost = 1;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Alchemize()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var alchemize = play.Card;
        var owner = alchemize.Owner;
        var rs = cs.Run;
        PotionCmd.TryToProcure(rs, PotionFactory.CreateRandomPotionInCombat(rs, owner, rs.Rng.CombatPotionGeneration).ToMutable(), owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
