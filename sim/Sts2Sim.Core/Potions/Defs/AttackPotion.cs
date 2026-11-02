namespace Sts2Sim.Core.Potions;

public sealed class AttackPotion : PotionModel
{
    public const string PotionId = "ATTACK_POTION";

    public const int CardsOffered = 3;

    public const string SiteMethod = "OnUse";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        var cards = PendingKernelCalls.CardFactoryGetDistinctForCombat(cs, player, CardType.Attack, CardsOffered, rs.Rng.CombatCardGeneration).ToList();
        var cardModel = CardSelectCmd.FromChooseACardScreen(cs, cards, player, canSkip: true, ChoiceSource.For(PotionId, ChoiceSource.NoInstance, SiteMethod));
        if (cardModel != null)
        {
            cardModel.SetToFreeThisTurn();
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, Owner);
        }
    }
}
