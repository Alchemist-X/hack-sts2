using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public static class StartingDecks
{
    public const string IroncladCharacterId = "IRONCLAD";

    public const int IroncladStartingHp = 80;

    public const int IroncladMaxEnergy = 3;

    public const decimal WearyTravelerHealMultiplier = 0.8m;

    public static ReadOnlyCollection<string> Ironclad { get; } = Array.AsReadOnly(new[]
    {
        StrikeIronclad.CardId,
        StrikeIronclad.CardId,
        StrikeIronclad.CardId,
        StrikeIronclad.CardId,
        StrikeIronclad.CardId,
        DefendIronclad.CardId,
        DefendIronclad.CardId,
        DefendIronclad.CardId,
        DefendIronclad.CardId,
        Bash.CardId
    });

    public static ReadOnlyCollection<string> IroncladWithAscendersBane { get; } = AppendAscendersBane(Ironclad);

    public static IReadOnlyList<string> IroncladAtRunStart(int ascension) => ApplyAscensionEffectsToDeck(Ironclad, ascension);

    public static bool IsIroncladStartingDeck(IReadOnlyList<string> deckIds)
    {
        ArgumentNullException.ThrowIfNull(deckIds);
        if (ReferenceEquals(deckIds, Ironclad))
        {
            return true;
        }
        if (deckIds.Count != Ironclad.Count)
        {
            return false;
        }
        for (int i = 0; i < deckIds.Count; i++)
        {
            if (!string.Equals(deckIds[i], Ironclad[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public static int IroncladHpAfterNeowArrival(int ascension) => HpAfterNeowArrival(IroncladStartingHp, ascension);

    public static IReadOnlyList<string> ApplyAscensionEffectsToDeck(IReadOnlyList<string> startingDeck, int ascension)
    {
        ArgumentNullException.ThrowIfNull(startingDeck);
        if (ascension < (int)AscensionLevel.AscendersBane)
        {
            return startingDeck;
        }
        if (ReferenceEquals(startingDeck, Ironclad))
        {
            return IroncladWithAscendersBane;
        }
        return AppendAscendersBane(startingDeck);
    }

    public static int HpAfterNeowArrival(int maxHp, int ascension)
    {
        if (maxHp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "Max HP must be positive.");
        }
        decimal currentHp = 0m;
        decimal heal = maxHp - currentHp;
        if (ascension >= (int)AscensionLevel.WearyTraveler)
        {
            heal *= WearyTravelerHealMultiplier;
        }
        return (int)Math.Min(currentHp + heal, maxHp);
    }

    private static ReadOnlyCollection<string> AppendAscendersBane(IReadOnlyList<string> startingDeck)
    {
        var deck = new string[startingDeck.Count + 1];
        for (int i = 0; i < startingDeck.Count; i++)
        {
            deck[i] = startingDeck[i];
        }
        deck[^1] = AscendersBane.CardId;
        return Array.AsReadOnly(deck);
    }
}
