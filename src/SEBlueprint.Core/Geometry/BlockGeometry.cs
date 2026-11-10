using System.Numerics;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Geometry;

/// <summary>
/// Where a block physically sits in its grid: which cells it covers and its box in meters.
/// </summary>
public static class BlockGeometry
{
    /// <summary>
    /// The block's size in cells after rotation. A 1x1x3 block lying on its side covers 3x1x1 cells.
    /// </summary>
    public static Vector3I RotatedSize(Vector3I size, BlockOrientation orientation) =>
        orientation.Transform(size).Abs();

    /// <summary>The highest cell the block occupies (inclusive). <see cref="Block.Min"/> is the lowest.</summary>
    public static Vector3I Max(Vector3I min, Vector3I size, BlockOrientation orientation) =>
        min + RotatedSize(size, orientation) - Vector3I.One;

    /// <summary>Every grid cell the block covers.</summary>
    public static IEnumerable<Vector3I> OccupiedCells(Vector3I min, Vector3I size, BlockOrientation orientation)
    {
        var max = Max(min, size, orientation);
        for (var x = min.X; x <= max.X; x++)
            for (var y = min.Y; y <= max.Y; y++)
                for (var z = min.Z; z <= max.Z; z++)
                    yield return new Vector3I(x, y, z);
    }

    /// <summary>
    /// The block's box in the grid's local space, in meters. Cell (0,0,0) is centered on the grid origin,
    /// so a cell spans [-cellSize/2, +cellSize/2] around cell * cellSize.
    /// </summary>
    public static (Vector3 Center, Vector3 Extents) LocalBox(Block block, BlockDefinition definition)
    {
        var min = block.Min;
        var max = Max(min, definition.Size, block.Orientation);
        var cell = block.Grid.CellSize;
        var center = new Vector3(min.X + max.X, min.Y + max.Y, min.Z + max.Z) * (cell / 2f);
        var extents = new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * cell;
        return (center, extents);
    }
}
