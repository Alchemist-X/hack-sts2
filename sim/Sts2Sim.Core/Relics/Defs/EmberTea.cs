namespace Sts2Sim.Core.Relics;

public sealed class EmberTea : RelicModel
{
    public const string RelicId = "EMBER_TEA";

    public const string CombatsKey = "Combats";

    private int _combatsLeft = 5;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool IsUsedUp => CombatsLeft <= 0;

    public override bool ShowCounter => true;

    public override int DisplayAmount => Math.Max(0, CombatsLeft);

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CombatsKey, CombatsLeft),
        new PowerVar<StrengthPower>(2m)
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

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (IsUsedUp || room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException("EmberTea.AfterRoomEntered: combat room without combat state");
        PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars[DynamicVarSet.StrengthName].BaseValue, null, null);
        CombatsLeft--;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatsLeft);
    }
}
