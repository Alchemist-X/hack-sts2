namespace Sts2Sim.Core.Relics;

public sealed class Regalite : RelicModel
{
    public const string RelicId = "REGALITE";

    private bool _usedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(4m, ValueProp.Unpowered)
    };

    public bool UsedThisTurn
    {
        get => _usedThisTurn;
        private set => _usedThisTurn = value;
    }

    public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        if (creator == null || !ReferenceEquals(creator, Owner) || UsedThisTurn)
        {
            return;
        }
        UsedThisTurn = true;
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        UsedThisTurn = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        UsedThisTurn = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisTurn);
    }
}
