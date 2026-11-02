namespace Sts2Sim.Core.Powers;

public sealed class ConfusedPower : PowerModel
{
    public const string PowerId = "CONFUSED_POWER";

    public const int CostRollExclusiveMax = 4;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        if (card.EnergyCost.Canonical < 0)
        {
            return;
        }
        int cost = NextEnergyCost(cs);
        card.EnergyCost.SetThisCombat(cost);
    }

    private static int NextEnergyCost(CombatState cs) => cs.Rng.CombatEnergyCosts.NextInt(CostRollExclusiveMax);
}
