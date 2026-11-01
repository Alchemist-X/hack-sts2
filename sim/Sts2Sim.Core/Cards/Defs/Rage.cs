namespace Sts2Sim.Core.Cards;

public sealed class Rage : CardDef
{
    public const string CardId = "RAGE";
    public const int Cost = 0;
    public const string PowerKey = "Power";
    public const decimal BasePower = 3m;
    public const decimal PowerUpgrade = 2m;
    public const string RagePowerId = "RAGE_POWER";

    public Rage()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, RagePowerId, owner, card.Vars[PowerKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerKey].UpgradeValueBy(PowerUpgrade);
    }
}
