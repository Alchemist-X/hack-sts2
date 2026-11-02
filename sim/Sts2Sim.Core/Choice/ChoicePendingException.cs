namespace Sts2Sim.Core.Choice;

public sealed class ChoicePendingException : Exception
{
    public ChoicePendingException(ChoicePrompt prompt)
        : base($"Choice pending at {prompt.SiteId} ({prompt.Kind}, {prompt.Options.Count} options)")
    {
        Prompt = prompt;
    }

    public ChoicePrompt Prompt { get; }
}
