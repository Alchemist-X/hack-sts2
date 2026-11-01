namespace Sts2Sim.Core.Monsters;

public abstract class BattlewornDummyEventEncounter : EncounterDef
{
    protected const string RanOutOfTimeKey = "RanOutOfTime";

    protected BattlewornDummyEventEncounter(string id)
        : base(id)
    {
    }

    public override EncounterExtraState CreateExtraState() => new Extra();

    public static bool IsBattlewornDummyEncounter(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        return cs.EncounterExtra is Extra;
    }

    public static bool GetRanOutOfTime(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        return cs.GetEncounterExtra<Extra>()?.RanOutOfTime ?? false;
    }

    public static void SetRanOutOfTime(CombatState cs, bool value)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var extra = cs.GetEncounterExtra<Extra>()
            ?? throw new InvalidOperationException($"BattlewornDummyEventEncounter.RanOutOfTime: combat {cs.EncounterId} carries no battleworn-dummy encounter state.");
        extra.RanOutOfTime = value;
    }

    public sealed class Extra : EncounterExtraState
    {
        public bool RanOutOfTime { get; set; }

        public override EncounterExtraState DeepClone() => new Extra { RanOutOfTime = RanOutOfTime };

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(MonsterCanonicalTags.BattlewornDummyEventEncounterExtra);
            w.WriteBool(RanOutOfTime);
        }
    }
}
