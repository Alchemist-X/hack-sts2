using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BootSequence : CardDef
{
    public const string CardId = "BOOT_SEQUENCE";
    public const int Cost = 0;
    public const decimal BaseBlock = 10m;
    public const decimal BlockUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Innate, CardKeyword.Exhaust });

    public BootSequence()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

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
