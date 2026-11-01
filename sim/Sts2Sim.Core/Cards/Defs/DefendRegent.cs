using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DefendRegent : CardDef
{
    public const string CardId = "DEFEND_REGENT";
    public const int Cost = 1;
    public const decimal BaseBlock = 5m;
    public const decimal BlockUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Defend });

    public DefendRegent()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CreatureCmd.GainBlock(cs, card.Owner, card.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
