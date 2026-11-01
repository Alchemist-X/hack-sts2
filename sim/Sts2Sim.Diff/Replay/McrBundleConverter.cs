using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.Replay;

public sealed record EncounterResolution(string? EncounterId, Sts2Sim.Core.MapPointType? PointType, string Reason);

public static partial class McrBundleConverter
{
    public static EncounterResolution ResolveEncounter(McrReplay replay)
    {
        var run = replay.Run;
        if (run.CurrentActIndex < 0 || run.CurrentActIndex >= run.Acts.Count)
        {
            return new(null, null, "act index out of range");
        }
        var act = run.Acts[run.CurrentActIndex];
        var rooms = act.Rooms;
        Sts2Sim.Core.MapPointType? pointType = null;
        if (run.VisitedMapCoords.Count > 0 && act.SavedMap != null)
        {
            var coord = run.VisitedMapCoords[^1];
            var map = act.SavedMap;
            var point = map.Points.FirstOrDefault(p => p.Coord == coord)
                ?? (map.Boss?.Coord == coord ? map.Boss : null)
                ?? (map.SecondBoss?.Coord == coord ? map.SecondBoss : null);
            pointType = point?.Type;
        }
        string? normal = rooms.NormalEncountersVisited < rooms.NormalEncounterIds.Count ? rooms.NormalEncounterIds[rooms.NormalEncountersVisited] : null;
        string? elite = rooms.EliteEncountersVisited < rooms.EliteEncounterIds.Count ? rooms.EliteEncounterIds[rooms.EliteEncountersVisited] : null;
        string? boss = rooms.BossEncountersVisited == 0 ? rooms.BossId : rooms.SecondBossId;
        var monsters = replay.Checksums.Count > 0
            ? replay.Checksums[0].State.Creatures.Where(c => c.MonsterId != null).Select(c => c.MonsterId!).ToList()
            : new List<string>();
        bool Matches(string? id)
        {
            if (id == null || monsters.Count == 0 || !EncounterRegistry.TryGet(id, out var def))
            {
                return false;
            }
            var possible = def.AllPossibleMonsters;
            return possible.Count > 0 && monsters.All(m => possible.Contains(m, StringComparer.Ordinal));
        }
        switch (pointType)
        {
            case Sts2Sim.Core.MapPointType.Monster:
                return new(normal, pointType, "map point Monster -> next normal encounter");
            case Sts2Sim.Core.MapPointType.Elite:
                return new(elite, pointType, "map point Elite -> next elite encounter");
            case Sts2Sim.Core.MapPointType.Boss:
                return new(boss, pointType, "map point Boss -> next boss encounter");
        }
        foreach (var (id, label) in new[] { (normal, "normal"), (elite, "elite"), (boss, "boss") })
        {
            if (Matches(id))
            {
                return new(id, pointType, $"map point {pointType?.ToString() ?? "?"}: first-checksum monsters match the next {label} encounter");
            }
        }
        return new(null, pointType, $"map point {pointType?.ToString() ?? "?"}: no candidate encounter matches monsters [{string.Join(",", monsters)}]");
    }
}
