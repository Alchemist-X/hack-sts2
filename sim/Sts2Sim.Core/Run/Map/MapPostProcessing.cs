namespace Sts2Sim.Core.Run;

public static class MapPostProcessing
{
    public static MapPoint?[,] CenterGrid(MapPoint?[,] grid)
    {
        int length = grid.GetLength(0);
        int length2 = grid.GetLength(1);
        bool leftEmpty = IsColumnEmpty(grid, 0) && IsColumnEmpty(grid, 1);
        bool rightEmpty = IsColumnEmpty(grid, length - 1) && IsColumnEmpty(grid, length - 2);
        int shift = 0;
        if (leftEmpty && !rightEmpty)
        {
            shift = -1;
        }
        else if (!leftEmpty && rightEmpty)
        {
            shift = 1;
        }
        if (shift == 0)
        {
            return grid;
        }
        if (shift > 0)
        {
            for (int i = 0; i < length2; i++)
            {
                for (int c = length - 1; c >= 0; c--)
                {
                    MapPoint? mapPoint = grid[c, i];
                    grid[c, i] = null;
                    int target = c + shift;
                    if (target < length)
                    {
                        grid[target, i] = mapPoint;
                        mapPoint?.SetColumn(target);
                    }
                }
            }
        }
        else
        {
            for (int j = 0; j < length2; j++)
            {
                for (int k = 0; k < length; k++)
                {
                    MapPoint? mapPoint = grid[k, j];
                    grid[k, j] = null;
                    int target = k + shift;
                    if (target >= 0)
                    {
                        grid[target, j] = mapPoint;
                        mapPoint?.SetColumn(target);
                    }
                }
            }
        }
        return grid;
    }

    public static MapPoint?[,] StraightenPaths(MapPoint?[,] grid)
    {
        int length = grid.GetLength(0);
        int length2 = grid.GetLength(1);
        for (int i = 0; i < length2; i++)
        {
            for (int j = 0; j < length; j++)
            {
                MapPoint? mapPoint = grid[j, i];
                if (mapPoint == null || mapPoint.parents.Count != 1 || mapPoint.Children.Count != 1)
                {
                    continue;
                }
                MapPoint parent = mapPoint.parents.First();
                MapPoint child = mapPoint.Children.First();
                bool leftOfBoth = mapPoint.coord.col < child.coord.col && mapPoint.coord.col < parent.coord.col;
                bool rightOfBoth = mapPoint.coord.col > child.coord.col && mapPoint.coord.col > parent.coord.col;
                if (leftOfBoth && j < length - 1)
                {
                    int num = j + 1;
                    if (grid[num, i] != null)
                    {
                        continue;
                    }
                    mapPoint.SetColumn(num);
                    grid[j, i] = null;
                    grid[num, i] = mapPoint;
                }
                if (rightOfBoth && j > 0)
                {
                    int num2 = j - 1;
                    if (grid[num2, i] == null)
                    {
                        mapPoint.SetColumn(num2);
                        grid[j, i] = null;
                        grid[num2, i] = mapPoint;
                    }
                }
            }
        }
        return grid;
    }

    private static bool IsColumnEmpty(MapPoint?[,] grid, int col)
    {
        int length = grid.GetLength(1);
        for (int i = 0; i < length; i++)
        {
            if (grid[col, i] != null)
            {
                return false;
            }
        }
        return true;
    }

    private static HashSet<int> GetNeighborAllowedPositions(int column, int totalColumns)
    {
        var result = new HashSet<int>(3);
        for (int i = -1; i <= 1; i++)
        {
            int num = column + i;
            if (num >= 0 && num < totalColumns)
            {
                result.Add(num);
            }
        }
        return result;
    }

    private static HashSet<int> GetAllowedPositions(MapPoint node, int totalColumns)
    {
        var result = new HashSet<int>(Enumerable.Range(0, totalColumns));
        foreach (MapPoint parent in node.parents)
        {
            result.IntersectWith(GetNeighborAllowedPositions(parent.coord.col, totalColumns));
        }
        foreach (MapPoint child in node.Children)
        {
            result.IntersectWith(GetNeighborAllowedPositions(child.coord.col, totalColumns));
        }
        return result;
    }

    public static MapPoint?[,] SpreadAdjacentMapPoints(MapPoint?[,] grid)
    {
        int length = grid.GetLength(0);
        int length2 = grid.GetLength(1);
        for (int i = 0; i < length2; i++)
        {
            var row = new List<MapPoint>(length2);
            for (int j = 0; j < length; j++)
            {
                MapPoint? mapPoint = grid[j, i];
                if (mapPoint != null)
                {
                    row.Add(mapPoint);
                }
            }
            bool moved;
            do
            {
                moved = false;
                foreach (MapPoint current in row)
                {
                    int col = current.coord.col;
                    HashSet<int> allowedPositions = GetAllowedPositions(current, length);
                    int bestGap = ComputeGap(col, row, current);
                    int bestCol = col;
                    foreach (int candidate in allowedPositions)
                    {
                        if (candidate != col && (grid[candidate, i] == null || grid[candidate, i] == current))
                        {
                            int gap = ComputeGap(candidate, row, current);
                            if (gap > bestGap)
                            {
                                bestCol = candidate;
                                bestGap = gap;
                            }
                        }
                    }
                    if (bestCol != col)
                    {
                        grid[col, i] = null;
                        grid[bestCol, i] = current;
                        current.SetColumn(bestCol);
                        moved = true;
                    }
                }
            }
            while (moved);
        }
        return grid;
    }

    private static int ComputeGap(int candidateCol, List<MapPoint> rowNodes, MapPoint currentNode)
    {
        int num = int.MaxValue;
        foreach (MapPoint node in rowNodes)
        {
            if (node != currentNode)
            {
                num = Math.Min(num, Math.Abs(candidateCol - node.coord.col));
            }
        }
        return num;
    }
}
