namespace Sts2Sim.Core.Relics;

public sealed class TeaOfDiscourtesy : RelicModel
{
    public const string RelicId = "TEA_OF_DISCOURTESY";

    public const string CombatsKey = "Combats";

    public const string DazedCountKey = "DazedCount";

    public const string DazedCardId = "DAZED";

    private int _combatsLeft = 1;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool IsUsedUp => CombatsLeft <= 0;

    public override bool ShowCounter => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(1m),
        new DynamicVar(CombatsKey, CombatsLeft),
        new DynamicVar(DazedCountKey, 2m)
    };

    public int CombatsLeft
    {
        get => _combatsLeft;
        set
        {
            _combatsLeft = value;
            DynamicVars[CombatsKey].BaseValue = _combatsLeft;
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        if (CombatsLeft > 0)
        {
            var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart: no attached combat");
            CardPileCmd.AddToCombatAndPreview(cs, Owner, DazedCardId, PileType.Draw, DynamicVars[DazedCountKey].IntValue, Owner, CardPilePosition.Random);
            CombatsLeft--;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatsLeft);
    }
}
