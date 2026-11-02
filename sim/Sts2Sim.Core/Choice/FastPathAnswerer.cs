namespace Sts2Sim.Core.Choice;

/// <summary>
/// The answerer behind <see cref="Run.RunSim.StepFast"/>: the clone-free step runs only when
/// <see cref="Run.RunSim.MayPrompt"/> says no prompt can occur, so any prompt that reaches it means the static
/// prompt-site table or the predicate is unsound for that state. It never answers: it throws
/// <see cref="FastPathPromptException"/> (fail-closed; never a silent pick).
/// </summary>
public sealed class FastPathAnswerer : IChoiceAnswerer
{
    public static readonly FastPathAnswerer Instance = new();

    private FastPathAnswerer()
    {
    }

    public ChoiceAnswer Answer(ChoicePrompt prompt) => throw new FastPathPromptException(prompt, null);
}

/// <summary>
/// A prompt reached a clone-free (fast-path) step that <see cref="Run.RunSim.MayPrompt"/> cleared. The run is left
/// mid-action and must be discarded. Not a NotImplemented/NotSupported exception, so Vec classifies it SIM_ERROR.
/// </summary>
public sealed class FastPathPromptException : InvalidOperationException
{
    public FastPathPromptException(ChoicePrompt prompt, RunAction? action)
        : base($"Fast-path step{(action is { } a ? " " + a : "")} reached choice prompt {prompt.SiteId} (source {prompt.SourceId}, {prompt.Kind}) "
               + "although RunSim.MayPrompt returned false: the static prompt-site table (Choice/PromptSites.g.cs, "
               + "sim/tools/inventory/gen_prompt_sites.py) or the predicate (Run/Loop/RunSim.FastPath.cs) misses this path. "
               + "The run was left mid-action and is discarded.")
    {
        Prompt = prompt;
        Action = action;
    }

    public ChoicePrompt Prompt { get; }

    public RunAction? Action { get; }
}
