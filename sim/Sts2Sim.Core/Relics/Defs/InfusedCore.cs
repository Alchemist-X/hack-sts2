namespace Sts2Sim.Core.Relics;

public sealed class InfusedCore : RelicModel
{
    public const string RelicId = "INFUSED_CORE";

    private const string LightningKey = "Lightning";

    private const string ExtraDamageKey = "ExtraDamage";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(LightningKey, 3m),
        new DynamicVar(ExtraDamageKey, 1m)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)) || Owner.TurnNumber > 1)
        {
            return;
        }
        for (int i = 0; i < DynamicVars[LightningKey].BaseValue; i++)
        {
            OrbCmd.Channel<LightningOrb>(cs, Owner);
        }
    }

    public override decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal value)
    {
        if (!ReferenceEquals(orb.Owner, Owner))
        {
            return value;
        }
        if (orb is not LightningOrb)
        {
            return value;
        }
        return value + DynamicVars[ExtraDamageKey].BaseValue;
    }
}
