using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class UltimateDefend : CardDef
{
    public const string CardId = "ULTIMATE_DEFEND";
    public const int Cost = 1;
    public const decimal BaseBlock = 11m;
    public const decimal BlockUpgrade = 4m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Defend });

    public UltimateDefend()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
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
        var ultimateDefend = play.Card;
        CreatureCmd.GainBlock(cs, ultimateDefend.Owner, ultimateDefend.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
