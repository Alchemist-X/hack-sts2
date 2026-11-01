namespace Sts2Sim.Core.Powers;

public sealed class ThieveryPower : PowerModel
{
    public const string PowerId = "THIEVERY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public int Gold { get; private set; }

    public void Steal(CombatState cs)
    {
        var target = GetTarget(cs) as PlayerState;
        if (target == null || target.IsDead || target.Gold <= 0)
        {
            return;
        }
        int amount = Math.Min(Amount, target.Gold);
        PlayerCmd.LoseGold(cs, amount, target, GoldLossType.Stolen);
        Gold += amount;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(Gold);
    }
}
