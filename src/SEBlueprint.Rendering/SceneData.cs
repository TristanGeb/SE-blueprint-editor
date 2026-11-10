using System.Numerics;
using SEBlueprint.Core.Colors;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.Model;
using SEBlueprint.Core.Models;

namespace SEBlueprint.Rendering;

/// <summary>
/// Everything needed to draw one grid, as plain arrays (no WPF/GPU objects), so it can be built on a
/// background thread. Blocks are grouped into <see cref="Batches"/>: one per distinct mesh, each drawn
/// with GPU instancing (one draw call for e.g. all 2,000 light armor slopes).
/// </summary>
public sealed class GridSceneData
{
    public required CubeGrid Grid { get; init; }

    public required Block[] Blocks { get; init; }

    /// <summary>Per block (same index as <see cref="Blocks"/>): unit cube → the block's bounding box. Used for selection outlines.</summary>
    public required Matrix4x4[] BoxTransforms { get; init; }

    public required IReadOnlyList<SceneBatch> Batches { get; init; }

    /// <summary>Transform from this grid's local space to the scene (main grid's space).</summary>
    public required Matrix4x4 GridToScene { get; init; }

    private Dictionary<Block, int>? _indexByBlock;

    /// <summary>Reverse lookup: the block's index in <see cref="Blocks"/>, or -1.</summary>
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

/// <summary>Many blocks that share one mesh, drawn in one go.</summary>
public sealed class SceneBatch
{
    public required BlockVisualPart Part { get; init; }

    /// <summary>Index into <see cref="GridSceneData.Blocks"/> for each instance.</summary>
    public required int[] BlockIndices { get; init; }

    public required Matrix4x4[] Transforms { get; init; }

    /// <summary>RGBA 0..1 per instance.</summary>
    public required Vector4[] Colors { get; init; }
}

public sealed class BlueprintSceneData
{
    public required Blueprint Blueprint { get; init; }

    public required IReadOnlyList<GridSceneData> Grids { get; init; }
}

public static class SceneBuilder
{
    /// <summary>Gap between armor blocks as a fraction of a cell, standing in for the game's armor edges.</summary>
    public const float GapFraction = 0.03f;

    /// <summary>Color for blocks whose definition is missing (e.g. mod blocks), so they stand out.</summary>
    public static readonly Vector3 UnknownBlockColor = new(1f, 0f, 1f);

    public const float GlassAlpha = 0.35f;

    /// <summary>
    /// Builds draw data for every grid. The grid with the most blocks becomes the "main" grid: the scene is
    /// centered on it and aligned to its axes, and subgrids (rotors, pistons) are placed relative to it.
    /// </summary>
    public static BlueprintSceneData Build(Blueprint blueprint, DefinitionRepository definitions, BlockModelLibrary models)
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
            grids.Add(BuildGrid(grid, definitions, models, gridToScene));
        }

        return new BlueprintSceneData { Blueprint = blueprint, Grids = grids };
    }

    public static GridSceneData BuildGrid(CubeGrid grid, DefinitionRepository definitions, BlockModelLibrary models, Matrix4x4 gridToScene)
    {
        var blocks = grid.Blocks.ToArray();
        var boxTransforms = new Matrix4x4[blocks.Length];
        var batches = new Dictionary<BlockVisualPart, BatchBuilder>();
        var gap = grid.CellSize * GapFraction;

        for (var i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            var def = definitions.Get(block);
            boxTransforms[i] = BoxTransform(block, def) * gridToScene;

            var visual = models.Get(def);
            var transform = VisualTransform(block, def, visual, gap) * gridToScene;
            var paint = def.IsUnknown ? UnknownBlockColor : SeColor.MaskToRgb(block.ColorMaskHsv);

            foreach (var part in visual.Parts)
            {
                if (!batches.TryGetValue(part, out var batch))
                    batches[part] = batch = new BatchBuilder();
                batch.Indices.Add(i);
                batch.Transforms.Add(transform);
                batch.Colors.Add(ColorFor(part.Appearance, paint));
            }
        }

        return new GridSceneData
        {
            Grid = grid,
            Blocks = blocks,
            BoxTransforms = boxTransforms,
            GridToScene = gridToScene,
            Batches = batches.Select(kv => new SceneBatch
            {
                Part = kv.Key,
                BlockIndices = kv.Value.Indices.ToArray(),
                Transforms = kv.Value.Transforms.ToArray(),
                Colors = kv.Value.Colors.ToArray(),
            }).ToList(),
        };
    }

    /// <summary>The block's rotation as a matrix (row-vector convention): rows are where its Right, Up and Backward point.</summary>
    public static Matrix4x4 RotationOf(BlockOrientation o)
    {
        var r = o.RightVector;
        var u = o.UpVector;
        var b = o.BackwardVector;
        return new Matrix4x4(
            r.X, r.Y, r.Z, 0,
            u.X, u.Y, u.Z, 0,
            b.X, b.Y, b.Z, 0,
            0, 0, 0, 1);
    }

    /// <summary>Unit cube (centered on the origin) to the block's bounding box in grid space.</summary>
    public static Matrix4x4 BoxTransform(Block block, BlockDefinition definition)
    {
        var (center, extents) = BlockGeometry.LocalBox(block, definition);
        return Matrix4x4.CreateScale(extents) * Matrix4x4.CreateTranslation(center);
    }

    /// <summary>
    /// Model space to grid space. Real models are in meters and centered on the block; unit shapes
    /// (armor, boxes) are scaled to the block's size first. Then rotate, then move to the block's center.
    /// </summary>
    public static Matrix4x4 VisualTransform(Block block, BlockDefinition definition, BlockVisual visual, float gap)
    {
        var (center, _) = BlockGeometry.LocalBox(block, definition);
        var rotation = RotationOf(block.Orientation);

        if (visual.IsUnitShape)
        {
            var size = new Vector3(definition.Size.X, definition.Size.Y, definition.Size.Z) * block.Grid.CellSize;
            size = Vector3.Max(size - new Vector3(gap), new Vector3(gap));
            return Matrix4x4.CreateScale(size) * rotation * Matrix4x4.CreateTranslation(center);
        }

        var offset = Vector3.Transform(definition.ModelOffset, rotation);
        return rotation * Matrix4x4.CreateTranslation(center + offset);
    }

    private static Vector4 ColorFor(PartAppearance look, Vector3 paint) => look.Kind switch
    {
        PartKind.Paint => new Vector4(paint, 1f),
        PartKind.Glass => new Vector4(look.Color, GlassAlpha),
        _ => new Vector4(look.Color, 1f),
    };

    private sealed class BatchBuilder
    {
        public List<int> Indices { get; } = new();
        public List<Matrix4x4> Transforms { get; } = new();
        public List<Vector4> Colors { get; } = new();
    }
}
