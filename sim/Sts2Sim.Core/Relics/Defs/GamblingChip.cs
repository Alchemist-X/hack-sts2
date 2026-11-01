namespace Sts2Sim.Core.Relics;

public sealed class GamblingChip : RelicModel
{
    public const string RelicId = "GAMBLING_CHIP";

    public const string SiteMethod = "AfterPlayerTurnStart";

    public const int MaxSelect = 999999999;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        var prefs = new CardSelectorPrefs(RelicId + ".selectionScreenPrompt", 0, MaxSelect);
        var result = CardSelectCmd.FromHandForDiscard(cs, Owner, prefs, null, ChoiceSource.For(RelicId, ChoiceSource.NoInstance, SiteMethod));
        var cards = result.ToList();
        if (cards.Count != 0)
        {
            PendingKernelCalls.CardCmdDiscardAndDraw(cs, cards, cards.Count);
        }
    }
}
