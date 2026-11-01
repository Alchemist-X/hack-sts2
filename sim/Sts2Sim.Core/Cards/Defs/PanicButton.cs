using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class PanicButton : CardDef
{
    public const string CardId = "PANIC_BUTTON";
    public const int Cost = 0;
    public const string NoBlockPowerId = "NO_BLOCK_POWER";
    public const string TurnsKey = "Turns";
    public const decimal BaseBlock = 30m;
    public const decimal BaseTurns = 2m;
    public const decimal BlockUpgrade = 10m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public PanicButton()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(TurnsKey, BaseTurns)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(NoBlockPowerId), owner, card.Vars[TurnsKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
