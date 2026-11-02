namespace Sts2Sim.Core.Powers;

public sealed class EntropyPower : PowerModel
{
    public const string PowerId = "ENTROPY_POWER";

    public const string AfterPlayerTurnStartSite = "AfterPlayerTurnStart";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, Amount);
        var selected = new List<CardInstance>(CardSelectCmd.FromHand(cs, player, prefs, null, ChoiceSource.For(PowerId, ChoiceSource.NoInstance, AfterPlayerTurnStartSite)));
        for (int i = 0; i < selected.Count; i++)
        {
            CardCmdForwarders.TransformToRandom(cs, selected[i], cs.Rng.CombatCardSelection);
        }
    }
}
