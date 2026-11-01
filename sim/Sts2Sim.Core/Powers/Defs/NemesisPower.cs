namespace Sts2Sim.Core.Powers;

public sealed class NemesisPower : PowerModel
{
    public const string PowerId = "NEMESIS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public bool ShouldApplyIntangible { get; private set; }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        ShouldApplyIntangible = !ShouldApplyIntangible;
        if (ShouldApplyIntangible)
        {
            PowerCmd.Apply<IntangiblePower>(cs, Owner, 1m, Owner, null);
        }
        else if (Owner.GetPower<IntangiblePower>() is { } intangible)
        {
            PowerCmd.Remove(cs, intangible);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(ShouldApplyIntangible);
    }
}
