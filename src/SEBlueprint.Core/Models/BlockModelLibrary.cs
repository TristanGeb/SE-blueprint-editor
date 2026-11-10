using System.Collections.Concurrent;
using SEBlueprint.Core.Definitions;

namespace SEBlueprint.Core.Models;

/// <summary>How much model detail to load. Lower levels draw big ships much faster.</summary>
public enum ModelDetail
{
    /// <summary>Plain boxes, like before real models were supported.</summary>
    Boxes,

    /// <summary>LOD3, the game's lowest-detail model.</summary>
    Low,

    /// <summary>LOD2 — the default; good shapes at a fraction of the triangles.</summary>
    Medium,

    /// <summary>LOD1.</summary>
    High,

    /// <summary>LOD0, full detail. Can be slow on large ships.</summary>
    Full,
}

/// <summary>One drawable part of a block's look: a mesh plus how it's colored.</summary>
public sealed record BlockVisualPart(MeshData Mesh, PartAppearance Appearance);

/// <summary>
/// What a block type looks like: its parts, and whether the meshes are in meters (real models)
/// or in a unit block that must be scaled to the block's size (armor shapes and boxes).
/// </summary>
public sealed class BlockVisual
{
    public required string Key { get; init; }

    public required IReadOnlyList<BlockVisualPart> Parts { get; init; }

    /// <summary>True for unit-block meshes (armor shapes, boxes) that are scaled to the block's size.</summary>
    public required bool IsUnitShape { get; init; }

    public static BlockVisual Box { get; } = new()
    {
        Key = "box",
        Parts = new[] { new BlockVisualPart(MeshData.UnitCube, PartAppearance.Paint) },
        IsUnitShape = true,
    };
}

/// <summary>
/// Loads and caches block visuals from the game's model files. Thread-safe, so scenes can be built
/// on a background thread. Models that fail to load fall back to a box.
/// </summary>
public sealed class BlockModelLibrary
{
    private readonly string? _contentFolder;
    private readonly ConcurrentDictionary<string, BlockVisual> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="contentFolder">The game's Content folder, or null to only use boxes and armor shapes.</param>
    public BlockModelLibrary(string? contentFolder, ModelDetail detail)
    {
        _contentFolder = contentFolder;
        Detail = detail;
    }

    public ModelDetail Detail { get; }

    private int _failedModels;

    /// <summary>Number of models that failed to load (they are drawn as boxes).</summary>
    public int FailedModels => _failedModels;

    public BlockVisual Get(BlockDefinition definition)
    {
        if (Detail == ModelDetail.Boxes || definition.IsUnknown)
            return BlockVisual.Box;

        if (definition.ModelPath is { } model && _contentFolder is not null)
            return _cache.GetOrAdd("model:" + model, _ => LoadModel(model));

        if (ArmorShapes.Get(definition.CubeTopology) is { } shape)
        {
            return _cache.GetOrAdd("shape:" + definition.CubeTopology, _ => new BlockVisual
            {
                Key = "shape:" + definition.CubeTopology,
                Parts = new[] { new BlockVisualPart(shape, PartAppearance.Paint) },
                IsUnitShape = true,
            });
        }

        return BlockVisual.Box;
    }

    private BlockVisual LoadModel(string relativePath)
    {
        try
        {
            var main = MwmReader.Read(Resolve(relativePath));
            var geometry = PickLod(main);

            // Merge parts that look the same so each block type needs as few draw calls as possible.
            var parts = new List<BlockVisualPart>();
            foreach (var group in geometry.Parts
                         .Select(p => (Part: p, Look: PartAppearance.FromMaterial(p.MaterialName, p.Technique)))
                         .Where(x => x.Look is not null && x.Part.Indices.Length >= 3)
                         .GroupBy(x => x.Look!.Value))
            {
                var indices = group.SelectMany(x => x.Part.Indices).ToList();
                // The game stores triangles clockwise (DirectX style); we need counter-clockwise.
                // Checked across all ~1300 vanilla models: ~90% are inside-out without this flip.
                if (!geometry.SwapWindingOrder)
                    FlipWinding(indices);
                var mesh = MeshData.FromTriangles(geometry.Positions, indices);
                if (mesh.TriangleCount > 0)
                    parts.Add(new BlockVisualPart(mesh, group.Key));
            }

            if (parts.Count == 0)
                return BlockVisual.Box;
            return new BlockVisual { Key = "model:" + relativePath, Parts = parts, IsUnitShape = false };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or EndOfStreamException or FormatException or ArgumentException or UnauthorizedAccessException)
        {
            Interlocked.Increment(ref _failedModels);
            return BlockVisual.Box;
        }
    }

    /// <summary>Reads the geometry for the chosen detail level, falling back to more detail if a level is missing.</summary>
    private MwmModel PickLod(MwmModel main)
    {
        // Lods[0] is LOD1, Lods[1] is LOD2, Lods[2] is LOD3. Full detail is the geometry asset (or the file itself).
        var wanted = Detail switch
        {
            ModelDetail.Low => 2,
            ModelDetail.Medium => 1,
            ModelDetail.High => 0,
            _ => -1,
        };
        for (var i = System.Math.Min(wanted, main.Lods.Count - 1); i >= 0; i--)
        {
            var path = Resolve(main.Lods[i].Model);
            if (!File.Exists(path))
                continue;
            var lod = MwmReader.Read(path);
            if (lod.HasGeometry)
                return lod;
        }

        if (main.GeometryAsset is { } asset)
            return MwmReader.Read(Resolve(asset));
        return main;
    }

    private string Resolve(string relativePath)
    {
        var path = Path.Combine(_contentFolder!, relativePath.TrimStart('\\', '/'));
        return path.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase) ? path : path + ".mwm";
    }

    private static void FlipWinding(List<int> indices)
    {
        for (var i = 0; i + 2 < indices.Count; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
    }
}
