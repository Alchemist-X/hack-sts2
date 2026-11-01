namespace Sts2Sim.Core.Relics;

public sealed class HandDrill : RelicModel
{
    public const string RelicId = "HAND_DRILL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<VulnerablePower>(2m)
    };

    public override void AfterBlockBroken(CombatState cs, CreatureState target, CreatureState? breaker)
    {
        bool byOwner = ReferenceEquals(breaker, Owner) || (breaker != null && ReferenceEquals(CombatStatus.PetOwnerOf(cs, breaker), Owner));
        if (byOwner && !target.IsPlayer)
        {
            PowerCmd.Apply<VulnerablePower>(cs, target, DynamicVars.Vulnerable.BaseValue, Owner, null);
        }
    }
}
