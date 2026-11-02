namespace Sts2Sim.Core.Cards;

public sealed class Equilibrium : CardDef
{
    public const string CardId = "EQUILIBRIUM";
    public const int Cost = 2;
    public const string RetainHandPowerId = "RETAIN_HAND_POWER";
    public const string EquilibriumKey = "Equilibrium";
    public const decimal BaseBlock = 13m;
    public const decimal BaseEquilibrium = 1m;
    public const decimal BlockUpgrade = 3m;

    public Equilibrium()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(EquilibriumKey, BaseEquilibrium)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(RetainHandPowerId), owner, card.Vars[EquilibriumKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
