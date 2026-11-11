using System.Numerics;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Editing;

/// <summary>Which grid axis a work plane is perpendicular to.</summary>
public enum PlaneAxis
{
    X,
    Y,
    Z,
}

/// <summary>Where a ray hit an occupied cell: the cell, and the face it entered through (pointing back at the ray).</summary>
public readonly record struct CellHit(Vector3I Cell, Vector3I Normal, Block Block, float Distance);

/// <summary>
/// Which cells of a grid are taken, and by which block. Used to place blocks: to find the cell under the mouse
/// (by stepping a ray through the cells) and to refuse spots that overlap another block.
/// Cells are in grid units: cell (x,y,z) is centered on (x,y,z) and spans ±0.5 around it.
/// </summary>
public sealed class GridOccupancy
{
    private readonly Dictionary<Vector3I, Block> _cells = new();

    /// <param name="sizeOf">Each block's unrotated size in cells (from its definition).</param>
    public GridOccupancy(CubeGrid grid, Func<Block, Vector3I> sizeOf)
    {
        Grid = grid;
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        foreach (var block in grid.Blocks)
        {
            foreach (var cell in BlockGeometry.OccupiedCells(block.Min, sizeOf(block), block.Orientation))
            {
                _cells.TryAdd(cell, block);
                min = Vector3I.Min(min, cell);
                max = Vector3I.Max(max, cell);
            }
        }
        Min = _cells.Count > 0 ? min : Vector3I.Zero;
        Max = _cells.Count > 0 ? max : Vector3I.Zero;
    }

    public CubeGrid Grid { get; }

    /// <summary>Lowest occupied cell (component-wise).</summary>
    public Vector3I Min { get; }

    /// <summary>Highest occupied cell (component-wise).</summary>
    public Vector3I Max { get; }

    public Block? BlockAt(Vector3I cell) => _cells.TryGetValue(cell, out var b) ? b : null;

    /// <summary>True if a block of this size and rotation at <paramref name="min"/> overlaps nothing.</summary>
    public bool IsFree(Vector3I min, Vector3I size, BlockOrientation orientation) =>
        BlockGeometry.OccupiedCells(min, size, orientation).All(c => !_cells.ContainsKey(c));

    /// <summary>
    /// Steps a ray (in grid cell units) through the cells and returns the first occupied one.
    /// <paramref name="direction"/> need not be normalized; <see cref="CellHit.Distance"/> is in units of it.
    /// </summary>
    public CellHit? Raycast(Vector3 origin, Vector3 direction)
    {
        if (_cells.Count == 0 || direction.LengthSquared() < 1e-12f)
            return null;

        // Clip the ray to the occupied box, so empty space is skipped in one go.
        var boxMin = new Vector3(Min.X - 0.5f, Min.Y - 0.5f, Min.Z - 0.5f);
        var boxMax = new Vector3(Max.X + 0.5f, Max.Y + 0.5f, Max.Z + 0.5f);
        if (!ClipToBox(origin, direction, boxMin, boxMax, out var tEnter, out var tExit, out var enterAxis))
            return null;

        var t = MathF.Max(tEnter, 0f);
        var p = origin + direction * (t + 1e-4f);
        var cell = Vector3I.Min(Vector3I.Max(new Vector3I(Round(p.X), Round(p.Y), Round(p.Z)), Min), Max);
        var step = new Vector3I(MathF.Sign(direction.X), MathF.Sign(direction.Y), MathF.Sign(direction.Z));
        var normal = tEnter > 0f ? -Axis(enterAxis) * Component(step, enterAxis) : -DominantAxis(direction, step);

        // Amanatides–Woo voxel traversal.
        while (true)
        {
            if (_cells.TryGetValue(cell, out var block))
                return new CellHit(cell, normal, block, t);

            var bestAxis = -1;
            var bestT = float.MaxValue;
            for (var axis = 0; axis < 3; axis++)
            {
                var d = Component(direction, axis);
                if (MathF.Abs(d) < 1e-12f)
                    continue;
                var boundary = Component(cell, axis) + 0.5f * MathF.Sign(d);
                var tAxis = (boundary - Component(origin, axis)) / d;
                if (tAxis < bestT)
                {
                    bestT = tAxis;
                    bestAxis = axis;
                }
            }
            if (bestAxis < 0 || bestT > tExit)
                return null;

            var s = Component(step, bestAxis);
            cell += Axis(bestAxis) * s;
            normal = -Axis(bestAxis) * s;
            t = bestT;
            if (Component(cell, bestAxis) < Component(Min, bestAxis) || Component(cell, bestAxis) > Component(Max, bestAxis))
                return null;
        }
    }

    /// <summary>
    /// Where a block goes when placed against a face: the cell next to the face (<paramref name="hit"/>.Cell +
    /// Normal) is covered, the block touches the face, and it's centered on that cell along the other two axes.
    /// </summary>
    public static Vector3I MinAgainstFace(CellHit hit, Vector3I rotatedSize)
    {
        var target = hit.Cell + hit.Normal;
        int Along(int axis)
        {
            var n = Component(hit.Normal, axis);
            var size = Component(rotatedSize, axis);
            var c = Component(target, axis);
            if (n > 0)
                return c;
            if (n < 0)
                return c - (size - 1);
            return c - (size - 1) / 2;
        }
        return new Vector3I(Along(0), Along(1), Along(2));
    }

    /// <summary>
    /// Where a ray (grid cell units) crosses the work plane of layer <paramref name="layer"/>, as the cell it lands
    /// in. The plane is drawn as the "floor" of the layer (its − side, at layer − 0.5), so that's what the ray is
    /// tested against: the cell then matches the square the mouse is over. Null if the ray is parallel to the plane
    /// or points away from it.
    /// </summary>
    public static Vector3I? CellOnPlane(Vector3 origin, Vector3 direction, PlaneAxis axis, int layer)
    {
        var a = (int)axis;
        var d = Component(direction, a);
        if (MathF.Abs(d) < 1e-6f)
            return null;
        var t = (layer - 0.5f - Component(origin, a)) / d;
        if (t <= 0f)
            return null;
        var p = origin + direction * t;
        var cell = new Vector3I(Round(p.X), Round(p.Y), Round(p.Z));
        return a switch
        {
            0 => cell with { X = layer },
            1 => cell with { Y = layer },
            _ => cell with { Z = layer },
        };
    }

    /// <summary>
    /// Where a block goes on a work plane: its lowest layer is the plane's layer (it extends in the + direction),
    /// and it's centered on the cell under the mouse along the plane.
    /// </summary>
    public static Vector3I MinOnPlane(Vector3I cell, PlaneAxis axis, Vector3I rotatedSize)
    {
        var a = (int)axis;
        int Along(int i) => i == a ? Component(cell, i) : Component(cell, i) - (Component(rotatedSize, i) - 1) / 2;
        return new Vector3I(Along(0), Along(1), Along(2));
    }

    private static bool ClipToBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float tEnter, out float tExit, out int enterAxis)
    {
        tEnter = float.MinValue;
        tExit = float.MaxValue;
        enterAxis = 0;
        for (var axis = 0; axis < 3; axis++)
        {
            var oi = Component(o, axis);
            var di = Component(d, axis);
            var lo = Component(min, axis);
            var hi = Component(max, axis);
            if (MathF.Abs(di) < 1e-12f)
            {
                if (oi < lo || oi > hi)
                    return false;
                continue;
            }
            var t1 = (lo - oi) / di;
            var t2 = (hi - oi) / di;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            if (t1 > tEnter)
            {
                tEnter = t1;
                enterAxis = axis;
            }
            tExit = MathF.Min(tExit, t2);
        }
        return tExit >= MathF.Max(tEnter, 0f);
    }

    private static Vector3I DominantAxis(Vector3 d, Vector3I step)
    {
        var ax = MathF.Abs(d.X);
        var ay = MathF.Abs(d.Y);
        var az = MathF.Abs(d.Z);
        return ax >= ay && ax >= az ? new Vector3I(step.X, 0, 0) : ay >= az ? new Vector3I(0, step.Y, 0) : new Vector3I(0, 0, step.Z);
    }

    private static int Round(float v) => (int)MathF.Round(v, MidpointRounding.AwayFromZero);

    private static Vector3I Axis(int axis) => axis switch { 0 => new(1, 0, 0), 1 => new(0, 1, 0), _ => new(0, 0, 1) };

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static int Component(Vector3I v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
