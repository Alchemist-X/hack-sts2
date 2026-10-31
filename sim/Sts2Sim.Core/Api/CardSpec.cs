namespace Sts2Sim.Core;

public readonly record struct CardSpec(string Id, bool Upgraded)
{
    public const char UpgradedSuffix = '+';

    public static CardSpec Parse(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (text[^1] != UpgradedSuffix)
        {
            return new CardSpec(text, false);
        }
        var id = text[..^1];
        if (id.Length == 0 || id[^1] == UpgradedSuffix)
        {
            throw new FormatException($"'{text}' is not a card id with at most one '{UpgradedSuffix}' upgrade suffix.");
        }
        return new CardSpec(id, true);
    }

    public static CardSpec[] FromIds(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var specs = new CardSpec[ids.Count];
        for (int i = 0; i < specs.Length; i++)
        {
            specs[i] = new CardSpec(ids[i], false);
        }
        return specs;
    }

    public override string ToString() => Upgraded ? Id + UpgradedSuffix : Id;
}
