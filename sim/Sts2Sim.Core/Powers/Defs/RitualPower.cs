namespace Sts2Sim.Core.Powers;

public sealed class RitualPower : PowerModel
{
    public const string PowerId = "RITUAL_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public bool WasJustAppliedByEnemy { get; private set; }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        if (Owner.IsEnemy)
        {
            WasJustAppliedByEnemy = true;
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (!WasJustAppliedByEnemy)
        {
            PowerCmd.Apply<StrengthPower>(cs, Owner, Amount, Owner, null);
        }
        WasJustAppliedByEnemy = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(WasJustAppliedByEnemy);
    }
}
