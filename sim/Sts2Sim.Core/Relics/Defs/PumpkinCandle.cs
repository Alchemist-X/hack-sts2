namespace Sts2Sim.Core.Relics;

public sealed class PumpkinCandle : RelicModel
{
    public const string RelicId = "PUMPKIN_CANDLE";

    private const string DefaultCombatCountKey = "CombatCount";

    public const int KindleAmount = 5;

    private int _kindleCount;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShowCounter => true;

    public override int DisplayAmount => KindleCount;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DefaultCombatCountKey, 5m),
        new EnergyVar(1)
    };

    public int KindleCount
    {
        get => _kindleCount;
        set
        {
            _kindleCount = value;
            Status = KindleCount <= 0 ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    public override void AfterObtained(RunState rs)
    {
        Rekindle();
    }

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        if (KindleCount <= 0)
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        KindleCount = Math.Max(KindleCount - 1, 0);
    }

    public override bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        options.Add(new KindleRestSiteOption(player));
        return true;
    }

    public void Rekindle()
    {
        KindleCount += KindleAmount;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_kindleCount);
    }
}
