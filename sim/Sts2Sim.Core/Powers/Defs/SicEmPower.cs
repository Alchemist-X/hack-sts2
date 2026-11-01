namespace Sts2Sim.Core.Powers;

public sealed class SicEmPower : PowerModel
{
    public const string PowerId = "SIC_EM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (dealer is not MonsterState osty || !string.Equals(osty.MonsterId, CombatStatus.OstyMonsterId, StringComparison.Ordinal))
        {
            return;
        }
        var petOwner = CombatStatus.PetOwnerOf(cs, osty);
        var applier = GetApplier(cs);
        if (petOwner == null || applier == null || !ReferenceEquals(petOwner, applier) || !ReferenceEquals(target, Owner))
        {
            return;
        }
        OstyCmd.Summon(cs, petOwner, Amount, this);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
