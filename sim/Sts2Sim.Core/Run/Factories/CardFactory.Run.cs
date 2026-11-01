namespace Sts2Sim.Core.Cards;

public static partial class CardFactory
{
    public static CardCreationResult CreateForMerchant(RunState rs, PlayerState player, IEnumerable<CardDef> options, CardType type) => throw new NotImplementedException("W2 R3: CardFactory.CreateForMerchant(type)");

    public static CardCreationResult CreateForMerchant(RunState rs, PlayerState player, IEnumerable<CardDef> options, CardRarity rarity) => throw new NotImplementedException("W2 R3: CardFactory.CreateForMerchant(rarity)");

    public static IEnumerable<CardCreationResult> CreateForReward(RunState rs, PlayerState player, int cardCount, CardCreationOptions options) => throw new NotImplementedException("W2 R3: CardFactory.CreateForReward");
}
