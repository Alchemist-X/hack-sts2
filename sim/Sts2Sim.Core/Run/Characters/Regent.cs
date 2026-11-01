namespace Sts2Sim.Core.Run;

public sealed class Regent : CharacterDef
{
    public const string CharacterId = "REGENT";

    public const int StartingHpValue = 75;

    public const int StartingGoldValue = 99;

    public const int ArchitectAttackVfxEntries = 5;

    private static readonly IReadOnlyList<string> Deck = Array.AsReadOnly(new[]
    {
        "STRIKE_REGENT",
        "STRIKE_REGENT",
        "STRIKE_REGENT",
        "STRIKE_REGENT",
        "DEFEND_REGENT",
        "DEFEND_REGENT",
        "DEFEND_REGENT",
        "DEFEND_REGENT",
        "FALLING_STAR",
        "VENERATE"
    });

    private static readonly IReadOnlyList<string> Relics = Array.AsReadOnly(new[] { "DIVINE_RIGHT" });

    public override string Id => CharacterId;

    public override string? UnlocksAfterRunAs => "SILENT";

    public override int StartingHp => StartingHpValue;

    public override int StartingGold => StartingGoldValue;

    public override string CardPoolId => "REGENT";

    public override string RelicPoolId => RelicPools.RegentRelicPool;

    public override string PotionPoolId => PotionPools.RegentPotionPool;

    public override IReadOnlyList<string> StartingDeck => Deck;

    public override IReadOnlyList<string> StartingRelics => Relics;

    public override int ArchitectAttackVfxCount => ArchitectAttackVfxEntries;
}
