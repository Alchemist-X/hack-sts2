namespace Sts2Sim.Core.Run;

public sealed class Silent : CharacterDef
{
    public const string CharacterId = "SILENT";

    public const int StartingHpValue = 70;

    public const int StartingGoldValue = 99;

    public const int ArchitectAttackVfxEntries = 4;

    private static readonly IReadOnlyList<string> Deck = Array.AsReadOnly(new[]
    {
        "STRIKE_SILENT",
        "STRIKE_SILENT",
        "STRIKE_SILENT",
        "STRIKE_SILENT",
        "STRIKE_SILENT",
        "DEFEND_SILENT",
        "DEFEND_SILENT",
        "DEFEND_SILENT",
        "DEFEND_SILENT",
        "DEFEND_SILENT",
        "NEUTRALIZE",
        "SURVIVOR"
    });

    private static readonly IReadOnlyList<string> Relics = Array.AsReadOnly(new[] { "RING_OF_THE_SNAKE" });

    public override string Id => CharacterId;

    public override string? UnlocksAfterRunAs => null;

    public override int StartingHp => StartingHpValue;

    public override int StartingGold => StartingGoldValue;

    public override string CardPoolId => "SILENT";

    public override string RelicPoolId => RelicPools.SilentRelicPool;

    public override string PotionPoolId => PotionPools.SilentPotionPool;

    public override IReadOnlyList<string> StartingDeck => Deck;

    public override IReadOnlyList<string> StartingRelics => Relics;

    public override int ArchitectAttackVfxCount => ArchitectAttackVfxEntries;
}
