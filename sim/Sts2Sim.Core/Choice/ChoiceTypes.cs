namespace Sts2Sim.Core.Choice;

public enum ChoicePromptKind : byte
{
    HandSelect,
    HandUpgrade,
    PileSelect,
    ChooseCard,
    SimpleGrid,
    DeckUpgrade,
    DeckTransform,
    DeckEnchant,
    DeckRemove,
    DeckGeneric,
    RewardsGrid,
    Bundle,
    CardReward
}

public enum ChoiceResultType : byte
{
    CombatCard,
    Index
}

public enum ChoiceContextKind : byte
{
    Inline,
    TurnSetup,
    PerListener,
    Throwing
}

public readonly record struct ChoiceOption(int InstanceId, string ModelId, int UpgradeLevel);

public sealed record ChoicePrompt(
    ChoicePromptKind Kind,
    string SiteId,
    string SourceId,
    int SourceInstanceId,
    PileType? Pile,
    IReadOnlyList<ChoiceOption> Options,
    int MinSelect,
    int MaxSelect,
    bool RequireConfirm,
    bool CanSkip,
    bool Cancelable,
    bool Ordered,
    ChoiceResultType ResultType);

public readonly record struct ChoiceAnswer(IReadOnlyList<int> Picks, bool Cancelled);

public interface IChoiceAnswerer
{
    ChoiceAnswer Answer(ChoicePrompt prompt);
}

public readonly record struct ChoiceSource(string SiteId, string SourceId, int SourceInstanceId)
{
    public const int NoInstance = -1;

    public static ChoiceSource For(string modelId, int instanceId, string method) => new(modelId + ":" + method, modelId, instanceId);

    public static ChoiceSource For(CardInstance card, string method) => For(card.Id, card.InstanceId, method);
}
