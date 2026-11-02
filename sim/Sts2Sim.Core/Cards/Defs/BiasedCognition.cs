namespace Sts2Sim.Core.Cards;

public sealed class BiasedCognition : CardDef
{
    public const string CardId = "BIASED_COGNITION";
    public const int Cost = 1;
    public const string BiasedCognitionPowerId = "BIASED_COGNITION_POWER";
    public const string BiasedCognitionPowerKey = "BiasedCognitionPower";
    public const decimal BaseFocus = 5m;
    public const decimal BaseBiasedCognition = 1m;
    public const decimal FocusUpgrade = 1m;

    public BiasedCognition()
        : base(CardId, Cost, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<FocusPower>(BaseFocus),
        new PowerVar<PowerModel>(BiasedCognitionPowerKey, BaseBiasedCognition)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var biasedCognition = play.Card;
        var owner = biasedCognition.Owner;
        PowerCmd.Apply<FocusPower>(cs, owner, biasedCognition.Vars[nameof(FocusPower)].BaseValue, owner, biasedCognition);
        PowerCmd.Apply(cs, PowerRegistry.Create(BiasedCognitionPowerId), owner, biasedCognition.Vars[BiasedCognitionPowerKey].BaseValue, owner, biasedCognition);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[nameof(FocusPower)].UpgradeValueBy(FocusUpgrade);
    }
}
