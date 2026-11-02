namespace Sts2Sim.Core.Powers;

public sealed class ReaperFormPower : PowerModel
{
    public const string PowerId = "REAPER_FORM_POWER";

    public const string DoomPowerId = "DOOM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (dealer == null)
        {
            return;
        }
        if (!ReferenceEquals(dealer, Owner) && !ReferenceEquals(CombatStatus.PetOwnerOf(cs, dealer), Owner))
        {
            return;
        }
        if (!props.IsPoweredAttack() || result.TotalDamage <= 0)
        {
            return;
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), target, result.TotalDamage * Amount, Owner, null);
    }
}
