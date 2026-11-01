namespace Sts2Sim.Core.Orbs;

public sealed class PlasmaOrb : OrbModel
{
    public const string OrbIdEntry = "PLASMA_ORB";
    public const decimal PassiveValue = 1m;
    public const decimal EvokeValue = 2m;

    public override string Id => OrbIdEntry;

    public override decimal PassiveVal(CombatState cs) => PassiveValue;

    public override decimal EvokeVal(CombatState cs) => EvokeValue;

    public override void AfterTurnStartOrbTrigger(CombatState cs) => TriggerPassive(cs, null);

    public override void Passive(CombatState cs, CreatureState? target)
    {
        if (target != null)
        {
            throw new InvalidOperationException("Plasma orbs cannot target creatures.");
        }
        PlayerCmd.GainEnergy(cs, PassiveVal(cs), Owner);
    }

    public override IEnumerable<CreatureState> Evoke(CombatState cs)
    {
        PlayerCmd.GainEnergy(cs, EvokeVal(cs), Owner);
        return new CreatureState[] { Owner };
    }
}
