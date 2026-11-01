namespace Sts2Sim.Core.Powers;

public sealed class PossessStrengthPower : PowerModel
{
    public const string PowerId = "POSSESS_STRENGTH_POWER";

    private List<(int CreatureId, decimal Amount)> _stolenStrength = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public IReadOnlyList<(int CreatureId, decimal Amount)> StolenStrength => _stolenStrength;

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (!ReferenceEquals(applier, Owner))
        {
            return;
        }
        if (!power.Owner.IsPlayer)
        {
            return;
        }
        if (power is not StrengthPower)
        {
            return;
        }
        if (amount >= 0m)
        {
            return;
        }
        int id = power.Owner.CombatId;
        int index = _stolenStrength.FindIndex(e => e.CreatureId == id);
        if (index < 0)
        {
            _stolenStrength.Add((id, 0m));
            index = _stolenStrength.Count - 1;
        }
        _stolenStrength[index] = (id, _stolenStrength[index].Amount + amount);
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner))
        {
            return;
        }
        var cs = rs.Combat!;
        var stolen = _stolenStrength.ToArray();
        for (int i = 0; i < stolen.Length; i++)
        {
            var target = CombatStatus.FindCreature(cs, stolen[i].CreatureId) ?? throw new InvalidOperationException($"{PowerId}: creature {stolen[i].CreatureId} is not in combat");
            PowerCmd.Apply<StrengthPower>(cs, target, -stolen[i].Amount, null, null);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _stolenStrength = new List<(int, decimal)>(((PossessStrengthPower)source)._stolenStrength);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteCount(_stolenStrength.Count);
        for (int i = 0; i < _stolenStrength.Count; i++)
        {
            w.WriteInt32(_stolenStrength[i].CreatureId);
            w.WriteDecimal(_stolenStrength[i].Amount);
        }
    }
}
