namespace Sts2Sim.Core.Monsters;

public sealed class NibbitsWeak : EncounterDef
{
    public const string EncounterIdEntry = "NIBBITS_WEAK";

    public NibbitsWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    // Models.Encounters/NibbitsWeak.cs `Tags => [EncounterTag.Nibbit]` (E3: ActModel.AddWithoutRepeatingTags, Models/ActModel.cs:357-369)
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Nibbit });

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(Nibbit.MonsterIdEntry), null, static m => Nibbit.GetExtra(m).IsAlone = true);
        return new[] { spawn };
    }
}
