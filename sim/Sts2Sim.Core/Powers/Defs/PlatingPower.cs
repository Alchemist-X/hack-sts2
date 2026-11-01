namespace Sts2Sim.Core.Powers;

public sealed class PlatingPower : PowerModel
{
    public const string PowerId = "PLATING_POWER";

    public const int BaseDecrement = 1;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public int DecrementAmount { get; private set; } = BaseDecrement;

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        if (Owner.Side == CombatSide.Enemy)
        {
            DecrementAmount = cs.Run.Players.Count;
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side != CombatSide.Player)
        {
            return;
        }
        if (Owner.IsPlayer)
        {
            return;
        }
        if (cs.RoundNumber > 1)
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
    }

    public override void BeforeSideTurnEndEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        }
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Owner is PlayerState player && player.TurnNumber == 1)
        {
            return;
        }
        if (Owner.Side == CombatSide.Enemy && cs.RoundNumber == 1)
        {
            return;
        }
        if (Owner.Side == CombatSide.Enemy)
        {
            using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
            PowerCmd.ModifyAmount(cs, this, -DecrementAmount, null, null);
        }
        else
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(DecrementAmount);
    }
}
