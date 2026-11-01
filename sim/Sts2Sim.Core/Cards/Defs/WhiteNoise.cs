using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class WhiteNoise : CardDef
{
    public const string CardId = "WHITE_NOISE";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const int GeneratedCount = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public WhiteNoise()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var whiteNoise = play.Card;
        var owner = whiteNoise.Owner;
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(owner.CharacterId);
        var options = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.Type == CardType.Power);
        var cardModel = CardFactory.GetDistinctForCombat(cs, owner, options, GeneratedCount, rs.Rng.CombatCardGeneration).FirstOrDefault();
        if (cardModel != null)
        {
            cardModel.SetToFreeThisTurn();
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
