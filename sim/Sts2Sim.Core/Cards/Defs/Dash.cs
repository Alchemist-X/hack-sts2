namespace Sts2Sim.Core.Cards;

public sealed class Dash : CardDef
{
    public const string CardId = "DASH";
    public const int Cost = 2;
    public const decimal BaseDamage = 10m;
    public const decimal BaseBlock = 10m;
    public const decimal DamageUpgrade = 3m;
    public const decimal BlockUpgrade = 3m;

    public Dash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        CreatureCmd.GainBlock(cs, card.Owner, card.Vars.Block, play);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
