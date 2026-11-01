namespace Sts2Sim.Core.Relics;

public sealed class SeaGlass : RelicModel
{
    public const string RelicId = "SEA_GLASS";

    public const string DefaultCharacterId = "IRONCLAD";

    private const string _characterKey = "Character";

    private string? _characterId;

    public override string Id => RelicId;

    public string? CharacterId
    {
        get => _characterId;
        set
        {
            _characterId = value;
            ((StringVar)DynamicVars[_characterKey]).StringValue = value ?? string.Empty;
        }
    }

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(15),
        new StringVar(_characterKey)
    };

    public override void AfterObtained(RunState rs)
    {
        if (CharacterId == null)
        {
            CharacterId = DefaultCharacterId;
        }
        int cardCount = DynamicVars.Cards.IntValue / 3;
        var pool = new[] { CardPools.ForCharacter(CharacterId!) };
        var options = CardCreationOptions.ForNonCombatWithUniformOdds(pool, c => c.Rarity == CardRarity.Common).WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
        var options2 = CardCreationOptions.ForNonCombatWithUniformOdds(pool, c => c.Rarity == CardRarity.Uncommon).WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
        var options3 = CardCreationOptions.ForNonCombatWithUniformOdds(pool, c => c.Rarity == CardRarity.Rare).WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
        var val = CardFactory.CreateForReward(rs, Owner, cardCount, options).ToList();
        var val2 = CardFactory.CreateForReward(rs, Owner, cardCount, options2).ToList();
        var val3 = CardFactory.CreateForReward(rs, Owner, cardCount, options3).ToList();
        var val4 = val.Concat(val2).Concat(val3).ToList();
        var prefs = new CardSelectorPrefs(RelicId + ".selectionScreenPrompt", 0, val4.Count);
        var enumerable = RelicDeckPromptsR91.FromSimpleGridForRewards(rs, val4, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardPileCmd.Add(rs, current, PileType.Deck);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteString(_characterId ?? string.Empty);
    }
}
