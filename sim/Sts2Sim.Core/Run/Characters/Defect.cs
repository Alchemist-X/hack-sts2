namespace Sts2Sim.Core.Run;

public sealed class Defect : CharacterDef
{
    public const string CharacterId = "DEFECT";

    public const int StartingHpValue = 75;

    public const int StartingGoldValue = 99;

    public const int BaseOrbSlotCountValue = 3;

    public const int ArchitectAttackVfxEntries = 5;

    private static readonly IReadOnlyList<string> Deck = Array.AsReadOnly(new[]
    {
        "STRIKE_DEFECT",
        "STRIKE_DEFECT",
        "STRIKE_DEFECT",
        "STRIKE_DEFECT",
        "DEFEND_DEFECT",
        "DEFEND_DEFECT",
        "DEFEND_DEFECT",
        "DEFEND_DEFECT",
        "ZAP",
        "DUALCAST"
    });

    private static readonly IReadOnlyList<string> Relics = Array.AsReadOnly(new[] { "CRACKED_CORE" });

    public override string Id => CharacterId;

    public override string? UnlocksAfterRunAs => "NECROBINDER";

    public override int StartingHp => StartingHpValue;

    public override int StartingGold => StartingGoldValue;

    public override int BaseOrbSlotCount => BaseOrbSlotCountValue;

    public override string CardPoolId => "DEFECT";

    public override string RelicPoolId => RelicPools.DefectRelicPool;

    public override string PotionPoolId => PotionPools.DefectPotionPool;

    public override IReadOnlyList<string> StartingDeck => Deck;

    public override IReadOnlyList<string> StartingRelics => Relics;

    public override int ArchitectAttackVfxCount => ArchitectAttackVfxEntries;
}
