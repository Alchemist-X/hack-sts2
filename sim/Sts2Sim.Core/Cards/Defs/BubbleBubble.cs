namespace Sts2Sim.Core.Cards;

public sealed class BubbleBubble : CardDef
{
    public const string CardId = "BUBBLE_BUBBLE";
    public const int Cost = 1;
    public const string PoisonPowerId = "POISON_POWER";
    public const decimal BasePoison = 9m;
    public const decimal PoisonUpgrade = 3m;

    public BubbleBubble()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
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
        var bubbleBubble = play.Card;
        if (target.HasPower(PoisonPowerId))
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), target, bubbleBubble.Vars.Poison.BaseValue, bubbleBubble.Owner, bubbleBubble);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Poison.UpgradeValueBy(PoisonUpgrade);
    }
}
