namespace Sts2Sim.Core.Powers;

public sealed class PlowPower : PowerModel
{
    public const string PowerId = "PLOW_POWER";

    public const string CeremonialBeastId = "CEREMONIAL_BEAST";

    public const string BeastCryStateId = "BEAST_CRY_MOVE";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || target.CurrentHp > Amount)
        {
            return;
        }
        var cs = rs.Combat!;
        var temporary = new List<TemporaryStrengthPower>();
        var powers = Owner.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            if (powers[i] is TemporaryStrengthPower t)
            {
                temporary.Add(t);
            }
        }
        for (int i = 0; i < temporary.Count; i++)
        {
            PowerCmd.Remove(cs, temporary[i]);
        }
        PowerCmd.Remove<StrengthPower>(cs, Owner);
        var monster = (MonsterState)Owner;
        if (MonsterPowerCoupling.Is(monster, CeremonialBeastId, out var beast))
        {
            MonsterPowerCoupling.Call(cs, beast, "SetStunned", PowerId);
            CreatureCmd.StunWithMove(cs, beast, MonsterPowerCoupling.Move(beast, "StunnedMove", PowerId), BeastCryStateId);
        }
        else
        {
            CreatureCmd.Stun(cs, monster);
        }
        PowerCmd.Remove(cs, this);
    }
}
