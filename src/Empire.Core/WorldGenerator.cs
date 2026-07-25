namespace Empire.Core;

public static class WorldGenerator
{
    private static readonly (int X, int Y, double Cost)[] Directions =
    [
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.414), (-1, 1, 1.414), (1, -1, 1.414), (-1, -1, 1.414)
    ];

    public static void GenerateTerrain(GameState state)
    {
        state.Terrain = new byte[GameConstants.MapHeight][];
        state.Navigation = new byte[GameConstants.MapHeight][];
        int[] fords = [9, 21, 33];
        for (var y = 0; y < GameConstants.MapHeight; y++)
        {
            state.Terrain[y] = new byte[GameConstants.MapWidth];
            state.Navigation[y] = new byte[GameConstants.MapWidth];
            for (var x = 0; x < GameConstants.MapWidth; x++)
            {
                var river = GameConstants.MapWidth * .5 + Math.Sin(y * .28) * 2.2 + Math.Sin(y * .67) * .65;
                var nearFord = fords.Any(ford => Math.Abs(y - ford) <= 1);
                var terrain = Math.Abs(x - river) < (nearFord ? .72 : 1.6)
                    ? (byte)1
                    : Hash2(x, y) > .84 ? (byte)2 : (byte)0;
                if ((x < 12 && y > 29) || (x > 45 && y < 13))
                {
                    terrain = 0;
                }

                state.Terrain[y][x] = terrain;
                state.Navigation[y][x] = terrain == 1 ? (byte)1 : (byte)0;
            }
        }
    }

    public static bool IsLand(GameState state, int x, int y) =>
        x >= 0 && y >= 0 && x < GameConstants.MapWidth && y < GameConstants.MapHeight && state.Navigation[y][x] == 0;

    public static bool IsLand(GameState state, double x, double y) =>
        IsLand(state, (int)Math.Floor(x / GameConstants.TileSize), (int)Math.Floor(y / GameConstants.TileSize));

    public static List<WorldPoint> FindPath(GameState state, double sourceX, double sourceY, double targetX, double targetY)
    {
        var startX = Math.Clamp((int)Math.Floor(sourceX / GameConstants.TileSize), 0, GameConstants.MapWidth - 1);
        var startY = Math.Clamp((int)Math.Floor(sourceY / GameConstants.TileSize), 0, GameConstants.MapHeight - 1);
        var targetCellX = Math.Clamp((int)Math.Floor(targetX / GameConstants.TileSize), 0, GameConstants.MapWidth - 1);
        var targetCellY = Math.Clamp((int)Math.Floor(targetY / GameConstants.TileSize), 0, GameConstants.MapHeight - 1);
        (targetCellX, targetCellY) = NearestLandCell(state, targetCellX, targetCellY);

        var start = startY * GameConstants.MapWidth + startX;
        var goal = targetCellY * GameConstants.MapWidth + targetCellX;
        if (start == goal)
        {
            return [new(targetX, targetY)];
        }

        var total = GameConstants.MapWidth * GameConstants.MapHeight;
        var distance = Enumerable.Repeat(double.PositiveInfinity, total).ToArray();
        var parent = Enumerable.Repeat(-1, total).ToArray();
        var closed = new bool[total];
        var open = new PriorityQueue<int, double>();
        distance[start] = 0;
        open.Enqueue(start, 0);

        var found = false;
        while (open.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            if (current == goal)
            {
                found = true;
                break;
            }

            closed[current] = true;
            var currentX = current % GameConstants.MapWidth;
            var currentY = current / GameConstants.MapWidth;
            foreach (var direction in Directions)
            {
                var nextX = currentX + direction.X;
                var nextY = currentY + direction.Y;
                if (!IsLand(state, nextX, nextY))
                {
                    continue;
                }

                if (direction.X != 0 && direction.Y != 0 &&
                    (!IsLand(state, currentX + direction.X, currentY) || !IsLand(state, currentX, currentY + direction.Y)))
                {
                    continue;
                }

                var next = nextY * GameConstants.MapWidth + nextX;
                var candidate = distance[current] + direction.Cost;
                if (closed[next] || candidate >= distance[next])
                {
                    continue;
                }

                distance[next] = candidate;
                parent[next] = current;
                var heuristic = Math.Max(Math.Abs(nextX - targetCellX), Math.Abs(nextY - targetCellY));
                open.Enqueue(next, candidate + heuristic);
            }
        }

        if (!found)
        {
            return [CellCenter(targetCellX, targetCellY)];
        }

        var reverse = new List<WorldPoint>();
        for (var node = goal; node != start && node >= 0; node = parent[node])
        {
            reverse.Add(CellCenter(node % GameConstants.MapWidth, node / GameConstants.MapWidth));
        }

        reverse.Reverse();
        var smoothed = new List<WorldPoint>();
        for (var index = 0; index < reverse.Count; index++)
        {
            if (index == reverse.Count - 1 || index % 3 == 2)
            {
                smoothed.Add(reverse[index]);
            }
        }

        if (smoothed.Count == 0)
        {
            smoothed.Add(new(targetX, targetY));
        }
        else
        {
            smoothed[^1] = new(targetX, targetY);
        }

        return smoothed;
    }

    private static (int X, int Y) NearestLandCell(GameState state, int x, int y)
    {
        if (IsLand(state, x, y))
        {
            return (x, y);
        }

        for (var radius = 1; radius < 10; radius++)
        {
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if ((Math.Abs(offsetX) != radius && Math.Abs(offsetY) != radius) || !IsLand(state, x + offsetX, y + offsetY))
                    {
                        continue;
                    }

                    return (x + offsetX, y + offsetY);
                }
            }
        }

        return (x, y);
    }

    private static WorldPoint CellCenter(int x, int y) =>
        new((x + .5) * GameConstants.TileSize, (y + .5) * GameConstants.TileSize);

    private static double Hash2(int x, int y)
    {
        var value = unchecked((x + 37) * 374761393 + (y + 91) * 668265263);
        value = unchecked((value ^ (int)((uint)value >> 13)) * 1274126177);
        return (uint)(value ^ (int)((uint)value >> 16)) / 4294967295d;
    }
}
