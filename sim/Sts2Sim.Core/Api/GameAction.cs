namespace Sts2Sim.Core;

public enum ActionKind : byte
{
    PlayCard,
    EndTurn
}

public readonly record struct GameAction(ActionKind Kind, int CardIndex, int TargetIndex)
{
    public const int NoIndex = -1;

    public static GameAction EndTurn => new(ActionKind.EndTurn, NoIndex, NoIndex);

    public static GameAction Play(int cardIndex, int targetIndex = NoIndex) => new(ActionKind.PlayCard, cardIndex, targetIndex);

    public override string ToString() => Kind == ActionKind.EndTurn
        ? "EndTurn"
        : TargetIndex == NoIndex ? $"Play({CardIndex})" : $"Play({CardIndex}->{TargetIndex})";
}
