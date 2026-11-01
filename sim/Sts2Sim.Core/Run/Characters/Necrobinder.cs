namespace Sts2Sim.Core.Run;

public sealed class Necrobinder : CharacterDef
{
    public const string CharacterId = "NECROBINDER";

    public const int StartingHpValue = 66;

    public const int StartingGoldValue = 99;

    public const int ArchitectAttackVfxEntries = 4;

    private static readonly IReadOnlyList<string> Deck = Array.AsReadOnly(new[]
    {
        "STRIKE_NECROBINDER",
        "STRIKE_NECROBINDER",
        "STRIKE_NECROBINDER",
        "STRIKE_NECROBINDER",
        "DEFEND_NECROBINDER",
        "DEFEND_NECROBINDER",
        "DEFEND_NECROBINDER",
        "DEFEND_NECROBINDER",
        "BODYGUARD",
        "UNLEASH"
    });

    private static readonly IReadOnlyList<string> Relics = Array.AsReadOnly(new[] { "BOUND_PHYLACTERY" });

    public override string Id => CharacterId;

    public override string? UnlocksAfterRunAs => "REGENT";

    public override int StartingHp => StartingHpValue;

    public override int StartingGold => StartingGoldValue;

    public override string CardPoolId => "NECROBINDER";

    public override string RelicPoolId => RelicPools.NecrobinderRelicPool;

    public override string PotionPoolId => PotionPools.NecrobinderPotionPool;

    public override IReadOnlyList<string> StartingDeck => Deck;

    public override IReadOnlyList<string> StartingRelics => Relics;

    public override int ArchitectAttackVfxCount => ArchitectAttackVfxEntries;
}
