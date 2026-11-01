namespace Sts2Sim.Core;

public enum RunPhase : byte
{
    Map,
    Combat,
    Rewards,
    CardReward,
    Shop,
    RestSite,
    Treasure,
    Event,
    Prompt,
    Victory,
    Defeat
}

public enum RunActionKind : byte
{
    PlayCard,
    EndTurn,
    UsePotion,
    DiscardPotion,
    Travel,
    ClaimReward,
    ProceedRewards,
    PickRewardCard,
    RewardAlternative,
    BuyEntry,
    LeaveShop,
    ChooseRestOption,
    LeaveRestSite,
    OpenChest,
    TakeTreasureRelic,
    SkipTreasureRelic,
    ChooseEventOption,
    LeaveEvent,
    PromptSelect,
    PromptConfirm,
    PromptCancel
}

public readonly record struct RunAction(RunActionKind Kind, int A, int B)
{
    public const int NoIndex = -1;

    public static RunAction Of(RunActionKind kind, int a = NoIndex, int b = NoIndex) => new(kind, a, b);

    public static RunAction FromCombat(GameAction action) => action.Kind switch
    {
        ActionKind.PlayCard => new(RunActionKind.PlayCard, action.CardIndex, action.TargetIndex),
        ActionKind.EndTurn => new(RunActionKind.EndTurn, NoIndex, NoIndex),
        ActionKind.UsePotion => new(RunActionKind.UsePotion, action.CardIndex, action.TargetIndex),
        ActionKind.DiscardPotion => new(RunActionKind.DiscardPotion, action.CardIndex, NoIndex),
        ActionKind.ChoosePick => new(RunActionKind.PromptSelect, action.CardIndex, NoIndex),
        ActionKind.ChooseConfirm => new(RunActionKind.PromptConfirm, NoIndex, NoIndex),
        ActionKind.ChooseCancel => new(RunActionKind.PromptCancel, NoIndex, NoIndex),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown combat action kind.")
    };

    public bool TryToCombat(out GameAction action)
    {
        switch (Kind)
        {
            case RunActionKind.PlayCard:
                action = new GameAction(ActionKind.PlayCard, A, B);
                return true;
            case RunActionKind.EndTurn:
                action = GameAction.EndTurn;
                return true;
            case RunActionKind.UsePotion:
                action = new GameAction(ActionKind.UsePotion, A, B);
                return true;
            case RunActionKind.DiscardPotion:
                action = new GameAction(ActionKind.DiscardPotion, A, GameAction.NoIndex);
                return true;
            case RunActionKind.PromptSelect:
                action = new GameAction(ActionKind.ChoosePick, A, GameAction.NoIndex);
                return true;
            case RunActionKind.PromptConfirm:
                action = GameAction.Confirm;
                return true;
            case RunActionKind.PromptCancel:
                action = GameAction.Cancel;
                return true;
            default:
                action = default;
                return false;
        }
    }

    public override string ToString() => (A, B) switch
    {
        (NoIndex, NoIndex) => Kind.ToString(),
        (_, NoIndex) => $"{Kind}({A})",
        _ => $"{Kind}({A}, {B})"
    };
}
