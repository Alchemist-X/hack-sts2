namespace Sts2Sim.Core.Powers;

public sealed class VigorPower : PowerModel
{
    public const string PowerId = "VIGOR_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public AttackCommand? CommandToModify { get; private set; }

    public int AmountWhenAttackStarted { get; private set; }

    public override void BeforeAttack(CombatState cs, AttackCommand command)
    {
        if (!ReferenceEquals(command.Attacker, Owner))
        {
            return;
        }
        if (!command.DamageProps.IsPoweredAttack())
        {
            return;
        }
        if (CommandToModify != null)
        {
            return;
        }
        if (command.IsFromMonster)
        {
            return;
        }
        CommandToModify = command;
        AmountWhenAttackStarted = Amount;
    }

    public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(Owner, dealer))
        {
            return 0m;
        }
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        if (CommandToModify != null && cardSource != null && !ReferenceEquals(cardSource, CommandToModify.ModelSource))
        {
            return 0m;
        }
        if (CommandToModify != null && !ReferenceEquals(CommandToModify.Attacker, dealer))
        {
            return 0m;
        }
        return Amount;
    }

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (!ReferenceEquals(command, CommandToModify))
        {
            return;
        }
        PowerCmd.ModifyAmount(cs, this, -AmountWhenAttackStarted, null, null);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(CommandToModify != null);
        w.WriteInt32(AmountWhenAttackStarted);
    }
}
