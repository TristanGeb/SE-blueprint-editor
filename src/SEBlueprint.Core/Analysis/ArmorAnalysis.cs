using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Analysis;

/// <summary>What an <see cref="ArmorAnalysis"/> measured.</summary>
public enum ArmorAnalysisKind
{
    /// <summary>
    /// For structural blocks on the outside of the ship: hit points of armor directly behind that spot,
    /// measured straight inward until empty space or a functional block. Shows thin spots in the hull.
    /// </summary>
    Thickness,

    /// <summary>
    /// For functional blocks (reactors, cockpits...): hit points that must be shot through to reach the block
    /// from its weakest direction (26 directions checked). Shows exposed components.
    /// </summary>
    Exposure,
}

/// <summary>One measured block.</summary>
/// <param name="Block">The block.</param>
/// <param name="HitPoints">Armor/shielding hit points (see <see cref="ArmorAnalysisKind"/>).</param>
/// <param name="WeakestDirection">For exposure: the direction (grid space) the block is easiest to reach from.</param>
public sealed record ArmorMeasurement(Block Block, float HitPoints, Vector3I WeakestDirection);

public sealed class ArmorAnalysisResult
{
    public required ArmorAnalysisKind Kind { get; init; }

    /// <summary>Measured blocks; blocks not measured (e.g. interior armor in a thickness analysis) are absent.</summary>
    public required IReadOnlyDictionary<Block, ArmorMeasurement> Measurements { get; init; }

    /// <summary>Measured blocks from weakest to strongest.</summary>
    public IEnumerable<ArmorMeasurement> Weakest => Measurements.Values.OrderBy(m => m.HitPoints);
}

/// <summary>
/// Estimates how well a ship is armored by ray-marching through its grid cells.
///
/// Damage in Space Engineers has to destroy blocks one by one to get through, so the protection along a path is
/// the sum of the hit points of the blocks it passes. Each cell of a multi-cell block counts its share
/// (block HP ÷ number of cells). This ignores damage types, deformation and projectile penetration rules, so treat
/// numbers as a relative guide: "this spot is ~6× weaker than that one".
/// </summary>
public static class ArmorAnalysis
{
    /// <summary>HP assumed for blocks whose definition is unknown (mod blocks).</summary>
    public const float UnknownBlockHitPoints = 1000f;

    /// <summary>Longest inward path measured for thickness, in cells. Deeper armor than this counts as "very thick".</summary>
    public const int MaxThicknessDepth = 32;

    private static readonly Vector3I[] FaceDirections =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>All 26 directions to a neighbouring cell: 6 faces, 12 edges, 8 corners.</summary>
    private static readonly Vector3I[] AllDirections =
        (from x in new[] { -1, 0, 1 }
         from y in new[] { -1, 0, 1 }
         from z in new[] { -1, 0, 1 }
         where x != 0 || y != 0 || z != 0
         select new Vector3I(x, y, z)).ToArray();

    public static ArmorAnalysisResult Analyze(Blueprint blueprint, DefinitionRepository definitions, ArmorAnalysisKind kind)
    {
        var all = new Dictionary<Block, ArmorMeasurement>();
        foreach (var grid in blueprint.Grids)
        {
            var occupancy = GridOccupancy.Build(grid, definitions);
            var measurements = kind == ArmorAnalysisKind.Thickness
                ? MeasureThickness(occupancy)
                : MeasureExposure(occupancy);
            foreach (var m in measurements)
                all[m.Block] = m;
        }
        return new ArmorAnalysisResult { Kind = kind, Measurements = all };
    }

    private static IEnumerable<ArmorMeasurement> MeasureThickness(GridOccupancy grid)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<ArmorMeasurement>();
        Parallel.For(0, grid.Blocks.Count, i =>
        {
            var info = grid.Blocks[i];
            if (!info.Structural)
                return;

            float? weakest = null;
            var weakestDirection = Vector3I.Zero;
            foreach (var cell in info.Cells)
            {
                foreach (var outward in FaceDirections)
                {
                    if (grid.IndexAt(cell + outward) >= 0)
                        continue; // that face is covered by another block, not exposed to space

                    // Walk inward through structural blocks until empty space or a functional block.
                    float hp = 0;
                    var p = cell;
                    for (var step = 0; step < MaxThicknessDepth; step++)
                    {
                        var index = grid.IndexAt(p);
                        if (index < 0 || !grid.Blocks[index].Structural)
                            break;
                        hp += grid.Blocks[index].HitPointsPerCell;
                        p -= outward;
                    }
                    if (weakest is null || hp < weakest)
                    {
                        weakest = hp;
                        weakestDirection = outward;
                    }
                }
            }

            if (weakest is { } value)
                results.Add(new ArmorMeasurement(info.Block, value, weakestDirection));
        });
        return results;
    }

    private static IEnumerable<ArmorMeasurement> MeasureExposure(GridOccupancy grid)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<ArmorMeasurement>();
        Parallel.For(0, grid.Blocks.Count, i =>
        {
            var info = grid.Blocks[i];
            if (info.Structural)
                return;

            var weakest = float.MaxValue;
            var weakestDirection = Vector3I.Zero;
            foreach (var direction in AllDirections)
            {
                var isDiagonal = System.Math.Abs(direction.X) + System.Math.Abs(direction.Y) + System.Math.Abs(direction.Z) > 1;
                foreach (var cell in info.Cells)
                {
                    // Only start from the block's outer cells on this side; inner cells are always better shielded.
                    if (grid.IndexAt(cell + direction) == i)
                        continue;

                    // Sum the HP of every other block between this cell and the edge of the grid.
                    float hp = 0;
                    var from = cell;
                    var p = cell + direction;
                    while ((grid.Contains(p) || grid.Contains(from)) && hp < weakest)
                    {
                        hp += CellHitPoints(grid, p, i);
                        if (isDiagonal)
                            hp += CrossingHitPoints(grid, from, direction, i);
                        from = p;
                        p += direction;
                    }
                    if (hp < weakest)
                    {
                        weakest = hp;
                        weakestDirection = direction;
                    }
                }
            }

            results.Add(new ArmorMeasurement(info.Block, weakest == float.MaxValue ? 0 : weakest, weakestDirection));
        });
        return results;
    }

    private static float CellHitPoints(GridOccupancy grid, Vector3I p, int self)
    {
        var index = grid.IndexAt(p);
        return index >= 0 && index != self ? grid.Blocks[index].HitPointsPerCell : 0f;
    }

    /// <summary>
    /// A diagonal step from one cell to the next passes exactly through the edge (or corner) where the cells
    /// "in between" meet, e.g. stepping (1,1,0) passes between the cells at +X and +Y. Without this, diagonal rays
    /// slipped through hulls made of blocks that only touch along an edge. A real shot passing there hits one of
    /// those in-between blocks, so the cheapest one is counted; if any of them is empty, the shot can squeeze
    /// through that gap and nothing is added.
    /// </summary>
    private static float CrossingHitPoints(GridOccupancy grid, Vector3I from, Vector3I d, int self)
    {
        // The direction's non-zero axes, e.g. (1,0,-1) → [(1,0,0), (0,0,-1)].
        Span<Vector3I> axes = stackalloc Vector3I[3];
        var axisCount = 0;
        if (d.X != 0) axes[axisCount++] = new Vector3I(d.X, 0, 0);
        if (d.Y != 0) axes[axisCount++] = new Vector3I(0, d.Y, 0);
        if (d.Z != 0) axes[axisCount++] = new Vector3I(0, 0, d.Z);

        var cheapest = float.MaxValue;
        // Every partial step: each non-empty proper subset of those axes (2 for an edge diagonal, 6 for a corner).
        var full = (1 << axisCount) - 1;
        for (var mask = 1; mask < full; mask++)
        {
            var partial = Vector3I.Zero;
            for (var a = 0; a < axisCount; a++)
            {
                if ((mask & (1 << a)) != 0)
                    partial += axes[a];
            }
            var index = grid.IndexAt(from + partial);
            if (index < 0 || index == self)
                return 0f; // a gap: the shot can pass here
            cheapest = System.Math.Min(cheapest, grid.Blocks[index].HitPointsPerCell);
        }
        return cheapest == float.MaxValue ? 0f : cheapest;
    }

    /// <summary>Which block occupies each cell of a grid, stored densely for fast ray marching.</summary>
    private sealed class GridOccupancy
    {
        private readonly int[] _cells;
        private readonly Vector3I _min;
        private readonly Vector3I _size;

        private GridOccupancy(List<BlockInfo> blocks, int[] cells, Vector3I min, Vector3I size)
        {
            Blocks = blocks;
            _cells = cells;
            _min = min;
            _size = size;
        }

        public List<BlockInfo> Blocks { get; }

        public bool Contains(Vector3I p)
        {
            var x = p.X - _min.X;
            var y = p.Y - _min.Y;
            var z = p.Z - _min.Z;
            return (uint)x < (uint)_size.X && (uint)y < (uint)_size.Y && (uint)z < (uint)_size.Z;
        }

        /// <summary>Index into <see cref="Blocks"/> of the block at a cell, or -1 for empty space.</summary>
        public int IndexAt(Vector3I p) =>
            Contains(p) ? _cells[((p.X - _min.X) * _size.Y + (p.Y - _min.Y)) * _size.Z + (p.Z - _min.Z)] : -1;

        public static GridOccupancy Build(CubeGrid grid, DefinitionRepository definitions)
        {
            var blocks = new List<BlockInfo>(grid.Blocks.Count);
            var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
            foreach (var block in grid.Blocks)
            {
                var def = definitions.Get(block);
                var cells = BlockGeometry.OccupiedCells(block.Min, def.Size, block.Orientation).ToArray();
                var hp = def.IsUnknown || def.EffectiveHitPoints <= 0 ? UnknownBlockHitPoints : def.EffectiveHitPoints;
                blocks.Add(new BlockInfo(block, cells, hp / cells.Length, def.IsStructural));
                foreach (var c in cells)
                {
                    min = Vector3I.Min(min, c);
                    max = Vector3I.Max(max, c);
                }
            }

            if (blocks.Count == 0)
                return new GridOccupancy(blocks, Array.Empty<int>(), Vector3I.Zero, Vector3I.Zero);

            var size = max - min + Vector3I.One;
            var cellsArray = new int[checked(size.X * size.Y * size.Z)];
            Array.Fill(cellsArray, -1);
            var occupancy = new GridOccupancy(blocks, cellsArray, min, size);
            for (var i = 0; i < blocks.Count; i++)
            {
                foreach (var c in blocks[i].Cells)
                    cellsArray[((c.X - min.X) * size.Y + (c.Y - min.Y)) * size.Z + (c.Z - min.Z)] = i;
            }
            return occupancy;
        }
    }

    private sealed record BlockInfo(Block Block, Vector3I[] Cells, float HitPointsPerCell, bool Structural);
}
