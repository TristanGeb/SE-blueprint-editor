using System.Numerics;
using SEBlueprint.Core.Colors;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Rendering;

/// <summary>
/// Everything needed to draw one grid, as plain arrays: one box per block.
/// Built on a background thread (no WPF/GPU objects in here), then turned into a 3D model on the UI thread.
/// Index i in every array refers to the same block.
/// </summary>
public sealed class GridSceneData
{
    public required CubeGrid Grid { get; init; }

    /// <summary>The blocks, in the same order as the instance arrays.</summary>
    public required Block[] Blocks { get; init; }

    /// <summary>Per-block transform of a unit cube (scale to block size, then move into place).</summary>
    public required Matrix4x4[] Transforms { get; init; }

    /// <summary>Per-block color as RGB 0..1.</summary>
    public required Vector3[] Colors { get; init; }

    /// <summary>Transform from this grid's local space to the scene (main grid's space).</summary>
    public required Matrix4x4 GridToScene { get; init; }

    private Dictionary<Block, int>? _indexByBlock;

    /// <summary>Reverse lookup: which instance index a block is drawn with, or -1.</summary>
    public int IndexOf(Block block)
    {
        if (_indexByBlock is null)
        {
            _indexByBlock = new Dictionary<Block, int>(Blocks.Length);
            for (var i = 0; i < Blocks.Length; i++)
                _indexByBlock[Blocks[i]] = i;
        }
        return _indexByBlock.TryGetValue(block, out var index) ? index : -1;
    }
}

public sealed class BlueprintSceneData
{
    public required Blueprint Blueprint { get; init; }

    public required IReadOnlyList<GridSceneData> Grids { get; init; }
}

public static class SceneBuilder
{
    /// <summary>Gap between neighboring blocks as a fraction of a cell, so individual blocks are visible.</summary>
    public const float GapFraction = 0.06f;

    /// <summary>Color used for blocks whose definition is missing (e.g. mod blocks), so they stand out.</summary>
    public static readonly Vector3 UnknownBlockColor = new(1f, 0f, 1f);

    /// <summary>
    /// Builds draw data for every grid. The grid with the most blocks becomes the "main" grid: the scene is
    /// centered on it and aligned to its axes, and subgrids (rotors, pistons) are placed relative to it.
    /// </summary>
    public static BlueprintSceneData Build(Blueprint blueprint, DefinitionRepository definitions)
    {
        var main = blueprint.Grids.OrderByDescending(g => g.Blocks.Count).FirstOrDefault();
        var mainPlacement = main?.Placement ?? GridPlacement.Identity;
        // Rotation matrices are orthonormal, so the inverse is the transpose.
        var sceneFromMainRotation = Matrix4x4.Transpose(mainPlacement.Rotation);

        var grids = new List<GridSceneData>(blueprint.Grids.Count);
        foreach (var grid in blueprint.Grids)
        {
            var placement = grid.Placement;
            var offset = (placement.Position - mainPlacement.Position).ToVector3();
            var gridToScene = placement.Rotation * Matrix4x4.CreateTranslation(offset) * sceneFromMainRotation;
            grids.Add(BuildGrid(grid, definitions, gridToScene));
        }

        return new BlueprintSceneData { Blueprint = blueprint, Grids = grids };
    }

    public static GridSceneData BuildGrid(CubeGrid grid, DefinitionRepository definitions, Matrix4x4 gridToScene)
    {
        var count = grid.Blocks.Count;
        var blocks = grid.Blocks.ToArray();
        var transforms = new Matrix4x4[count];
        var colors = new Vector3[count];
        var gap = grid.CellSize * GapFraction;

        for (var i = 0; i < count; i++)
        {
            var block = blocks[i];
            var def = definitions.Get(block);
            transforms[i] = BlockTransform(block, def, gap) * gridToScene;
            colors[i] = def.IsUnknown ? UnknownBlockColor : SeColor.MaskToRgb(block.ColorMaskHsv);
        }

        return new GridSceneData
        {
            Grid = grid,
            Blocks = blocks,
            Transforms = transforms,
            Colors = colors,
            GridToScene = gridToScene,
        };
    }

    /// <summary>Unit cube (centered on the origin) to the block's box in grid space.</summary>
    public static Matrix4x4 BlockTransform(Block block, BlockDefinition definition, float gap = 0f)
    {
        var (center, extents) = BlockGeometry.LocalBox(block, definition);
        var size = Vector3.Max(extents - new Vector3(gap), new Vector3(gap));
        return Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center);
    }
}
