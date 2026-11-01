namespace Sts2Sim.Core.Powers;

public sealed class RampartPower : PowerModel
{
    public const string PowerId = "RAMPART_POWER";

    public const string TurretOperatorMonsterId = "TURRET_OPERATOR";

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
        var operators = new List<MonsterState>();
        var enemies = cs.Monsters;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (string.Equals(enemies[i].MonsterId, TurretOperatorMonsterId, StringComparison.Ordinal))
            {
                operators.Add(enemies[i]);
            }
        }
        for (int i = 0; i < operators.Count; i++)
        {
            CreatureCmd.GainBlock(cs, operators[i], Amount, ValueProp.Unpowered, null);
        }
    }
}
