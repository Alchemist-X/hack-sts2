namespace Sts2Sim.Core.Cards;

public sealed class TrueGrit : CardDef
{
    public const string CardId = "TRUE_GRIT";
    public const int Cost = 1;
    public const decimal BaseBlock = 7m;
    public const decimal BlockUpgrade = 2m;
    public const int SelectCount = 1;

    public TrueGrit()
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
        if (card.IsUpgraded)
        {
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, SelectCount);
            var chosen = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
            if (chosen != null)
            {
                CardCmd.Exhaust(cs, chosen);
            }
            return;
        }
        var cardModel = cs.Rng.CombatCardSelection.NextItem(owner.Hand.Cards);
        if (cardModel != null)
        {
            CardCmd.Exhaust(cs, cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
