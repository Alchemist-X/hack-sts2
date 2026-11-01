namespace Sts2Sim.Core.Powers;

public sealed class ToolsOfTheTradePower : PowerModel
{
    public const string PowerId = "TOOLS_OF_THE_TRADE_POWER";

    public const string AfterPlayerTurnStartSite = "AfterPlayerTurnStart";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return count;
        }
        return count + Amount;
    }

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, Amount);
        var selected = new List<CardInstance>(CardSelectCmd.FromHandForDiscard(cs, player, prefs, null, ChoiceSource.For(PowerId, ChoiceSource.NoInstance, AfterPlayerTurnStartSite)));
        if (selected.Count != 0)
        {
            CardCmdForwarders.Discard(cs, selected);
        }
    }
}
