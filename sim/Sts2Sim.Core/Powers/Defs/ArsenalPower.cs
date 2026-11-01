namespace Sts2Sim.Core.Powers;

public sealed class ArsenalPower : PowerModel
{
    public const string PowerId = "ARSENAL_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        if (creator != null && ReferenceEquals(creator, Owner))
        {
            using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
            PowerCmd.Apply<StrengthPower>(cs, Owner, Amount, Owner, null);
        }
    }
}
