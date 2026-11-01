namespace Sts2Sim.Core;

public enum ActionKind : byte
{
    PlayCard,
    EndTurn,
    UsePotion,
    DiscardPotion,
    ChoosePick,
    ChooseConfirm,
    ChooseCancel
}

public readonly record struct GameAction(ActionKind Kind, int CardIndex, int TargetIndex)
{
    public const int NoIndex = -1;

    public static GameAction EndTurn => new(ActionKind.EndTurn, NoIndex, NoIndex);

    public static GameAction Confirm => new(ActionKind.ChooseConfirm, NoIndex, NoIndex);

    public static GameAction Cancel => new(ActionKind.ChooseCancel, NoIndex, NoIndex);

    public static GameAction Play(int cardIndex, int targetIndex = NoIndex) => new(ActionKind.PlayCard, cardIndex, targetIndex);

    public static GameAction UsePotion(int slot, int target = NoIndex) => new(ActionKind.UsePotion, slot, target);

    public static GameAction DiscardPotion(int slot) => new(ActionKind.DiscardPotion, slot, NoIndex);

    public static GameAction Pick(int option) => new(ActionKind.ChoosePick, option, NoIndex);

    public override string ToString() => Kind switch
    {
        ActionKind.PlayCard => TargetIndex == NoIndex ? $"Play({CardIndex})" : $"Play({CardIndex}->{TargetIndex})",
        ActionKind.EndTurn => "EndTurn",
        ActionKind.UsePotion => TargetIndex == NoIndex ? $"UsePotion({CardIndex})" : $"UsePotion({CardIndex}->{TargetIndex})",
        ActionKind.DiscardPotion => $"DiscardPotion({CardIndex})",
        ActionKind.ChoosePick => $"Pick({CardIndex})",
        ActionKind.ChooseConfirm => "Confirm",
        ActionKind.ChooseCancel => "Cancel",
        _ => $"{Kind}({CardIndex}, {TargetIndex})"
    };
}
