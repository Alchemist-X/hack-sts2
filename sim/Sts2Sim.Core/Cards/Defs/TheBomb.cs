namespace Sts2Sim.Core.Cards;

public sealed class TheBomb : CardDef
{
    public const string CardId = "THE_BOMB";
    public const int Cost = 2;
    public const string TurnsKey = "Turns";
    public const string BombDamageKey = "BombDamage";
    public const decimal BaseTurns = 3m;
    public const decimal BaseBombDamage = 40m;
    public const decimal BombDamageUpgrade = 10m;
    public const string SetDamageMember = "SetDamage";
    public const string TheBombPowerId = "THE_BOMB_POWER";

    public TheBomb()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(TurnsKey, BaseTurns),
        new DynamicVar(BombDamageKey, BaseBombDamage)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var theBombPower = PowerCmd.Apply(cs, TheBombPowerId, owner, card.Vars[TurnsKey].BaseValue, owner, card);
        ArgumentNullException.ThrowIfNull(theBombPower);
        theBombPower.GetType().GetMethod(SetDamageMember, new[] { typeof(decimal) })!.Invoke(theBombPower, new object[] { card.Vars[BombDamageKey].BaseValue });
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[BombDamageKey].UpgradeValueBy(BombDamageUpgrade);
    }
}
