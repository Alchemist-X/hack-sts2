namespace Sts2Sim.Core.Relics;

public sealed class PaelsWing : RelicModel
{
    public const string RelicId = "PAELS_WING";

    public const string SacrificeAlternativeKey = "SACRIFICE";

    private const string SacrificesKey = "Sacrifices";

    private int _rewardsSacrificed;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShowCounter => true;

    public override int DisplayAmount => RewardsSacrificed % DynamicVars[SacrificesKey].IntValue;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(SacrificesKey, 2m)
    };

    public int RewardsSacrificed
    {
        get => _rewardsSacrificed;
        set => _rewardsSacrificed = value;
    }

    public override bool TryModifyCardRewardAlternatives(RunState rs, PlayerState player, CardReward cardReward, List<CardRewardAlternative> alternatives)
    {
        if (!ReferenceEquals(Owner, player))
        {
            return false;
        }
        alternatives.Add(new CardRewardAlternative(SacrificeAlternativeKey, static (state, reward) => OnSacrificeFor(state, reward.Player), PostAlternateCardRewardAction.EndSelectionAndCompleteReward));
        return true;
    }

    private static void OnSacrificeFor(RunState rs, PlayerState player)
    {
        var relic = player.Relics.OfType<PaelsWing>().FirstOrDefault()
            ?? throw new InvalidOperationException(RelicId + ": sacrifice alternative selected but the player no longer holds the relic");
        relic.OnSacrifice(rs);
    }

    public void OnSacrifice(RunState rs)
    {
        RewardsSacrificed++;
        if (RewardsSacrificed % DynamicVars[SacrificesKey].IntValue == 0)
        {
            RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, Owner), Owner);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_rewardsSacrificed);
    }
}
