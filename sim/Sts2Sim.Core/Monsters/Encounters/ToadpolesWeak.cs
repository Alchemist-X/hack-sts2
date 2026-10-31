namespace Sts2Sim.Core.Monsters;

public sealed class ToadpolesWeak : EncounterDef
{
    public const string EncounterIdEntry = "TOADPOLES_WEAK";

    public ToadpolesWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var def = MonsterRegistry.Get(Toadpole.MonsterIdEntry);
        var front = new MonsterSpawn(def, null, static m => Toadpole.GetExtra(m).IsFront = true);
        var back = new MonsterSpawn(def, null, static m => Toadpole.GetExtra(m).IsFront = false);
        return new[] { front, back };
    }
}
