namespace Sts2Sim.Core.Powers;

public sealed class GigantificationPower : PowerModel
{
    public const string PowerId = "GIGANTIFICATION_POWER";

    public const decimal DamageMultiplier = 3m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public AttackCommand? CommandToModify { get; private set; }

    public override void BeforeAttack(CombatState cs, AttackCommand command)
    {
        if (command.ModelSource is not { } card)
        {
            return;
        }
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        if (card.Type != CardType.Attack)
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
        CommandToModify = command;
    }

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (cardSource == null)
        {
            return 1m;
        }
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(rs.Combat!, cardSource), Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (CommandToModify == null || ReferenceEquals(cardSource, CommandToModify.ModelSource))
        {
            return DamageMultiplier;
        }
        return 1m;
    }

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (ReferenceEquals(command, CommandToModify))
        {
            CommandToModify = null;
            PowerCmd.Decrement(cs, this);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(CommandToModify != null);
    }
}
