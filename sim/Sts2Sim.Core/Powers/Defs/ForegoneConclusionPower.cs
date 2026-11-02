namespace Sts2Sim.Core.Powers;

public sealed class ForegoneConclusionPower : PowerModel
{
    public const string PowerId = "FOREGONE_CONCLUSION_POWER";

    public const string SelectionScreenPromptKey = PowerId + ".selectionScreenPrompt";

    public const string BeforeHandDrawSite = "BeforeHandDraw";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        CardPileCmd.ShuffleIfNecessary(cs, player);
        var prefs = new CardSelectorPrefs(SelectionScreenPromptKey, Amount);
        var cards = new List<CardInstance>(CardSelectCmd.FromCombatPile(cs, player.DrawPile, player, prefs, ChoiceSource.For(PowerId, ChoiceSource.NoInstance, BeforeHandDrawSite)));
        CardPileCmd.Add(cs, cards, PileType.Hand);
        PowerCmd.Remove(cs, this);
    }
}
