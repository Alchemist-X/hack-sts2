namespace Sts2Sim.Core.Run;

public sealed class DrowningBeacon : EventModel
{
    public const string EventId = "DROWNING_BEACON";

    public const int HpLoss = 13;

    public const string GlowwaterPotionId = "GLOWWATER_POTION";

    public const string FresnelLensId = "FRESNEL_LENS";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("DROWNING_BEACON.pages.INITIAL.options.BOTTLE", BottleOption),
        new EventOption("DROWNING_BEACON.pages.INITIAL.options.CLIMB", ClimbOption)
    };

    private void BottleOption(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var potion = EventKit3456.CreatePotion(rs, GlowwaterPotionId);
        var rewards = new List<Reward>(1);
        if (potion != null)
        {
            rewards.Add(new PotionReward(potion, owner));
        }
        RewardsCmd.OfferCustom(rs, owner, rewards);
        SetEventFinished("DROWNING_BEACON.pages.BOTTLE.description");
    }

    private void ClimbOption(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        EventKit3456.LoseMaxHp(rs, owner, HpLoss);
        EventKit3456.ObtainRelic(rs, FresnelLensId, owner);
        SetEventFinished("DROWNING_BEACON.pages.CLIMB.description");
    }
}
