namespace Sts2Sim.Core.Monsters;

public sealed class OwlMagistrateNormal : EncounterDef
{
    public const string EncounterIdEntry = "OWL_MAGISTRATE_NORMAL";

    public OwlMagistrateNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("OWL_MAGISTRATE"))
        };
    }
}
