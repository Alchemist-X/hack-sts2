namespace Sts2Sim.Core.Relics;

public sealed class StoneCalendar : RelicModel
{
    public const string RelicId = "STONE_CALENDAR";

    public const string DamageTurnKey = "DamageTurn";

    private bool _isActivating;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(52m, ValueProp.Unpowered),
        new DynamicVar(DamageTurnKey, 7m)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Owner.TurnNumber == DynamicVars[DamageTurnKey].IntValue)
        {
            Status = RelicStatus.Active;
        }
    }

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        int intValue = DynamicVars[DamageTurnKey].IntValue;
        int turnNumber = Owner.TurnNumber;
        Status = RelicStatus.Normal;
        if (turnNumber == intValue)
        {
            DoActivateVisuals();
            var hittable = new List<MonsterState>(cs.Monsters.Count);
            cs.GetHittableEnemies(hittable);
            CreatureCmd.Damage(cs, hittable, DynamicVars.Damage, Owner);
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        Status = RelicStatus.Normal;
    }

    private void DoActivateVisuals()
    {
        _isActivating = true;
        _isActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
    }
}
