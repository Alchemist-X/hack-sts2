namespace Sts2Sim.Core.Monsters;

public sealed class FakeMerchantEventEncounter : EncounterDef
{
    public const string EncounterIdEntry = "FAKE_MERCHANT_EVENT_ENCOUNTER";
    public const string MerchantSlot = "merchant";
    public const int GoldReward = 300;

    private static readonly IReadOnlyList<string> SlotList = Array.AsReadOnly(new[] { MerchantSlot });

    public FakeMerchantEventEncounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotList;

    public override int MinGoldReward(int ascension) => GoldReward;

    public override int MaxGoldReward(int ascension) => GoldReward;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[] { new MonsterSpawn(MonsterRegistry.Get("FAKE_MERCHANT_MONSTER"), MerchantSlot) };
    }
}
