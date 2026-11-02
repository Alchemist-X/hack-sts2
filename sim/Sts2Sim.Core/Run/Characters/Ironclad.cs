namespace Sts2Sim.Core.Run;

public sealed class Ironclad : CharacterDef
{
    public const string CharacterId = "IRONCLAD";

    public const int StartingHpValue = 80;

    public const int StartingGoldValue = 99;

    public const int ArchitectAttackVfxEntries = 5;

    private static readonly IReadOnlyList<string> Deck = Array.AsReadOnly(new[]
    {
        "STRIKE_IRONCLAD",
        "STRIKE_IRONCLAD",
        "STRIKE_IRONCLAD",
        "STRIKE_IRONCLAD",
        "STRIKE_IRONCLAD",
        "DEFEND_IRONCLAD",
        "DEFEND_IRONCLAD",
        "DEFEND_IRONCLAD",
        "DEFEND_IRONCLAD",
        "BASH"
    });

    private static readonly IReadOnlyList<string> Relics = Array.AsReadOnly(new[] { "BURNING_BLOOD" });

    public override string Id => CharacterId;

    public override string? UnlocksAfterRunAs => null;

    public override int StartingHp => StartingHpValue;

    public override int StartingGold => StartingGoldValue;

    public override string CardPoolId => "IRONCLAD";

    public override string RelicPoolId => RelicPools.IroncladRelicPool;

    public override string PotionPoolId => PotionPools.IroncladPotionPool;

    public override IReadOnlyList<string> StartingDeck => Deck;

    public override IReadOnlyList<string> StartingRelics => Relics;

    public override int ArchitectAttackVfxCount => ArchitectAttackVfxEntries;
}
