using Sts2Sim.Core.Monsters;

namespace Sts2Sim.Core.Run;

/// <summary>
/// <c>EncounterModel.SaveCustomState</c> / <c>LoadCustomState</c> (Models/EncounterModel.cs:329-336, base: empty / no-op)
/// for the sim's encounter extra states. Only BATTLEWORN_DUMMY_EVENT_* overrides it in v0.111.0
/// (Models.Encounters/BattlewornDummyEventEncounter.cs:24-32: <c>RanOutOfTime</c> as <c>bool.ToString()</c>).
/// The game loads it on every saved combat room (Rooms/CombatRoom.cs:685-686).
/// </summary>
public static class EncounterCustomState
{
    public const string RanOutOfTimeKey = "RanOutOfTime";

    public static Dictionary<string, string> Save(EncounterExtraState? extra)
    {
        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        if (extra is BattlewornDummyEventEncounter.Extra dummy)
        {
            state[RanOutOfTimeKey] = dummy.RanOutOfTime ? bool.TrueString : bool.FalseString;
        }
        return state;
    }

    public static EncounterExtraState? Load(EncounterDef encounter, IReadOnlyDictionary<string, string> state)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(state);
        var extra = encounter.CreateExtraState();
        if (extra is BattlewornDummyEventEncounter.Extra dummy)
        {
            // bool.Parse(state["RanOutOfTime"]): a missing key throws in the game too.
            dummy.RanOutOfTime = bool.Parse(state[RanOutOfTimeKey]);
        }
        return extra;
    }
}
