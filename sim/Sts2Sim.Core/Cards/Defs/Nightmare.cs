using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Nightmare : CardDef
{
    public const string CardId = "NIGHTMARE";
    public const int Cost = 3;
    public const int CostUpgrade = -1;
    public const int SelectCount = 1;
    public const decimal PowerAmount = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Nightmare()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool CanBeGeneratedInCombat => false;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var selectedCard = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (selectedCard != null)
        {
            var power = PowerCmd.Apply<NightmarePower>(cs, owner, PowerAmount, owner, card);
            power!.SetSelectedCard(cs, selectedCard);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
