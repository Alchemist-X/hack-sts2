namespace Sts2Sim.Core.Run;

public sealed class DenseVegetation : EventModel
{
    public const string EventId = "DENSE_VEGETATION";

    public const decimal HpLoss = 8m;

    public const int MinGold = 61;

    public const int MaxGoldExclusive = 100;

    public override string Id => EventId;

    public override bool IsShared => true;

    public int Gold { get; private set; }

    // Option-text var {Heal} (Models.Events/DenseVegetation.cs:302 HealRestSiteOption.GetHealAmount(Owner)), read by the play server.
    public decimal HealFor(RunState rs) => Owner != null ? HealRestSiteOption.GetHealAmount(rs, Owner) : 0m;

    public override void CalculateVars(RunState rs)
    {
        Gold = Rng!.NextInt(MinGold, MaxGoldExclusive);
    }

    public override bool IsAllowed(RunState rs)
    {
        if (rs.Players.Count == 1)
        {
            return true;
        }
        return rs.Players.All(p => p.CurrentHp > HpLoss);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("TRUDGE_ON"), TrudgeOn),
        new EventOption(InitialOptionKey("REST"), Rest)
    };

    private void TrudgeOn(RunState rs)
    {
        var owner = Owner!;
        CreatureCmd.Damage(rs, owner, HpLoss, ValueProp.Unblockable | ValueProp.Unpowered);
        PlayerCmd.GainGold(rs, Gold, owner);
        SetEventFinished(Id + ".pages.TRUDGE_ON.description");
    }

    private void Rest(RunState rs)
    {
        PlayerCmd.MimicRestSiteHeal(rs, Owner!);
        SetEventState(Id + ".pages.REST.description", new[]
        {
            new EventOption(OptionKey("REST", "FIGHT"), Fight)
        });
    }

    private void Fight(RunState rs)
    {
        EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(DenseVegetationEventEncounter.EncounterIdEntry), Array.Empty<Reward>(), shouldResumeAfterCombat: false);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(Gold);
    }
}
