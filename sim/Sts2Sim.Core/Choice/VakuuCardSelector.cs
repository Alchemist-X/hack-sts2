namespace Sts2Sim.Core.Choice;

public sealed class VakuuCardSelector : IChoiceSelector
{
    public const string Id = "VAKUU_CARD_SELECTOR";

    public string SelectorId => Id;

    public ChoiceAnswer Answer(ChoicePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        int count = Math.Min(prompt.MaxSelect, prompt.Options.Count);
        if (count <= 0)
        {
            return new ChoiceAnswer(Array.Empty<int>(), false);
        }
        var picks = new int[count];
        for (int i = 0; i < count; i++)
        {
            picks[i] = i;
        }
        return new ChoiceAnswer(picks, false);
    }
}
