namespace Sts2Sim.Core.Powers;

public sealed class RampartPower : PowerModel
{
    public const string PowerId = "RAMPART_POWER";

    public const string TurretOperatorId = "TURRET_OPERATOR";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side != CombatSide.Player || cs.PlayersTakingExtraTurn.Count > 0)
        {
            return;
        }
        var monsters = cs.Monsters.ToArray();
        for (int i = 0; i < monsters.Length; i++)
        {
            if (monsters[i].MonsterId == TurretOperatorId)
            {
                CreatureCmd.GainBlock(cs, monsters[i], Amount, ValueProp.Unpowered, null);
            }
        }
    }
}
