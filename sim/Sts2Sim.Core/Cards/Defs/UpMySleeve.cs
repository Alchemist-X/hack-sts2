namespace Sts2Sim.Core.Cards;

public sealed class UpMySleeve : CardDef
{
    public const string CardId = "UP_MY_SLEEVE";
    public const int Cost = 2;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;
    public const int CostReduction = -1;
    public const ushort StateTag = 0xCA34;

    public UpMySleeve()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override CardExtraState? CreateExtraState() => new UpMySleeveState();

    public static int GetTimesPlayedThisCombat(CardInstance card) => ((UpMySleeveState)card.Extra!).TimesPlayedThisCombat;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var upMySleeve = play.Card;
        for (int i = 0; i < upMySleeve.Vars.Cards.IntValue; i++)
        {
            Shiv.CreateInHand(cs, upMySleeve.Owner);
        }
        ((UpMySleeveState)upMySleeve.Extra!).TimesPlayedThisCombat++;
        upMySleeve.EnergyCost.AddThisCombat(CostReduction);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    private sealed class UpMySleeveState : CardExtraState
    {
        public int TimesPlayedThisCombat { get; set; }

        public override CardExtraState DeepClone() => (UpMySleeveState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(TimesPlayedThisCombat);
        }
    }
}
