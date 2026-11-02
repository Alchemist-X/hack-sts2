namespace Sts2Sim.Core.Powers;

public sealed class CreativeAiPower : PowerModel
{
    public const string PowerId = "CREATIVE_AI_POWER";

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
        for (int i = 0; i < Amount; i++)
        {
            var pool = CardPools.ForCharacter(player.CharacterId);
            var powers = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.Type == CardType.Power);
            var cardModel = CardFactory.GetDistinctForCombat(cs, player, powers, 1, rs.Rng.CombatCardGeneration).FirstOrDefault();
            if (cardModel != null)
            {
                CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, player);
            }
        }
    }
}
