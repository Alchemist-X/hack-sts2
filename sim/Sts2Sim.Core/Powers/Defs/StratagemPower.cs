namespace Sts2Sim.Core.Powers;

public sealed class StratagemPower : PowerModel
{
    public const string PowerId = "STRATAGEM_POWER";

    public const string SelectionScreenPromptKey = PowerId + ".selectionScreenPrompt";

    public const string AfterShuffleSite = "AfterShuffle";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
        if (!ReferenceEquals(shuffler, Owner as PlayerState))
        {
            return;
        }
        var prefs = new CardSelectorPrefs(SelectionScreenPromptKey, Amount);
        var selected = new List<CardInstance>(CardSelectCmd.FromCombatPile(cs, shuffler.DrawPile, shuffler, prefs, ChoiceSource.For(PowerId, ChoiceSource.NoInstance, AfterShuffleSite)));
        for (int i = 0; i < selected.Count; i++)
        {
            CardPileCmd.Add(cs, selected[i], PileType.Hand);
        }
    }
}
