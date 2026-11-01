namespace Sts2Sim.Core.Cards;

public sealed class LegSweep : CardDef
{
    public const string CardId = "LEG_SWEEP";
    public const int Cost = 2;
    public const decimal BaseBlock = 11m;
    public const decimal BaseWeak = 2m;
    public const decimal BlockUpgrade = 3m;
    public const decimal WeakUpgrade = 1m;

    public LegSweep()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        CreatureCmd.GainBlock(cs, card.Owner, card.Vars.Block, play);
        PowerCmd.Apply<WeakPower>(cs, target, card.Vars.Weak.BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars.Weak.UpgradeValueBy(WeakUpgrade);
    }
}
