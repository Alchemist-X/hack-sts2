namespace Sts2Sim.Core.Powers;

public sealed class ShriekPower : PowerModel
{
    public const string PowerId = "SHRIEK_POWER";

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
        var monster = (MonsterState)Owner;
        if (monster.Def is not TerrorEel)
        {
            throw new InvalidCastException($"{PowerId} casts its owner to TerrorEel (Models.Powers/ShriekPower.cs:74), but the owner is {monster.MonsterId}");
        }
        CreatureCmd.Stun(cs, monster, TerrorEel.TerrorMoveId);
        PowerCmd.Remove(cs, this);
    }
}
