namespace Sts2Sim.Core.Monsters;

public sealed class NibbitsWeak : EncounterDef
{
    public const string EncounterIdEntry = "NIBBITS_WEAK";

    public NibbitsWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(Nibbit.MonsterIdEntry), null, static m => Nibbit.GetExtra(m).IsAlone = true);
        return new[] { spawn };
    }
}
