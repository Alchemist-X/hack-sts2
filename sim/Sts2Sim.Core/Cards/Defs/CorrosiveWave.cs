namespace Sts2Sim.Core.Cards;

public sealed class CorrosiveWave : CardDef
{
    public const string CardId = "CORROSIVE_WAVE";
    public const int Cost = 1;
    public const string CorrosiveWavePowerId = "CORROSIVE_WAVE_POWER";
    public const string PowerVarName = "CorrosiveWave";
    public const decimal BaseCorrosiveWave = 2m;
    public const decimal CorrosiveWaveUpgrade = 1m;

    public CorrosiveWave()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseCorrosiveWave)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var corrosiveWave = play.Card;
        var owner = corrosiveWave.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CorrosiveWavePowerId), owner, corrosiveWave.Vars[PowerVarName].BaseValue, owner, corrosiveWave);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(CorrosiveWaveUpgrade);
    }
}
