namespace Sts2Sim.Core.Run;

public enum PostAlternateCardRewardAction
{
    None,
    EndSelectionAndDoNotCompleteReward,
    EndSelectionAndCompleteReward,
    DoNothing
}

public sealed partial class CardRewardAlternative
{
    public const string SkipId = "Skip";

    public const string RerollId = "REROLL";

    public const int MaxAlternatives = 2;

    public CardRewardAlternative(string optionId, PostAlternateCardRewardAction afterSelected)
        : this(optionId, null, afterSelected)
    {
    }

    public CardRewardAlternative(string optionId, Action<RunState, CardReward>? onSelect, PostAlternateCardRewardAction afterSelected)
    {
        ArgumentNullException.ThrowIfNull(optionId);
        OptionId = optionId;
        OnSelect = onSelect;
        AfterSelected = afterSelected;
    }

    public string OptionId { get; }

    public Action<RunState, CardReward>? OnSelect { get; }

    public PostAlternateCardRewardAction AfterSelected { get; }

    public bool CompletesReward => AfterSelected == PostAlternateCardRewardAction.EndSelectionAndCompleteReward;

    public bool EndsSelection => (uint)(AfterSelected - 1) <= 1u;

    public static IReadOnlyList<CardRewardAlternative> Generate(RunState rs, CardReward cardReward)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(cardReward);
        var val = new List<CardRewardAlternative>();
        if (cardReward.CanSkip)
        {
            val.Add(new CardRewardAlternative(SkipId, PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward));
        }
        if (cardReward.CanReroll)
        {
            val.Add(new CardRewardAlternative(RerollId, static (state, reward) => reward.Reroll(state), PostAlternateCardRewardAction.DoNothing));
        }
        Hook.ModifyCardRewardAlternatives(rs, cardReward.Player, cardReward, val);
        if (val.Count > MaxAlternatives)
        {
            throw new InvalidOperationException("More than 2 card reward alternatives are not supported.");
        }
        return val;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteString(OptionId);
        w.WriteByte((byte)AfterSelected);
        w.WriteBool(OnSelect != null);
    }
}
