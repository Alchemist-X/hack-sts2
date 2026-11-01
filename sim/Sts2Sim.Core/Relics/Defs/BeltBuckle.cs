namespace Sts2Sim.Core.Relics;

public sealed class BeltBuckle : RelicModel
{
    public const string RelicId = "BELT_BUCKLE";

    private bool _dexterityApplied;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    private bool DexterityApplied
    {
        get => _dexterityApplied;
        set => _dexterityApplied = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.DexterityName, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress && !Owner.Potions.Any())
        {
            ApplyDexterity(cs);
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        DexterityApplied = false;
        RefreshStatus(rs);
        if (!Owner.Potions.Any())
        {
            ApplyDexterity(rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart outside combat"));
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        RefreshStatus(rs);
    }

    public override void AfterPotionProcured(RunState rs, PotionModel potion)
    {
        RefreshStatus(rs);
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress && Owner.Potions.Any())
        {
            RemoveDexterity(cs);
        }
    }

    public override void AfterPotionDiscarded(RunState rs, PotionModel potion)
    {
        RefreshStatus(rs);
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress && !Owner.Potions.Any())
        {
            ApplyDexterity(cs);
        }
    }

    public override void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target)
    {
        RefreshStatus(rs);
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress && !Owner.Potions.Any())
        {
            ApplyDexterity(cs);
        }
    }

    public override void AfterCombatVictory(RunState rs, CombatRoom? room)
    {
        DexterityApplied = false;
        RefreshStatus(rs);
    }

    private void ApplyDexterity(CombatState cs)
    {
        if (!DexterityApplied)
        {
            DexterityApplied = true;
            PowerCmd.Apply<DexterityPower>(cs, Owner, DynamicVars.Dexterity.BaseValue, null, null);
        }
    }

    private void RemoveDexterity(CombatState cs)
    {
        if (DexterityApplied)
        {
            DexterityApplied = false;
            PowerCmd.Apply<DexterityPower>(cs, Owner, -DynamicVars.Dexterity.BaseValue, null, null);
        }
    }

    private void RefreshStatus(RunState rs)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress && !Owner.Potions.Any())
        {
            Status = RelicStatus.Active;
        }
        else
        {
            Status = RelicStatus.Normal;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_dexterityApplied);
    }
}
