namespace Sts2Sim.Core.Powers;

public sealed class ShriekPower : PowerModel
{
    public const string PowerId = "SHRIEK_POWER";

    public const string TerrorEelId = "TERROR_EEL";

    public const string TerrorStateId = "TERROR_MOVE";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool AllowNegative => true;

    public override bool ShouldScaleInMultiplayer => true;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || target.CurrentHp > Amount)
        {
            return;
        }
        var cs = rs.Combat!;
        var eel = MonsterPowerCoupling.Require(Owner, TerrorEelId, PowerId);
        CreatureCmd.Stun(cs, eel, TerrorStateId);
        PowerCmd.Remove(cs, this);
    }
}
