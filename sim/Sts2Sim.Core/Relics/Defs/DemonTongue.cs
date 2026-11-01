namespace Sts2Sim.Core.Relics;

public sealed class DemonTongue : RelicModel
{
    public const string RelicId = "DEMON_TONGUE";

    private bool _triggeredThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat;
        if (cs == null || !CombatStatus.IsAttached(cs, Owner))
        {
            return;
        }
        if (cs.CurrentSide == Owner.Side && ReferenceEquals(target, Owner) && result.UnblockedDamage > 0 && !_triggeredThisTurn)
        {
            _triggeredThisTurn = true;
            CreatureCmd.Heal(cs, Owner, result.UnblockedDamage);
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        _triggeredThisTurn = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_triggeredThisTurn);
    }
}
