namespace Sts2Sim.Core.Relics;

public sealed class CrackedCore : RelicModel
{
    public const string RelicId = "CRACKED_CORE";

    private const string LightningKey = "Lightning";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(LightningKey, 1m)
    };

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
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
}
