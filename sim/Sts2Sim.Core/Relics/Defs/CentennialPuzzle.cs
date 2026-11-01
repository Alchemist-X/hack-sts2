namespace Sts2Sim.Core.Relics;

public sealed class CentennialPuzzle : RelicModel
{
    public const string RelicId = "CENTENNIAL_PUZZLE";

    private bool _usedThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public bool UsedThisCombat
    {
        get => _usedThisCombat;
        private set
        {
            if (_usedThisCombat != value)
            {
                _usedThisCombat = value;
            }
        }
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat;
        if (cs == null || !cs.IsInProgress || !ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || UsedThisCombat)
        {
            return;
        }
        UsedThisCombat = true;
        for (int i = 0; i < DynamicVars.Cards.BaseValue; i++)
        {
            CardPileCmd.Draw(cs, Owner);
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        UsedThisCombat = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisCombat);
    }
}
