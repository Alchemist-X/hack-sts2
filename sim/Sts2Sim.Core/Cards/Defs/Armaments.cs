namespace Sts2Sim.Core.Cards;

public sealed class Armaments : CardDef
{
    public const string CardId = "ARMAMENTS";
    public const int Cost = 1;
    public const decimal BaseBlock = 5m;

    public Armaments()
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
        var armaments = play.Card;
        var owner = armaments.Owner;
        var block = armaments.Vars.Block;
        CreatureCmd.GainBlock(cs, owner, block.BaseValue, block.Props, play);
        if (!armaments.IsUpgraded)
        {
            var cardModel = CardSelectCmd.FromHandForUpgrade(cs, owner, ChoiceSource.For(armaments, "OnPlay"));
            if (cardModel != null)
            {
                CardCmd.Upgrade(cs, cardModel);
            }
            return;
        }
        var hand = owner.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            var current = hand[i];
            if (current.IsUpgradable)
            {
                CardCmd.Upgrade(cs, current);
            }
        }
    }
}
