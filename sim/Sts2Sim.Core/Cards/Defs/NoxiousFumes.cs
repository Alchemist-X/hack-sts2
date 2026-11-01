namespace Sts2Sim.Core.Cards;

public sealed class NoxiousFumes : CardDef
{
    public const string CardId = "NOXIOUS_FUMES";
    public const int Cost = 1;
    public const string NoxiousFumesPowerId = "NOXIOUS_FUMES_POWER";
    public const string PoisonPerTurnKey = "PoisonPerTurn";
    public const decimal BasePoisonPerTurn = 2m;
    public const decimal PoisonPerTurnUpgrade = 1m;

    public NoxiousFumes()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PoisonPerTurnKey, BasePoisonPerTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(NoxiousFumesPowerId), owner, card.Vars[PoisonPerTurnKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PoisonPerTurnKey].UpgradeValueBy(PoisonPerTurnUpgrade);
    }
}
