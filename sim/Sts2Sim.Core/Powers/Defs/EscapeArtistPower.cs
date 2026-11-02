namespace Sts2Sim.Core.Powers;

public sealed class EscapeArtistPower : PowerModel
{
    public const string PowerId = "ESCAPE_ARTIST_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Amount > 1)
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
