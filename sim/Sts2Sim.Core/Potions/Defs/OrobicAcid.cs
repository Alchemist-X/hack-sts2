namespace Sts2Sim.Core.Potions;

public sealed class OrobicAcid : PotionModel
{
    public const string PotionId = "OROBIC_ACID";

    public const int CardsPerType = 1;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        var rng = rs.Rng.CombatCardGeneration;
        var cards = new List<CardInstance>();
        cards.AddRange(PendingKernelCalls.CardFactoryGetDistinctForCombat(cs, player, CardType.Attack, CardsPerType, rng));
        cards.AddRange(PendingKernelCalls.CardFactoryGetDistinctForCombat(cs, player, CardType.Skill, CardsPerType, rng));
        cards.AddRange(PendingKernelCalls.CardFactoryGetDistinctForCombat(cs, player, CardType.Power, CardsPerType, rng));
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].SetToFreeThisTurn();
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, Owner);
    }
}
