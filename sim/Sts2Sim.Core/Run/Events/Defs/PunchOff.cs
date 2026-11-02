namespace Sts2Sim.Core.Run;

public sealed class PunchOff : EventModel
{
    public const string EventId = "PUNCH_OFF";

    public const string InjuryId = "INJURY";

    public const int MinGold = 91;

    public const int MaxGoldExclusive = 99;

    public const int MinTotalFloor = 6;

    public override string Id => EventId;

    public override EventLayoutType LayoutType => EventLayoutType.Combat;

    public override EncounterDef? CanonicalEncounter => EncounterRegistry.Get(PunchOffEventEncounter.EncounterIdEntry);

    public override bool IsShared => true;

    public int Gold { get; private set; }

    public override bool IsAllowed(RunState rs) => rs.TotalFloor >= MinTotalFloor;

    public override void CalculateVars(RunState rs)
    {
        Gold = Rng!.NextInt(MinGold, MaxGoldExclusive);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("NAB"), Nab),
        new EventOption(InitialOptionKey("I_CAN_TAKE_THEM"), TakeThem)
    };

    private void Nab(RunState rs)
    {
        var owner = Owner!;
        CardPileCmd.AddCurseToDeck(rs, InjuryId, owner);
        RewardsCmd.OfferCustom(rs, owner, new List<Reward> { new RelicReward(owner) });
        SetEventFinished(Id + ".pages.NAB.description");
    }

    private void TakeThem(RunState rs)
    {
        SetEventState(Id + ".pages.I_CAN_TAKE_THEM.description", new[]
        {
            new EventOption(OptionKey("I_CAN_TAKE_THEM", "FIGHT"), Fight)
        });
    }

    private void Fight(RunState rs)
    {
        var owner = Owner!;
        var extraRewards = new List<Reward>
        {
            new RelicReward(owner),
            new PotionReward(owner)
        };
        EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(PunchOffEventEncounter.EncounterIdEntry), extraRewards, shouldResumeAfterCombat: false);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(Gold);
    }
}
