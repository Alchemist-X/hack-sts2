namespace Sts2Sim.Core.Run;

public sealed partial class CardRewardAlternative
{
    public const string SkipId = "SKIP";

    public const string RerollId = "REROLL";

    public CardRewardAlternative(string optionId, bool completesReward)
    {
        OptionId = optionId;
        CompletesReward = completesReward;
    }

    public string OptionId { get; }

    public bool CompletesReward { get; }

    public static IReadOnlyList<CardRewardAlternative> Generate(RunState rs, CardReward cardReward) => throw new NotImplementedException("W2 R3: CardRewardAlternative.Generate");
}
