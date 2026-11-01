namespace Sts2Sim.Core.Relics;

public sealed class ToastyMittens : RelicModel
{
    public const string RelicId = "TOASTY_MITTENS";

    public const string SiteMethod = "AfterPlayerTurnStart";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.StrengthName, 1m)
    };

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, 1);
        var selected = CardSelectCmd.FromHand(cs, player, prefs, null, ChoiceSource.For(RelicId, ChoiceSource.NoInstance, SiteMethod));
        foreach (var card in selected)
        {
            CardCmd.Exhaust(cs, card);
        }
        PowerCmd.Apply<StrengthPower>(cs, player, DynamicVars.Strength.BaseValue, player, null);
    }
}
