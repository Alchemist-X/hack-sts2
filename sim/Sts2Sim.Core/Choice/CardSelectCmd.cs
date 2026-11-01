namespace Sts2Sim.Core.Choice;

public static class CardSelectCmd
{
    public static CardInstance? FromChooseACardScreen(CombatState cs, IReadOnlyList<CardInstance> cards, PlayerState player, bool canSkip = false, ChoiceSource source = default)
        => throw new NotImplementedException("CardSelectCmd.FromChooseACardScreen: choice agent (Commands/CardSelectCmd.cs:3169)");

    public static IEnumerable<CardInstance> FromSimpleGrid(CombatState cs, IReadOnlyList<CardInstance> cardsIn, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source = default)
        => throw new NotImplementedException("CardSelectCmd.FromSimpleGrid: choice agent (Commands/CardSelectCmd.cs:3295)");

    public static IEnumerable<CardInstance> FromCombatPile(CombatState cs, CardPile pile, PlayerState player, CardSelectorPrefs prefs, ChoiceSource source = default)
        => throw new NotImplementedException("CardSelectCmd.FromCombatPile: choice agent (Commands/CardSelectCmd.cs:3353)");

    public static IEnumerable<CardInstance> FromCombatPile(CombatState cs, CardPile pile, PlayerState player, CardSelectorPrefs prefs, Func<CardInstance, bool>? filter, ChoiceSource source = default)
        => throw new NotImplementedException("CardSelectCmd.FromCombatPile: choice agent (Commands/CardSelectCmd.cs:3361)");

    public static IEnumerable<CardInstance> FromHand(CombatState cs, PlayerState player, CardSelectorPrefs prefs, Func<CardInstance, bool>? filter, ChoiceSource source)
        => throw new NotImplementedException("CardSelectCmd.FromHand: choice agent (Commands/CardSelectCmd.cs:3668)");

    public static IEnumerable<CardInstance> FromHandForDiscard(CombatState cs, PlayerState player, CardSelectorPrefs prefs, Func<CardInstance, bool>? filter, ChoiceSource source)
        => throw new NotImplementedException("CardSelectCmd.FromHandForDiscard: choice agent (Commands/CardSelectCmd.cs:3726)");

    public static CardInstance? FromHandForUpgrade(CombatState cs, PlayerState player, ChoiceSource source)
        => throw new NotImplementedException("CardSelectCmd.FromHandForUpgrade: choice agent (Commands/CardSelectCmd.cs:3744)");
}
