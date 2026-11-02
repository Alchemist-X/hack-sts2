namespace Sts2Sim.Core.Relics;

public sealed class PhilosophersStone : RelicModel
{
    public const string RelicId = "PHILOSOPHERS_STONE";

    public const string StrengthKey = "StrengthPower";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(1m),
        new EnergyVar(1)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }

    public override void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
        if (creature.Side == Owner.Side)
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, creature, DynamicVars[StrengthKey].BaseValue, null, null);
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".AfterRoomEntered: combat room without an attached combat");
        var targets = cs.GetOpponentsOf(Owner).Where(c => c.IsAlive).ToList();
        PowerCmd.Apply<StrengthPower>(cs, targets, DynamicVars[StrengthKey].BaseValue, null, null);
    }
}
