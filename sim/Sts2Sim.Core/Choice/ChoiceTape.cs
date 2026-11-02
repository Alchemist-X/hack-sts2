namespace Sts2Sim.Core.Choice;

public sealed class ChoiceTapeAnswerer : IChoiceAnswerer
{
    private readonly IReadOnlyList<ChoiceAnswer> _answers;
    private readonly IReadOnlyList<ChoicePrompt> _prompts;
    private readonly IChoiceAnswerer? _fallback;
    private readonly Action<int>? _beforeAnswer;
    private int _cursor;

    /// <param name="beforeAnswer">Called with the entry index k after prompt k passed the shape check and before taped
    /// answer k is returned (determinized clones resample hidden state there; see <c>RunSim.CloneDeterminized</c>).</param>
    public ChoiceTapeAnswerer(IReadOnlyList<ChoiceAnswer> answers, IReadOnlyList<ChoicePrompt> prompts, IChoiceAnswerer? fallback = null, Action<int>? beforeAnswer = null)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(prompts);
        if (answers.Count != prompts.Count)
        {
            throw new ArgumentException($"The tape holds {answers.Count} answers for {prompts.Count} recorded prompts.", nameof(prompts));
        }
        _answers = answers;
        _prompts = prompts;
        _fallback = fallback;
        _beforeAnswer = beforeAnswer;
    }

    public int Count => _answers.Count;

    public int Consumed => _cursor;

    public bool IsExhausted => _cursor >= _answers.Count;

    public ChoiceAnswer Answer(ChoicePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        int k = _cursor;
        if (k < _answers.Count)
        {
            var recorded = _prompts[k];
            if (!ChoiceTape.SameShape(recorded, prompt))
            {
                throw new InvalidOperationException($"Choice tape divergence at entry {k}: the re-run asked {ChoiceTape.Describe(prompt)}, the tape recorded {ChoiceTape.Describe(recorded)}.");
            }
            _cursor = k + 1;
            _beforeAnswer?.Invoke(k);
            return _answers[k];
        }
        if (_fallback != null)
        {
            return _fallback.Answer(prompt);
        }
        throw new ChoicePendingException(prompt);
    }
}

public static class ChoiceTape
{
    public static bool SameShape(ChoicePrompt recorded, ChoicePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(prompt);
        if (recorded.Kind != prompt.Kind || !string.Equals(recorded.SiteId, prompt.SiteId, StringComparison.Ordinal))
        {
            return false;
        }
        var a = recorded.Options;
        var b = prompt.Options;
        if (a.Count != b.Count)
        {
            return false;
        }
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].InstanceId != b[i].InstanceId || !string.Equals(a[i].ModelId, b[i].ModelId, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public static string Describe(ChoicePrompt prompt)
    {
        var options = prompt.Options;
        var parts = new string[options.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = $"{options[i].ModelId}#{options[i].InstanceId}";
        }
        return $"{prompt.Kind} {prompt.SiteId} [{string.Join(", ", parts)}]";
    }

    public static ChoicePrompt Copy(ChoicePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var source = prompt.Options;
        var options = new ChoiceOption[source.Count];
        for (int i = 0; i < options.Length; i++)
        {
            options[i] = source[i];
        }
        return prompt with { Options = options };
    }

    public static ChoiceAnswer Copy(ChoiceAnswer answer)
    {
        var source = answer.Picks;
        if (source is null || source.Count == 0)
        {
            return new ChoiceAnswer(Array.Empty<int>(), answer.Cancelled);
        }
        var picks = new int[source.Count];
        for (int i = 0; i < picks.Length; i++)
        {
            picks[i] = source[i];
        }
        return new ChoiceAnswer(picks, answer.Cancelled);
    }

    public static void WritePending(CanonicalWriter w, ICanonical basis, GameAction? action, IReadOnlyList<ChoiceAnswer> tape, IReadOnlyList<int> picks)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(picks);
        w.WriteTag(ChoiceCanonicalTags.Pending);
        basis.WriteCanonical(w);
        w.WriteTag(ChoiceCanonicalTags.PendingAction);
        w.WriteBool(action.HasValue);
        if (action is { } a)
        {
            w.WriteByte((byte)a.Kind);
            w.WriteInt32(a.CardIndex);
            w.WriteInt32(a.TargetIndex);
        }
        w.WriteTag(ChoiceCanonicalTags.Tape);
        w.WriteCount(tape.Count);
        for (int i = 0; i < tape.Count; i++)
        {
            WriteAnswer(w, tape[i]);
        }
        w.WriteTag(ChoiceCanonicalTags.Picks);
        WritePicks(w, picks);
    }

    private static void WriteAnswer(CanonicalWriter w, ChoiceAnswer answer)
    {
        w.WriteTag(ChoiceCanonicalTags.Answer);
        w.WriteBool(answer.Cancelled);
        WritePicks(w, answer.Picks ?? Array.Empty<int>());
    }

    private static void WritePicks(CanonicalWriter w, IReadOnlyList<int> picks)
    {
        w.WriteCount(picks.Count);
        for (int i = 0; i < picks.Count; i++)
        {
            w.WriteInt32(picks[i]);
        }
    }
}
