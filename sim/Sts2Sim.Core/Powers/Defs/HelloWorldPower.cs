namespace Sts2Sim.Core.Powers;

public sealed class HelloWorldPower : PowerModel
{
    public const string PowerId = "HELLO_WORLD_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState) || AmountOnTurnStart < 1)
        {
            return;
        }
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(player.CharacterId);
        var commons = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.Rarity == CardRarity.Common);
        var distinctForCombat = CardFactory.GetDistinctForCombat(cs, player, commons, AmountOnTurnStart, rs.Rng.CombatCardGeneration);
        CardPileCmd.AddGeneratedCardsToCombat(cs, distinctForCombat, PileType.Hand, player);
    }
}
