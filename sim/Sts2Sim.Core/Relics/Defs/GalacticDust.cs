namespace Sts2Sim.Core.Relics;

public sealed class GalacticDust : RelicModel
{
    public const string RelicId = "GALACTIC_DUST";

    private bool _isActivating;
    private int _starsSpent;

    public override string Id => RelicId;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return StarsSpent % DynamicVars.Stars.IntValue;
            }
            return DynamicVars.Stars.IntValue;
        }
    }

    private bool IsActivating
    {
        get => _isActivating;
        set
        {
            _isActivating = value;
            UpdateDisplay();
        }
    }

    public int StarsSpent
    {
        get => _starsSpent;
        set
        {
            _starsSpent = value;
            UpdateDisplay();
        }
    }

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(10),
        new BlockVar(10m, ValueProp.Unpowered)
    };

    private void UpdateDisplay()
    {
        if (IsActivating)
        {
            Status = RelicStatus.Normal;
        }
        else
        {
            int intValue = DynamicVars.Stars.IntValue;
            Status = StarsSpent == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)
    {
        if (!ReferenceEquals(spender, Owner))
        {
            return;
        }
        StarsSpent += amount;
        if (StarsSpent >= DynamicVars.Stars.IntValue)
        {
            DoActivateVisuals();
            CreatureCmd.GainBlock(cs, Owner, StarsSpent / DynamicVars.Stars.IntValue * DynamicVars.Block.IntValue, ValueProp.Unpowered, null);
            StarsSpent %= DynamicVars.Stars.IntValue;
        }
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_starsSpent);
    }
}
