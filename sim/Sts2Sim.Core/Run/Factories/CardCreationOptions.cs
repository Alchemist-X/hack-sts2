namespace Sts2Sim.Core.Run;

public enum CardCreationSource
{
    None,
    Encounter,
    Shop,
    Other
}

[Flags]
public enum CardCreationFlags
{
    None = 0,
    NoRarityModification = 1,
    NoUpgradeRoll = 2,
    NoHookUpgrades = 4,
    NoModifyHooks = 8,
    NoCardPoolModifications = 0x10,
    NoCardModelModifications = 0x20,
    ForceRarityOddsChange = 0x40,
    IsCardReward = 0x80,
    IsFromCombat = 0x100,
    NoUpgrades = 6,
    NoModifications = -1
}

public sealed partial class CardCreationOptions
{
    private readonly List<CardPool> _cardPools = new();

    public CardCreationOptions(IEnumerable<CardPool> cardPools, CardRarityOddsType rarityOdds, Func<CardDef, bool>? cardPoolFilter = null)
        : this(cardPools, CardCreationSource.None, rarityOdds, cardPoolFilter)
    {
    }

    public CardCreationOptions(IEnumerable<CardPool> cardPools, CardCreationSource source, CardRarityOddsType rarityOdds, Func<CardDef, bool>? cardPoolFilter = null)
    {
        ArgumentNullException.ThrowIfNull(cardPools);
        _cardPools.AddRange(cardPools);
        Source = source;
        RarityOdds = rarityOdds;
        CardPoolFilter = cardPoolFilter;
    }

    public IReadOnlyList<CardPool> CardPools => _cardPools;

    public CardRarityOddsType RarityOdds { get; private set; }

    public Func<CardDef, bool>? CardPoolFilter { get; private set; }

    public CardCreationSource Source { get; private set; }

    public CardCreationFlags Flags { get; private set; }

    public Rng? RngOverride { get; private set; }

    public static CardCreationOptions ForRoom(RunState rs, PlayerState player, RoomType roomType)
    {
        ArgumentNullException.ThrowIfNull(player);
        CardCreationSource source;
        switch (roomType)
        {
            case RoomType.Monster:
            case RoomType.Elite:
            case RoomType.Boss:
                source = CardCreationSource.Encounter;
                break;
            case RoomType.Shop:
                source = CardCreationSource.Shop;
                break;
            case RoomType.Event:
                throw new InvalidOperationException("ForRoom should not be used in event rooms");
            default:
                source = CardCreationSource.Other;
                break;
        }
        return new CardCreationOptions(new[] { Cards.CardPools.ForCharacter(player.CharacterId) }, source, roomType switch
        {
            RoomType.Monster => CardRarityOddsType.RegularEncounter,
            RoomType.Elite => CardRarityOddsType.EliteEncounter,
            RoomType.Boss => CardRarityOddsType.BossEncounter,
            RoomType.Shop => CardRarityOddsType.Shop,
            _ => CardRarityOddsType.RegularEncounter,
        });
    }

    public static CardCreationOptions ForNonCombatWithDefaultOdds(IEnumerable<CardPool> cardPools, Func<CardDef, bool>? cardPoolFilter = null)
        => new CardCreationOptions(cardPools, CardCreationSource.Other, CardRarityOddsType.RegularEncounter, cardPoolFilter).WithFlags(CardCreationFlags.NoUpgradeRoll);

    public static CardCreationOptions ForNonCombatWithUniformOdds(IEnumerable<CardPool> cardPools, Func<CardDef, bool>? cardPoolFilter = null)
        => new CardCreationOptions(cardPools, CardCreationSource.Other, CardRarityOddsType.Uniform, cardPoolFilter).WithFlags(CardCreationFlags.NoUpgradeRoll);

    public IEnumerable<CardDef> GetPossibleCards(RunState rs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        var constraint = Cards.CardPools.MultiplayerConstraintFor(rs);
        var content = rs.Config?.Content;
        var filter = CardPoolFilter;
        return CardPools.SelectMany(p => p.GetUnlockedCards(rs.Unlocks, constraint))
            .Where(c => (filter == null || filter(c)) && (content == null || content.AllowsCard(c.Id)));
    }

    public CardCreationOptions WithCardPools(IEnumerable<CardPool> pools)
    {
        ArgumentNullException.ThrowIfNull(pools);
        var val = pools.ToList();
        _cardPools.Clear();
        _cardPools.AddRange(val);
        return this;
    }

    public CardCreationOptions WithFilter(Func<CardDef, bool> filter)
    {
        CardPoolFilter = filter;
        return this;
    }

    public CardCreationOptions WithFlags(CardCreationFlags flag)
    {
        Flags |= flag;
        return this;
    }

    public CardCreationOptions WithRarityOdds(CardRarityOddsType rarityOdds)
    {
        RarityOdds = rarityOdds;
        return this;
    }

    public CardCreationOptions WithRngOverride(Rng rng)
    {
        RngOverride = rng;
        return this;
    }

    public CardRarity? TryGetSingleRarityInPool()
    {
        var val = CardPools.SelectMany(c => c.AllCards).ToList();
        var filter = CardPoolFilter;
        if (filter != null)
        {
            val = val.Where(filter).ToList();
        }
        var first = val.FirstOrDefault();
        if (first != null && val.All(c => c.Rarity == first.Rarity))
        {
            return first.Rarity;
        }
        return null;
    }

    public CardCreationOptions CloneFor(PlayerState sourceOwner, PlayerState targetOwner)
    {
        var clone = new CardCreationOptions(_cardPools, Source, RarityOdds, CardPoolFilter)
        {
            Flags = Flags,
            RngOverride = RemapRng(RngOverride, sourceOwner, targetOwner)
        };
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteCount(_cardPools.Count);
        for (int i = 0; i < _cardPools.Count; i++)
        {
            w.WriteString(_cardPools[i].Id);
        }
        w.WriteByte((byte)Source);
        w.WriteByte((byte)RarityOdds);
        w.WriteInt32((int)Flags);
        w.WriteBool(CardPoolFilter != null);
        w.WriteBool(RngOverride != null);
        RngOverride?.WriteCanonical(w);
    }

    internal static Rng? RemapRng(Rng? rng, PlayerState sourceOwner, PlayerState targetOwner)
    {
        if (rng == null)
        {
            return null;
        }
        var source = sourceOwner.PlayerRng;
        var target = targetOwner.PlayerRng;
        if (source != null && target != null)
        {
            foreach (PlayerRngType type in Enum.GetValues<PlayerRngType>())
            {
                if (ReferenceEquals(source.Get(type), rng))
                {
                    return target.Get(type);
                }
            }
        }
        return rng.DeepClone();
    }
}
