namespace Sts2Sim.Core.Run;

public sealed partial class CardCreationOptions
{
    public CardCreationOptions(IEnumerable<CardPool> cardPools, CardRarityOddsType rarityOdds, Func<CardDef, bool>? cardPoolFilter = null)
    {
        CardPools = cardPools.ToList();
        RarityOdds = rarityOdds;
        CardPoolFilter = cardPoolFilter;
    }

    public List<CardPool> CardPools { get; private set; }

    public CardRarityOddsType RarityOdds { get; private set; }

    public Func<CardDef, bool>? CardPoolFilter { get; private set; }

    public static CardCreationOptions ForRoom(RunState rs, PlayerState player, RoomType roomType) => throw new NotImplementedException("W2 R3: CardCreationOptions.ForRoom");

    public static CardCreationOptions ForNonCombatWithDefaultOdds(IEnumerable<CardPool> cardPools, Func<CardDef, bool>? cardPoolFilter = null) => throw new NotImplementedException("W2 R3: CardCreationOptions.ForNonCombatWithDefaultOdds");

    public static CardCreationOptions ForNonCombatWithUniformOdds(IEnumerable<CardPool> cardPools, Func<CardDef, bool>? cardPoolFilter = null) => throw new NotImplementedException("W2 R3: CardCreationOptions.ForNonCombatWithUniformOdds");

    public IEnumerable<CardDef> GetPossibleCards(RunState rs, PlayerState player) => throw new NotImplementedException("W2 R3: CardCreationOptions.GetPossibleCards");
}
