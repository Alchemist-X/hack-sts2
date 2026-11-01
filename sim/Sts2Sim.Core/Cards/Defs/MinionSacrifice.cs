using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class MinionSacrifice : CardDef
{
    public const string CardId = "MINION_SACRIFICE";
    public const int Cost = 0;
    public const decimal BaseBlock = 7m;
    public const decimal BlockUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });
    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Minion });

    public MinionSacrifice()
        : base(CardId, Cost, CardType.Skill, CardRarity.Token, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

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
