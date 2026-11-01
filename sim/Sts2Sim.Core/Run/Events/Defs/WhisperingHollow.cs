namespace Sts2Sim.Core.Run;

public sealed class WhisperingHollow : EventModel
{
    public const string EventId = "WHISPERING_HOLLOW";

    public const int BaseGold = 35;

    public const int GoldVariance = 9;

    public const decimal HpLoss = 9m;

    public const string HugSite = "WHISPERING_HOLLOW:HUG";

    public override string Id => EventId;

    public int Gold { get; private set; } = BaseGold;

    public override bool IsAllowed(RunState rs) => rs.Players.All(p => p.Gold >= BaseGold + GoldVariance);

    public override void CalculateVars(RunState rs)
    {
        Gold += Rng!.NextInt(-GoldVariance, GoldVariance + 1);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("GOLD"), GoldOption),
        new EventOption(InitialOptionKey("HUG"), Hug)
    };

    private void GoldOption(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, Gold, owner, GoldLossType.Spent);
        RewardsCmd.OfferCustom(rs, owner, new List<Reward> { new PotionReward(owner), new PotionReward(owner) });
        SetEventFinished(Id + ".pages.GOLD.description");
    }

    private void Hug(RunState rs)
    {
        var owner = Owner!;
        var picked = CardSelectCmd.FromDeckForTransformation(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), new ChoiceSource(HugSite, Id, ChoiceSource.NoInstance)).ToList();
        foreach (var card in picked)
        {
            CardCmd.TransformToRandom(rs, card, Rng!);
        }
        CreatureCmd.Damage(rs, owner, HpLoss, ValueProp.Unblockable | ValueProp.Unpowered);
        SetEventFinished(Id + ".pages.HUG.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(Gold);
    }
}
