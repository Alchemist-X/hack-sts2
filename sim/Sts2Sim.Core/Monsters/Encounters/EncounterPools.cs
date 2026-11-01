namespace Sts2Sim.Core.Monsters;

public static class EncounterPools
{
    public static IReadOnlyList<int> ValidIndices(IReadOnlyList<int> validCounts, IReadOnlyList<int> current)
    {
        var items = new List<int>(validCounts.Count);
        for (int r = 0; r < validCounts.Count; r++)
        {
            int taken = 0;
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i] == r)
                {
                    taken++;
                }
            }
            if (taken < validCounts[r])
            {
                items.Add(r);
            }
        }
        return items;
    }
}
