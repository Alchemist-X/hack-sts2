namespace Sts2Sim.Core.Choice;

public sealed class CardSelectorPrefs
{
    public const string LocTable = "card_selection";

    public const string TransformSelectionPrompt = "TO_TRANSFORM";

    public const string ExhaustSelectionPrompt = "TO_EXHAUST";

    public const string RemoveSelectionPrompt = "TO_REMOVE";

    public const string EnchantSelectionPrompt = "TO_ENCHANT";

    public const string DiscardSelectionPrompt = "TO_DISCARD";

    public const string UpgradeSelectionPrompt = "TO_UPGRADE";

    public CardSelectorPrefs(string promptKey, int selectCount)
        : this(promptKey, selectCount, selectCount)
    {
    }

    public CardSelectorPrefs(string promptKey, int minCount, int maxCount)
    {
        PromptKey = promptKey;
        MaxSelect = maxCount;
        MinSelect = minCount;
        RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect;
    }

    public string PromptKey { get; }

    public int MinSelect { get; }

    public int MaxSelect { get; }

    public bool RequireManualConfirmation { get; init; }

    public bool Cancelable { get; init; }

    public Comparison<CardInstance>? Comparison { get; init; }
}
