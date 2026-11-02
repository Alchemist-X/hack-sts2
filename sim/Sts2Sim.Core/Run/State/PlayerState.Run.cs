namespace Sts2Sim.Core.State;

public sealed partial class PlayerState
{
    public PlayerRngSet? PlayerRng { get; set; }

    public PlayerOddsSet? PlayerOdds { get; set; }

    public RelicGrabBag? RelicGrabBag { get; set; }

    public ExtraPlayerFields ExtraFields { get; private set; } = new();

    private void CopyRunFieldsFrom(PlayerState source)
    {
        var rng = source.PlayerRng?.DeepClone();
        PlayerRng = rng;
        PlayerOdds = source.PlayerOdds?.CloneFor(rng ?? throw new InvalidOperationException("Player odds need the player's rng set."));
        RelicGrabBag = source.RelicGrabBag?.DeepClone();
        ExtraFields = source.ExtraFields.DeepClone();
    }

    private void WriteRunCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.PlayerRunFields);
        var rng = PlayerRng;
        w.WriteBool(rng != null);
        rng?.WriteCanonical(w);
        var odds = PlayerOdds;
        w.WriteBool(odds != null);
        odds?.WriteCanonical(w);
        var bag = RelicGrabBag;
        w.WriteBool(bag != null);
        bag?.WriteCanonical(w);
        ExtraFields.WriteCanonical(w);
    }
}
