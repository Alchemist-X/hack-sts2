using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Guards : CardDef
{
    public const string CardId = "GUARDS";
    public const int Cost = 2;
    public const int MinSelect = 0;
    public const int MaxSelect = 999999999;
    public const string MinionSacrificeId = "MINION_SACRIFICE";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Guards()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, MinSelect, MaxSelect);
        var selection = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToList();
        for (int i = 0; i < selection.Count; i++)
        {
            var minion = cs.CreateCard(MinionSacrificeId, owner);
            if (card.IsUpgraded)
            {
                CardCmd.Upgrade(cs, minion);
            }
            CardCmd.Transform(cs, selection[i], minion);
        }
    }
}
