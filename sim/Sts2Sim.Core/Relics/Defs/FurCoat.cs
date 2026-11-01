namespace Sts2Sim.Core.Relics;

public sealed class FurCoat : RelicModel
{
    public const string RelicId = "FUR_COAT";

    private const string _combatsKey = "Combats";

    private int _furCoatActIndex = -1;
    private int[] _furCoatCoordCols = Array.Empty<int>();
    private int[] _furCoatCoordRows = Array.Empty<int>();
    private bool _furCoatCoordsSet;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public int FurCoatActIndex
    {
        get => _furCoatActIndex;
        set => _furCoatActIndex = value;
    }

    private int[] FurCoatCoordCols
    {
        get => _furCoatCoordCols;
        set => _furCoatCoordCols = value;
    }

    private int[] FurCoatCoordRows
    {
        get => _furCoatCoordRows;
        set => _furCoatCoordRows = value;
    }

    private bool FurCoatCoordsSet
    {
        get => _furCoatCoordsSet;
        set => _furCoatCoordsSet = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(_combatsKey, 8m)
    };

    public string QuestKey => RelicId + "#" + Owner.CombatId;

    public override void AfterObtained(RunState rs)
    {
        FurCoatActIndex = rs.CurrentActIndex;
        if (rs.Map != null)
        {
            rs.Map = AddMarkedRooms(rs, rs.Map);
        }
    }

    public override ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex)
    {
        return AddMarkedRooms(rs, map);
    }

    private ActMap AddMarkedRooms(RunState rs, ActMap map)
    {
        if (rs.CurrentActIndex != FurCoatActIndex)
        {
            return map;
        }
        var markedCoords = GetMarkedCoords();
        bool flag = markedCoords == null;
        if (!flag)
        {
            flag = !markedCoords!.TrueForAll(c => map.HasPoint(c) && (map.GetPoint(c)!.PointType == MapPointType.Monster || map.GetPoint(c)!.PointType == MapPointType.Elite));
        }
        if (flag)
        {
            var rng = Rng.ForPlayerModel(rs.Rng, rs.GetPlayerSlotIndex(Owner), RelicId);
            var candidates = map.GetAllMapPoints()
                .Where(p => (p.PointType == MapPointType.Monster || p.PointType == MapPointType.Elite) && !HasFurCoatQuest(map, p.coord))
                .ToList();
            candidates.UnstableShuffle(rng);
            int count = DynamicVars[_combatsKey].IntValue;
            var picked = candidates.Take(count).ToList();
            FurCoatCoordCols = new int[picked.Count];
            FurCoatCoordRows = new int[picked.Count];
            for (int i = 0; i < picked.Count; i++)
            {
                FurCoatCoordCols[i] = picked[i].coord.col;
                FurCoatCoordRows[i] = picked[i].coord.row;
            }
            FurCoatCoordsSet = true;
            foreach (var point in picked)
            {
                map = map.WithQuestAdded(point.coord, QuestKey);
            }
        }
        else
        {
            foreach (var coord in markedCoords!)
            {
                if (map.GetPoint(coord) == null)
                {
                    throw new InvalidOperationException($"Loaded a fur coat map with coordinate {coord}, but the generated map does not contain that coordinate!");
                }
                map = map.WithQuestAdded(coord, QuestKey);
            }
        }
        return map;
    }

    private static bool HasFurCoatQuest(ActMap map, MapCoord coord)
    {
        foreach (var key in map.GetQuests(coord))
        {
            if (key.StartsWith(RelicId + "#", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public override void BeforeCombatStart(RunState rs)
    {
        var markedCoords = GetMarkedCoords();
        if (markedCoords == null || RunLoop.CurrentMapCoord(rs) is not { } current || !markedCoords.Contains(current))
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart outside combat");
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        foreach (var enemy in hittable)
        {
            CreatureCmd.SetCurrentHp(cs, enemy, 1m);
        }
    }

    public override void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
        if (creature.Side != CombatSide.Enemy)
        {
            return;
        }
        var markedCoords = GetMarkedCoords();
        if (markedCoords != null && RunLoop.CurrentMapCoord(cs.Run) is { } current && markedCoords.Contains(current))
        {
            CreatureCmd.SetCurrentHp(cs, creature, 1m);
        }
    }

    public List<MapCoord>? GetMarkedCoords()
    {
        if (!FurCoatCoordsSet)
        {
            return null;
        }
        var list = new List<MapCoord>();
        for (int i = 0; i < FurCoatCoordCols.Length; i++)
        {
            list.Add(new MapCoord(FurCoatCoordCols[i], FurCoatCoordRows[i]));
        }
        return list;
    }

    protected override void CopyMutableReferenceState(RelicModel source)
    {
        var other = (FurCoat)source;
        _furCoatCoordCols = (int[])other._furCoatCoordCols.Clone();
        _furCoatCoordRows = (int[])other._furCoatCoordRows.Clone();
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_furCoatActIndex);
        w.WriteBool(_furCoatCoordsSet);
        w.WriteCount(_furCoatCoordCols.Length);
        for (int i = 0; i < _furCoatCoordCols.Length; i++)
        {
            w.WriteInt32(_furCoatCoordCols[i]);
            w.WriteInt32(_furCoatCoordRows[i]);
        }
    }
}
