namespace Sts2Sim.Core.Cards;

public sealed class DeadlyPoison : CardDef
{
    public const string CardId = "DEADLY_POISON";
    public const int Cost = 1;
    public const string PoisonPowerId = "POISON_POWER";
    public const decimal BasePoison = 5m;
    public const decimal PoisonUpgrade = 2m;

    public DeadlyPoison()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, BasePoison)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var deadlyPoison = play.Card;
        PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), target, deadlyPoison.Vars.Poison.BaseValue, deadlyPoison.Owner, deadlyPoison);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Poison.UpgradeValueBy(PoisonUpgrade);
    }
}
