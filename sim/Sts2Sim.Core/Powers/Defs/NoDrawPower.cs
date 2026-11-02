namespace Sts2Sim.Core.Powers;

public sealed class NoDrawPower : PowerModel
{
    public const string PowerId = "NO_DRAW_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw)
    {
        if (fromHandDraw)
        {
            return true;
        }
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return false;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
