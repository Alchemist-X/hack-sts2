namespace Sts2Sim.Core.Powers;

public sealed class PossessSpeedPower : PowerModel
{
    public const string PowerId = "POSSESS_SPEED_POWER";

    private List<(int CreatureId, decimal Amount)> _stolenDexterity = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public IReadOnlyList<(int CreatureId, decimal Amount)> StolenDexterity => _stolenDexterity;

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
        if (power is not DexterityPower)
        {
            return;
        }
        if (amount >= 0m)
        {
            return;
        }
        int id = power.Owner.CombatId;
        int index = _stolenDexterity.FindIndex(e => e.CreatureId == id);
        if (index < 0)
        {
            _stolenDexterity.Add((id, 0m));
            index = _stolenDexterity.Count - 1;
        }
        _stolenDexterity[index] = (id, _stolenDexterity[index].Amount + amount);
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner))
        {
            return;
        }
        var cs = rs.Combat!;
        var stolen = _stolenDexterity.ToArray();
        for (int i = 0; i < stolen.Length; i++)
        {
            var target = CombatStatus.FindCreature(cs, stolen[i].CreatureId) ?? throw new InvalidOperationException($"{PowerId}: creature {stolen[i].CreatureId} is not in combat");
            PowerCmd.Apply<DexterityPower>(cs, target, -stolen[i].Amount, null, null);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _stolenDexterity = new List<(int, decimal)>(((PossessSpeedPower)source)._stolenDexterity);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteCount(_stolenDexterity.Count);
        for (int i = 0; i < _stolenDexterity.Count; i++)
        {
            w.WriteInt32(_stolenDexterity[i].CreatureId);
            w.WriteDecimal(_stolenDexterity[i].Amount);
        }
    }
}
