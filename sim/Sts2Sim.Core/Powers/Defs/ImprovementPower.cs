namespace Sts2Sim.Core.Powers;

public sealed class ImprovementPower : PowerModel
{
    public const string PowerId = "IMPROVEMENT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        var player = (PlayerState)Owner;
        var val = player.Deck.Where(c => c.IsUpgradable).ToList();
        for (int num = 0; num < Amount; num++)
        {
            if (val.Count == 0)
            {
                break;
            }
            var cardModel = rs.Rng.CombatCardSelection.NextItem(val)!;
            val.Remove(cardModel);
            CardCmd.Upgrade(rs.Combat, cardModel);
        }
    }
}
