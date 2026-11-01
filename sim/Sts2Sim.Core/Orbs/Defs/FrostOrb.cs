namespace Sts2Sim.Core.Orbs;

public sealed class FrostOrb : OrbModel
{
    public const string OrbIdEntry = "FROST_ORB";
    public const string HibernatePowerId = "HIBERNATE_POWER";
    public const decimal BasePassiveValue = 2m;
    public const decimal BaseEvokeValue = 5m;

    public override string Id => OrbIdEntry;

    public override decimal PassiveVal(CombatState cs) => ModifyOrbValue(cs, BasePassiveValue);

    public override decimal EvokeVal(CombatState cs) => ModifyOrbValue(cs, BaseEvokeValue);

    public override void BeforeTurnEndOrbTrigger(CombatState cs) => TriggerPassive(cs, null);

    public override void Passive(CombatState cs, CreatureState? target)
    {
        if (target != null)
        {
            throw new InvalidOperationException("Frost orbs cannot target creatures.");
        }
        var owner = Owner;
        CreatureCmd.GainBlock(cs, owner, PassiveVal(cs), ValueProp.Unpowered, null);
        if (owner.HasPower(HibernatePowerId))
        {
            var players = PlayersInCombat(cs);
            for (int i = 0; i < players.Count; i++)
            {
                if (!ReferenceEquals(players[i], owner))
                {
                    CreatureCmd.GainBlock(cs, players[i], PassiveVal(cs), ValueProp.Unpowered, null);
                }
            }
        }
    }

    public override IEnumerable<CreatureState> Evoke(CombatState cs)
    {
        var owner = Owner;
        CreatureCmd.GainBlock(cs, owner, EvokeVal(cs), ValueProp.Unpowered, null);
        if (owner.HasPower(HibernatePowerId))
        {
            var players = PlayersInCombat(cs);
            for (int i = 0; i < players.Count; i++)
            {
                if (!ReferenceEquals(players[i], owner))
                {
                    CreatureCmd.GainBlock(cs, players[i], EvokeVal(cs), ValueProp.Unpowered, null);
                }
            }
            var evoked = PlayersInCombat(cs);
            var creatures = new CreatureState[evoked.Count];
            for (int i = 0; i < creatures.Length; i++)
            {
                creatures[i] = evoked[i];
            }
            return creatures;
        }
        return new CreatureState[] { owner };
    }
}
