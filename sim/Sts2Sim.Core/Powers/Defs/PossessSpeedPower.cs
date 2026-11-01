namespace Sts2Sim.Core.Powers;

public sealed class PossessSpeedPower : PowerModel
{
    public const string PowerId = "POSSESS_SPEED_POWER";

    private List<KeyValuePair<int, decimal>> _stolenDexterity = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

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
        AddAmount(_stolenDexterity, power.Owner.CombatId, amount);
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
            var victim = cs.GetCreatureById(stolen[i].Key);
            if (victim != null)
            {
                PowerCmd.Apply<DexterityPower>(cs, victim, -stolen[i].Value, null, null);
            }
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _stolenDexterity = new List<KeyValuePair<int, decimal>>(((PossessSpeedPower)source)._stolenDexterity);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w) => WriteAmounts(w, _stolenDexterity);

    private static void AddAmount(List<KeyValuePair<int, decimal>> amounts, int creatureId, decimal amount)
    {
        for (int i = 0; i < amounts.Count; i++)
        {
            if (amounts[i].Key == creatureId)
            {
                amounts[i] = new KeyValuePair<int, decimal>(creatureId, amounts[i].Value + amount);
                return;
            }
        }
        amounts.Add(new KeyValuePair<int, decimal>(creatureId, amount));
    }

    private static void WriteAmounts(CanonicalWriter w, List<KeyValuePair<int, decimal>> amounts)
    {
        w.WriteCount(amounts.Count);
        for (int i = 0; i < amounts.Count; i++)
        {
            w.WriteInt32(amounts[i].Key);
            w.WriteDecimal(amounts[i].Value);
        }
    }
}
