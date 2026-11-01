namespace Sts2Sim.Core.Cards;

public sealed class Survivor : CardDef
{
    public const string CardId = "SURVIVOR";
    public const int Cost = 1;
    public const decimal BaseBlock = 8m;
    public const decimal BlockUpgrade = 3m;
    public const int DiscardCount = 1;

    public Survivor()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
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
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, DiscardCount);
        var cardModel = CardSelectCmd.FromHandForDiscard(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Discard(cs, cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
