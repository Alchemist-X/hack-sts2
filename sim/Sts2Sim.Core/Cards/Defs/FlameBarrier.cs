namespace Sts2Sim.Core.Cards;

public sealed class FlameBarrier : CardDef
{
    public const string CardId = "FLAME_BARRIER";
    public const int Cost = 2;
    public const string FlameBarrierPowerId = "FLAME_BARRIER_POWER";
    public const string DamageBackKey = "DamageBack";
    public const decimal BaseBlock = 12m;
    public const decimal BaseDamageBack = 4m;
    public const decimal BlockUpgrade = 4m;
    public const decimal DamageBackUpgrade = 2m;

    public FlameBarrier()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(DamageBackKey, BaseDamageBack)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(FlameBarrierPowerId), owner, card.Vars[DamageBackKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars[DamageBackKey].UpgradeValueBy(DamageBackUpgrade);
    }
}
