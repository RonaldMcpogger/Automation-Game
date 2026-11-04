using System.Collections.Generic;
using UnityEngine;

/// <summary>Deterministic placement on low, gently sloped ground connected to the clearing.</summary>
public static class MapResourcePlanner
{
    public struct Placement
    {
        public Vector3 position;
        public float yaw;
        public float scale;
    }

    public static List<Placement> Generate(MapGenerationSettings settings, TerrainData terrain)
    {
        settings.Validate();
        var result = new List<Placement>();
        if (settings.resourceSpotCount == 0)
            return result;
        var random = new System.Random(unchecked(settings.seed ^ 0x32b71ac5));
        var reachable = new ReachableGround(settings, terrain);
        Add(settings.starterResourcePosition);

        int attempts = settings.resourceSpotCount * 200;
        for (int attempt = 0; attempt < attempts && result.Count < settings.resourceSpotCount; attempt++)
        {
            float margin = settings.resourceEdgeMargin;
            var point = new Vector2(settings.Origin.x + margin + (float)random.NextDouble() * (settings.mapSize.x - margin * 2f),
                settings.Origin.z + margin + (float)random.NextDouble() * (settings.mapSize.y - margin * 2f));
            // Only the starter deposit enters the reserved building area.
            if (settings.DistanceFromClearing(point) < settings.clearingBlend + 3f || !reachable.Contains(point))
                continue;
            if (!IsSuitable(settings, terrain, point))
                continue;
            bool separated = true;
            foreach (var placed in result)
                if (Vector2.Distance(point, new Vector2(placed.position.x, placed.position.z)) < settings.resourceSpacing)
                {
                    separated = false;
                    break;
                }
            if (separated)
                Add(point);
        }
        return result;

        void Add(Vector2 point)
        {
            result.Add(new Placement
            {
                position = new Vector3(point.x, Height(settings, terrain, point), point.y),
                yaw = (float)random.NextDouble() * 360f,
                scale = Mathf.Lerp(0.9f, 1.2f, (float)random.NextDouble())
            });
        }
    }

    public static bool IsSuitable(MapGenerationSettings settings, TerrainData terrain, Vector2 point)
    {
        Vector2 uv = UV(settings, point);
        float height = Height(settings, terrain, point);
        if (terrain.GetSteepness(uv.x, uv.y) > settings.resourceMaxSlope || height > settings.resourceMaxElevation)
            return false;
        // Check the footprint, not just a single point, to avoid straddling a sharp slope.
        for (int z = -1; z <= 1; z++)
        for (int x = -1; x <= 1; x++)
            if (Mathf.Abs(Height(settings, terrain, point + new Vector2(x * 2f, z * 2f)) - height) > 0.65f)
                return false;
        return true;
    }

    private static Vector2 UV(MapGenerationSettings settings, Vector2 point) => new Vector2(
        (point.x - settings.Origin.x) / settings.mapSize.x, (point.y - settings.Origin.z) / settings.mapSize.y);

    private static float Height(MapGenerationSettings settings, TerrainData terrain, Vector2 point)
    {
        Vector2 uv = UV(settings, point);
        return terrain.GetInterpolatedHeight(uv.x, uv.y) + settings.Origin.y;
    }

    private sealed class ReachableGround
    {
        private readonly bool[,] reached;
        private readonly MapGenerationSettings settings;
        private readonly int width, depth;
        private readonly float stepX, stepZ;

        public ReachableGround(MapGenerationSettings settings, TerrainData terrain)
        {
            this.settings = settings;
            width = Mathf.CeilToInt(settings.mapSize.x / 8f) + 1;
            depth = Mathf.CeilToInt(settings.mapSize.y / 8f) + 1;
            stepX = settings.mapSize.x / (width - 1);
            stepZ = settings.mapSize.y / (depth - 1);
            reached = new bool[depth, width];
            var passable = new bool[depth, width];
            var heights = new float[depth, width];
            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                passable[z, x] = terrain.GetSteepness(x / (width - 1f), z / (depth - 1f)) <= 35f;
                heights[z, x] = terrain.GetInterpolatedHeight(x / (width - 1f), z / (depth - 1f));
            }
            var start = Cell(settings.clearingCenter);
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            reached[start.y, start.x] = true;
            var directions = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var direction in directions)
                {
                    var next = cell + direction;
                    if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= depth
                        || reached[next.y, next.x] || !passable[next.y, next.x])
                        continue;
                    float step = direction.x == 0 ? stepZ : stepX;
                    if (Mathf.Abs(heights[next.y, next.x] - heights[cell.y, cell.x]) / step > 0.7f)
                        continue;
                    float midX = (next.x + cell.x) * 0.5f / (width - 1);
                    float midZ = (next.y + cell.y) * 0.5f / (depth - 1);
                    if (terrain.GetSteepness(midX, midZ) > 35f)
                        continue;
                    reached[next.y, next.x] = true;
                    queue.Enqueue(next);
                }
            }
        }

        public bool Contains(Vector2 point)
        {
            var cell = Cell(point);
            return reached[cell.y, cell.x];
        }

        private Vector2Int Cell(Vector2 point) => new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt((point.x - settings.Origin.x) / stepX), 0, width - 1),
            Mathf.Clamp(Mathf.RoundToInt((point.y - settings.Origin.z) / stepZ), 0, depth - 1));
    }
}
