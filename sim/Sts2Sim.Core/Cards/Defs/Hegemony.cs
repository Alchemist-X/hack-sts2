namespace Sts2Sim.Core.Cards;

public sealed class Hegemony : CardDef
{
    public const string CardId = "HEGEMONY";
    public const int Cost = 2;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";
    public const decimal BaseDamage = 15m;
    public const int BaseEnergy = 2;
    public const decimal DamageUpgrade = 3m;
    public const decimal EnergyUpgrade = 1m;

    public Hegemony()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, card.Vars.Energy.IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
