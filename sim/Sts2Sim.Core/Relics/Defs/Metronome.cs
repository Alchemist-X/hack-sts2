namespace Sts2Sim.Core.Relics;

public sealed class Metronome : RelicModel
{
    public const string RelicId = "METRONOME";

    private const string OrbCountKey = "OrbCount";

    private bool _isActivating;
    private int _orbsChanneled;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool ShowCounter
    {
        get
        {
            if (OrbsChanneled >= DynamicVars[OrbCountKey].IntValue)
            {
                return IsActivating;
            }
            return true;
        }
    }

    public override int DisplayAmount => Math.Min(OrbsChanneled, DynamicVars[OrbCountKey].IntValue);

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(30m, ValueProp.Unpowered),
        new DynamicVar(OrbCountKey, 7m)
    };

    private bool IsActivating
    {
        get => _isActivating;
        set
        {
            _isActivating = value;
            UpdateDisplay();
        }
    }

    private int OrbsChanneled
    {
        get => _orbsChanneled;
        set
        {
            _orbsChanneled = value;
            UpdateDisplay();
        }
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        OrbsChanneled = 0;
        UpdateDisplay();
    }

    public override void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        OrbsChanneled++;
        if (OrbsChanneled == DynamicVars[OrbCountKey].IntValue)
        {
            DoActivateVisuals();
            CreatureCmd.Damage(cs, HittableEnemies(cs), DynamicVars.Damage, Owner);
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        OrbsChanneled = 0;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        int intValue = DynamicVars[OrbCountKey].IntValue;
        if (OrbsChanneled == intValue - 1 && !IsActivating)
        {
            Status = RelicStatus.Active;
        }
        else
        {
            Status = RelicStatus.Normal;
        }
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_orbsChanneled);
    }
}
