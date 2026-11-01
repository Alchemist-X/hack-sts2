using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Transfigure : CardDef
{
    public const string CardId = "TRANSFIGURE";
    public const int Cost = 1;
    public const int BaseEnergy = 1;
    public const int SelectCount = 1;
    public const int CostIncrease = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Transfigure()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var cardToTransfigure = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToList();
        for (int i = 0; i < cardToTransfigure.Count; i++)
        {
            var current = cardToTransfigure[i];
            if (!current.EnergyCost.CostsX && current.EnergyCost.GetWithModifiers(CostModifiers.None) >= 0)
            {
                current.EnergyCost.AddThisCombat(CostIncrease);
            }
            current.BaseReplayCount++;
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
