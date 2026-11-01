namespace Sts2Sim.Core.Powers;

public sealed class SpectrumShiftPower : PowerModel
{
    public const string PowerId = "SPECTRUM_SHIFT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        var rs = cs.Run;
        var options = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        var cards = CardFactory.GetDistinctForCombat(cs, player, options, Amount, rs.Rng.CombatCardGeneration).ToList();
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, player);
    }
}
