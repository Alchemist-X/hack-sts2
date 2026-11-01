namespace Sts2Sim.Core.Run;

public sealed partial class CardReward : Reward
{
    public CardReward(PlayerState player, CardCreationOptions options, int optionCount) : base(player)
    {
        Options = options;
        OptionCount = optionCount;
    }

    public override RewardType RewardType => RewardType.Card;

    public CardCreationOptions Options { get; }

    public int OptionCount { get; }

    public IReadOnlyList<CardCreationResult> Cards => throw new NotImplementedException("W2 R3: CardReward.Cards");

    public IReadOnlyList<CardRewardAlternative> Alternatives => throw new NotImplementedException("W2 R3: CardReward.Alternatives");

    public bool CanReroll => throw new NotImplementedException("W2 R3: CardReward.CanReroll");

    public bool PickCard(RunState rs, int index) => throw new NotImplementedException("W2 R3: CardReward.PickCard");

    public bool ChooseAlternative(RunState rs, int index) => throw new NotImplementedException("W2 R3: CardReward.ChooseAlternative");
}
