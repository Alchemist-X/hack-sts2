namespace Sts2Sim.Core.Run;

public sealed class MerchantInventory : ICanonical
{
    private static readonly CardType[] ColoredCardTypes = { CardType.Attack, CardType.Attack, CardType.Skill, CardType.Skill, CardType.Power };

    private static readonly CardRarity[] ColorlessCardRarities = { CardRarity.Uncommon, CardRarity.Rare };

    private readonly List<MerchantCardEntry> _characterCardEntries = new();

    private readonly List<MerchantCardEntry> _colorlessCardEntries = new();

    private readonly List<MerchantRelicEntry> _relicEntries = new();

    private readonly List<MerchantPotionEntry> _potionEntries = new();

    public MerchantInventory(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; private set; }

    public IReadOnlyList<MerchantCardEntry> CharacterCardEntries => _characterCardEntries;

    public IReadOnlyList<MerchantCardEntry> ColorlessCardEntries => _colorlessCardEntries;

    public IReadOnlyList<MerchantRelicEntry> RelicEntries => _relicEntries;

    public IReadOnlyList<MerchantPotionEntry> PotionEntries => _potionEntries;

    public MerchantCardRemovalEntry? CardRemovalEntry { get; private set; }

    public IEnumerable<MerchantCardEntry> CardEntries => _characterCardEntries.Concat(_colorlessCardEntries);

    public IReadOnlyList<MerchantEntry> AllEntries
    {
        get
        {
            var all = new List<MerchantEntry>(_characterCardEntries.Count + _colorlessCardEntries.Count + _relicEntries.Count + _potionEntries.Count + 1);
            all.AddRange(_characterCardEntries);
            all.AddRange(_colorlessCardEntries);
            all.AddRange(_relicEntries);
            all.AddRange(_potionEntries);
            if (CardRemovalEntry != null)
            {
                all.Add(CardRemovalEntry);
            }
            return all;
        }
    }

    public static MerchantInventory CreateForNormalMerchant(RunState rs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        var inventory = new MerchantInventory(player);
        inventory.PopulateCharacterCardEntries(rs);
        inventory.PopulateColorlessCardEntries(rs);
        inventory.PopulateRelicEntries(rs);
        inventory.PopulatePotionEntries(rs);
        inventory.CardRemovalEntry = new MerchantCardRemovalEntry(rs, player);
        return inventory;
    }

    public void AddRelicEntry(MerchantRelicEntry entry) => _relicEntries.Add(entry);

    private static PlayerRngSet RngOf(PlayerState player) => player.PlayerRng ?? throw new InvalidOperationException("The merchant needs the player's rng set.");

    private void PopulateCharacterCardEntries(RunState rs)
    {
        int onSale = RngOf(Player).Shops.NextInt(ColoredCardTypes.Length);
        var cardPool = CardPools.ForCharacter(Player.CharacterId).GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).ToList();
        for (int i = 0; i < ColoredCardTypes.Length; i++)
        {
            var entry = new MerchantCardEntry(Player, this, cardPool, ColoredCardTypes[i]);
            entry.Populate(rs);
            _characterCardEntries.Add(entry);
            if (onSale == i)
            {
                entry.SetOnSale();
            }
        }
    }

    private void PopulateColorlessCardEntries(RunState rs)
    {
        var cardPool = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).ToList();
        for (int i = 0; i < ColorlessCardRarities.Length; i++)
        {
            var entry = new MerchantCardEntry(Player, this, cardPool, ColorlessCardRarities[i]);
            entry.Populate(rs);
            _colorlessCardEntries.Add(entry);
        }
    }

    private void PopulateRelicEntries(RunState rs)
    {
        var rarities = new[]
        {
            RelicFactory.RollRarity(Player),
            RelicFactory.RollRarity(Player),
            RelicRarity.Shop,
        };
        for (int i = 0; i < rarities.Length; i++)
        {
            AddRelicEntry(new MerchantRelicEntry(rs, rarities[i], Player));
        }
    }

    private void PopulatePotionEntries(RunState rs)
    {
        var potions = PotionFactory.CreateRandomPotionsOutOfCombat(rs, Player, 3, RngOf(Player).Shops).ToList();
        for (int i = 0; i < potions.Count; i++)
        {
            _potionEntries.Add(new MerchantPotionEntry(PotionRegistry.Create(potions[i].Id), Player));
        }
    }

    public void UpdateEntries(RunState rs)
    {
        var all = AllEntries;
        for (int i = 0; i < all.Count; i++)
        {
            all[i].OnMerchantInventoryUpdated(rs);
        }
    }

    public MerchantInventory CloneFor(PlayerState player)
    {
        var clone = new MerchantInventory(player);
        for (int i = 0; i < _characterCardEntries.Count; i++)
        {
            clone._characterCardEntries.Add((MerchantCardEntry)_characterCardEntries[i].CloneFor(player, clone));
        }
        for (int i = 0; i < _colorlessCardEntries.Count; i++)
        {
            clone._colorlessCardEntries.Add((MerchantCardEntry)_colorlessCardEntries[i].CloneFor(player, clone));
        }
        for (int i = 0; i < _relicEntries.Count; i++)
        {
            clone._relicEntries.Add((MerchantRelicEntry)_relicEntries[i].CloneFor(player));
        }
        for (int i = 0; i < _potionEntries.Count; i++)
        {
            clone._potionEntries.Add((MerchantPotionEntry)_potionEntries[i].CloneFor(player));
        }
        clone.CardRemovalEntry = (MerchantCardRemovalEntry?)CardRemovalEntry?.CloneFor(player);
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Merchant);
        var all = AllEntries;
        w.WriteCount(all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            all[i].WriteCanonical(w);
        }
    }
}
