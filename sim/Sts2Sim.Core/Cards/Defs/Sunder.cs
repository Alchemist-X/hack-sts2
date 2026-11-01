namespace Sts2Sim.Core.Cards;

public sealed class Sunder : CardDef
{
    public const string CardId = "SUNDER";
    public const int Cost = 3;
    public const decimal BaseDamage = 26m;
    public const int BaseEnergy = 3;
    public const decimal DamageUpgrade = 8m;

    public Sunder()
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
        var sunder = play.Card;
        var attackCommand = DamageCmd.Attack(cs, sunder.Vars.Damage.BaseValue).FromCard(sunder, play).Targeting(target).Execute();
        var results = attackCommand.Results;
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].WasTargetKilled)
            {
                PlayerCmd.GainEnergy(cs, sunder.Vars.Energy.IntValue, sunder.Owner);
                return;
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
