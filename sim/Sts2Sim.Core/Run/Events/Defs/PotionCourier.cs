namespace Sts2Sim.Core.Run;

public sealed class PotionCourier : EventModel
{
    public const string EventId = "POTION_COURIER";

    public const int FoulPotions = 3;

    public const string FoulPotionId = "FOUL_POTION";

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState) => runState.CurrentActIndex > 0;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("POTION_COURIER.pages.INITIAL.options.GRAB_POTIONS", GrabPotions),
        new EventOption("POTION_COURIER.pages.INITIAL.options.RANSACK", Ransack)
    };

    private void GrabPotions(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var rewards = new List<Reward>();
        for (int i = 0; i < FoulPotions; i++)
        {
            var potion = EventKit3456.CreatePotion(rs, FoulPotionId);
            if (potion != null)
            {
                rewards.Add(new PotionReward(potion, owner));
            }
        }
        RewardsCmd.OfferCustom(rs, owner, rewards);
        SetEventFinished("POTION_COURIER.pages.GRAB_POTIONS.description");
    }

    private void Ransack(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var items = PotionPools.ForCharacter(owner.CharacterId).GetUnlockedPotions(rs.Unlocks)
            .Concat(PotionPools.Get(PotionPools.SharedPotionPool).GetUnlockedPotions(rs.Unlocks))
            .Where(p => p.Rarity == PotionRarity.Uncommon);
        var canonical = RewardRngs.Rewards(owner).NextItem(items);
        if (canonical != null)
        {
            var potion = EventKit3456.CreatePotion(rs, canonical.Id);
            var rewards = new List<Reward>(1);
            if (potion != null)
            {
                rewards.Add(new PotionReward(potion, owner));
            }
            RewardsCmd.OfferCustom(rs, owner, rewards);
        }
        SetEventFinished("POTION_COURIER.pages.RANSACK.description");
    }
}
