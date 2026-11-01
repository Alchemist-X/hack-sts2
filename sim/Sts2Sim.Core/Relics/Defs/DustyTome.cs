namespace Sts2Sim.Core.Relics;

public sealed class DustyTome : RelicModel, IEventRelicSetup
{
    public const string RelicId = "DUSTY_TOME";

    public const string AncientCardKey = "AncientCard";

    public const string NotSetUpSite = "DustyTome.AfterObtained: AncientCard not set up (the game dereferences null here)";

    private string? _ancientCard;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public string? AncientCard
    {
        get => _ancientCard;
        set
        {
            _ancientCard = value;
            if (_ancientCard != null)
            {
                ((StringVar)DynamicVars[AncientCardKey]).StringValue = _ancientCard;
            }
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StringVar(AncientCardKey)
    };

    public bool SetupForPlayer(RunState rs, PlayerState player)
    {
        var items = CardPools.ForCharacter(player.CharacterId).GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs))
            .Where(c => c.Rarity == CardRarity.Ancient && !ArchaicTooth.TranscendenceCards.Contains(c.Id));
        var rng = player.PlayerRng ?? throw new InvalidOperationException("DustyTome.SetupForPlayer needs the player's rng set.");
        AncientCard = (rng.Rewards.NextItem(items) ?? throw new InvalidOperationException("DustyTome.SetupForPlayer: no ancient card in the character pool.")).Id;
        return true;
    }

    public override void AfterObtained(RunState rs)
    {
        if (AncientCard == null)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, NotSetUpSite);
            return;
        }
        var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(AncientCard), Owner);
        CardCmd.Upgrade(rs.Combat, card);
        CardPileCmd.Add(rs, card, PileType.Deck);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteString(_ancientCard ?? string.Empty);
    }
}
