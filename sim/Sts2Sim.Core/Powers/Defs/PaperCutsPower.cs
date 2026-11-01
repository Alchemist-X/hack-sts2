namespace Sts2Sim.Core.Powers;

public sealed class PaperCutsPower : PowerModel
{
    public const string PowerId = "PAPER_CUTS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (ReferenceEquals(dealer, Owner) && target.IsPlayer && props.IsPoweredAttack() && result.UnblockedDamage > 0)
        {
            CreatureCmd.LoseMaxHp(cs, target, Amount, isFromCard: false);
        }
    }
}
