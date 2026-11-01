namespace Sts2Sim.Core.Powers;

public sealed class NemesisPower : PowerModel
{
    public const string PowerId = "NEMESIS_POWER";

    private bool _shouldApplyIntangible;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        bool contains = false;
        for (int i = 0; i < participants.Count; i++)
        {
            if (ReferenceEquals(participants[i], Owner))
            {
                contains = true;
                break;
            }
        }
        if (!contains)
        {
            return;
        }
        _shouldApplyIntangible = !_shouldApplyIntangible;
        if (_shouldApplyIntangible)
        {
            PowerCmd.Apply<IntangiblePower>(cs, Owner, 1, Owner, null);
        }
        else if (Owner.HasPower<IntangiblePower>())
        {
            PowerCmd.Remove(cs, Owner.GetPower<IntangiblePower>());
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_shouldApplyIntangible);
    }
}
