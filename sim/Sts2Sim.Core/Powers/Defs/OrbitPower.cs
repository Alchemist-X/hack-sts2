namespace Sts2Sim.Core.Powers;

public sealed class OrbitPower : PowerModel
{
    public const string PowerId = "ORBIT_POWER";

    public const int EnergyIncrement = 4;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public int EnergySpent { get; private set; }

    public int TriggerCount { get; private set; }

    public override void AfterEnergySpent(CombatState cs, CardInstance card, int amount)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) || amount <= 0)
        {
            return;
        }
        EnergySpent += amount;
        int triggers = EnergySpent / EnergyIncrement - TriggerCount;
        if (triggers > 0)
        {
            PlayerCmd.GainEnergy(cs, Amount * triggers, OwnerPlayer());
            TriggerCount += triggers;
        }
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} gains energy for its owner's player (Models.Powers/OrbitPower.cs), but its owner is not a player");

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(EnergySpent);
        w.WriteInt32(TriggerCount);
    }
}
