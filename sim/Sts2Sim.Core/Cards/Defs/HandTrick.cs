namespace Sts2Sim.Core.Cards;

public sealed class HandTrick : CardDef
{
    public const string CardId = "HAND_TRICK";
    public const int Cost = 1;
    public const decimal BaseBlock = 7m;
    public const decimal BlockUpgrade = 3m;
    public const int SelectCount = 1;

    public HandTrick()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
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
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, c => c.Type == CardType.Skill && !CardPileCmd.IsSlyThisTurn(cs, c), ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.ApplySingleTurnSly(cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
