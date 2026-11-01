namespace Sts2Sim.Core.Relics;

public sealed class VexingPuzzlebox : RelicModel
{
    public const string RelicId = "VEXING_PUZZLEBOX";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber != 1)
        {
            return;
        }
        var options = CardPools.ForCharacter(Owner.CharacterId).GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run));
        var card = CardFactory.GetDistinctForCombat(cs, Owner, options, 1, cs.Run.Rng.CombatCardGeneration).First();
        card.SetToFreeThisTurn();
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand, player);
    }
}
