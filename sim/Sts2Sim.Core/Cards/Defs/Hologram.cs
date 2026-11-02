using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Hologram : CardDef
{
    public const string CardId = "HOLOGRAM";
    public const int Cost = 1;
    public const decimal BaseBlock = 3m;
    public const decimal BlockUpgrade = 2m;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Hologram()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var block = card.Vars.Block;
        CreatureCmd.GainBlock(cs, owner, block.BaseValue, block.Props, play);
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromCombatPile(cs, owner.DiscardPile, owner, prefs, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Add(cs, cardModel, PileType.Hand);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
