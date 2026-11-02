namespace Sts2Sim.Core.Cards;

public sealed class Friendship : CardDef
{
    public const string CardId = "FRIENDSHIP";
    public const int Cost = 1;
    public const string FriendshipPowerId = "FRIENDSHIP_POWER";
    public const decimal BaseStrength = 2m;
    public const int BaseEnergy = 1;
    public const decimal StrengthUpgrade = -1m;

    public Friendship()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(BaseStrength),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply<StrengthPower>(cs, owner, -card.Vars.Strength.BaseValue, owner, card);
        PowerCmd.Apply(cs, PowerRegistry.Create(FriendshipPowerId), owner, card.Vars.Energy.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
