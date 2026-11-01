namespace Sts2Sim.Core.Cards;

public sealed class CosmicIndifference : CardDef
{
    public const string CardId = "COSMIC_INDIFFERENCE";
    public const int Cost = 1;
    public const decimal BaseBlock = 6m;
    public const decimal BlockUpgrade = 3m;
    public const int SelectCount = 1;

    public CosmicIndifference()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

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
            var pileType = cardModel.GetCombatPile(cs)?.Type;
            if (pileType == PileType.Draw || pileType == PileType.Discard)
            {
                CardPileCmd.Add(cs, cardModel, PileType.Draw, CardPilePosition.Top);
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
