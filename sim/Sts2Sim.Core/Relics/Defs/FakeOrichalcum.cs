namespace Sts2Sim.Core.Relics;

public sealed class FakeOrichalcum : RelicModel
{
    public const string RelicId = "FAKE_ORICHALCUM";

    private bool _shouldTrigger;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(3m, ValueProp.Unpowered)
    };

    private bool ShouldTrigger
    {
        get => _shouldTrigger;
        set => _shouldTrigger = value;
    }

    public override void BeforeSideTurnEndVeryEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Owner.Block > 0)
        {
            return;
        }
        ShouldTrigger = true;
    }

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (ShouldTrigger)
        {
            ShouldTrigger = false;
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        ShouldTrigger = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_shouldTrigger);
    }
}
