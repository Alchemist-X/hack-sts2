namespace Sts2Sim.Core.Powers;

public sealed class CallOfTheVoidPower : PowerModel
{
    public const string PowerId = "CALL_OF_THE_VOID_POWER";

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
        var pool = CardPools.ForCharacter(player.CharacterId);
        IReadOnlyList<CardDef> readOnlyList = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs))
            .Where(c => c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Ancient)
            .ToList();
        if (readOnlyList.Count <= 0)
        {
            return;
        }
        var array = new CardInstance[Math.Max(0, Amount)];
        var combatCardGeneration = rs.Rng.CombatCardGeneration;
        for (int i = 0; i < Amount; i++)
        {
            array[i] = CardFactory.GetDistinctForCombat(cs, player, readOnlyList, 1, combatCardGeneration).First();
            CardCmd.ApplyKeyword(array[i], CardKeyword.Ethereal);
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, array, PileType.Hand, player);
    }
}
