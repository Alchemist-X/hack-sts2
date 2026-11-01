namespace Sts2Sim.Core.Monsters;

public abstract class BattlewornDummyEventEncounter : EncounterDef
{
    protected const string RanOutOfTimeKey = "RanOutOfTime";

    protected BattlewornDummyEventEncounter(string id)
        : base(id)
    {
    }

    public static bool GetRanOutOfTime(CombatState cs)
        => throw new NotImplementedException("W3 gap: mutable encounter instance state (BattlewornDummyEventEncounter.RanOutOfTime; EncounterDef is shared and immutable)");

    public static void SetRanOutOfTime(CombatState cs, bool value)
        => throw new NotImplementedException("W3 gap: mutable encounter instance state (BattlewornDummyEventEncounter.RanOutOfTime; EncounterDef is shared and immutable)");
}
