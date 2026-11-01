namespace Sts2Sim.Core.Cards;

public sealed class Fisticuffs : CardDef
{
    public const string CardId = "FISTICUFFS";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal DamageUpgrade = 2m;

    public Fisticuffs()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var fisticuffs = play.Card;
        var attackCommand = DamageCmd.Attack(cs, fisticuffs.Vars.Damage.BaseValue).FromCard(fisticuffs, play).Targeting(target).Execute();
        int dealt = attackCommand.Results.Sum(r => r.TotalDamage + r.OverkillDamage);
        CreatureCmd.GainBlock(cs, fisticuffs.Owner, dealt, ValueProp.Move, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
