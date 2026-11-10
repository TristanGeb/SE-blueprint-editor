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

/// <summary>One drawable part of a block's look: a mesh, how it's colored, and (optionally) its textures.</summary>
public sealed record BlockVisualPart(MeshData Mesh, PartAppearance Appearance, MaterialTextures? Textures = null)
{
    /// <summary>True if this part can be drawn with its texture.</summary>
    public bool IsTextured => Textures is not null && Mesh.TexCoords is not null;
}

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

    /// <summary>
    /// Armor textures hold a grid of pattern variants (columns × rows); each block shows one cell, picked from
    /// its position so neighbouring blocks differ, like in the game. (0, 0) for textures without variants.
    /// </summary>
    public (int Columns, int Rows) PatternCells { get; init; }

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
    /// <param name="useTextures">Whether to draw the game's textures (off = flat colors only).</param>
    public BlockModelLibrary(string? contentFolder, ModelDetail detail, bool useTextures = true)
    {
        _contentFolder = contentFolder;
        Detail = detail;
        UseTextures = useTextures && contentFolder is not null;
    }

    public ModelDetail Detail { get; }

    public bool UseTextures { get; }

    /// <summary>The game's Content folder (texture paths are relative to it), or null if the game wasn't found.</summary>
    public string? ContentFolder => _contentFolder;

    /// <summary>
    /// Size of one armor pattern cell in texture coordinates. Read from the game's armor tiles: a full face of
    /// SquarePlate / HeavySquarePlate covers u 0..0.25, v 0..0.5, i.e. the texture is a 4 × 2 grid of variants.
    /// </summary>
    private static readonly System.Numerics.Vector2 ArmorCell = new(0.25f, 0.5f);

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
            // Light and heavy armor share shapes but not textures, so the tile model is part of the key.
            var key = $"shape:{definition.CubeTopology}|{definition.ArmorTileModel}";
            return _cache.GetOrAdd(key, _ =>
            {
                var textures = ArmorTextures(definition.ArmorTileModel);
                return new BlockVisual
                {
                    Key = key,
                    Parts = new[]
                    {
                        new BlockVisualPart(textures is null ? shape : shape.WithTexCoordScale(ArmorCell), PartAppearance.Paint, textures),
                    },
                    IsUnitShape = true,
                    PatternCells = textures is null ? (0, 0) : (4, 2),
                };
            });
        }

        return BlockVisual.Box;
    }

    /// <summary>The textures of an armor face tile (e.g. light vs heavy armor), or null if unavailable.</summary>
    private MaterialTextures? ArmorTextures(string? tileModel)
    {
        if (tileModel is null || _contentFolder is null)
            return null;
        try
        {
            var main = MwmReader.Read(Resolve(tileModel));
            var geometry = main.GeometryAsset is { } asset ? MwmReader.Read(Resolve(asset)) : main;
            return geometry.Parts.Select(MaterialTextures.FromPart).FirstOrDefault(t => t is not null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or EndOfStreamException or FormatException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>How deep subparts may nest (turret base → head → barrels is 3).</summary>
    private const int MaxSubpartDepth = 5;

    private BlockVisual LoadModel(string relativePath)
    {
        try
        {
            // Collect the triangles of the model and all its subparts, grouped by look + texture, so each
            // block type needs as few draw calls as possible.
            var groups = new Dictionary<(PartAppearance Look, MaterialTextures? Textures), GeometryBuilder>();
            AddModel(relativePath, System.Numerics.Matrix4x4.Identity, depth: 0, groups, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var parts = new List<BlockVisualPart>();
            foreach (var (key, builder) in groups)
            {
                var mesh = MeshData.FromTriangles(builder.Positions, builder.Indices, builder.HasTexCoords ? builder.TexCoords : null);
                if (mesh.TriangleCount > 0)
                    parts.Add(new BlockVisualPart(mesh, key.Look, key.Textures));
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

    /// <summary>
    /// Adds one model's triangles (moved by <paramref name="transform"/>) and then, recursively, its subparts.
    /// Subparts are the moving pieces the game attaches at runtime: turret heads and barrels, door panels,
    /// drill heads, landing gear... The parent model marks each with a "subpart_*" dummy whose "file" property
    /// names the subpart's model (in the same folder) and whose matrix says where it sits.
    /// </summary>
    private void AddModel(string relativePath, System.Numerics.Matrix4x4 transform, int depth,
        Dictionary<(PartAppearance, MaterialTextures?), GeometryBuilder> groups, HashSet<string> visiting)
    {
        if (depth > MaxSubpartDepth || !visiting.Add(relativePath))
            return; // guard against broken files that reference themselves

        var main = MwmReader.Read(Resolve(relativePath));
        var geometry = PickLod(main);

        // A mirroring transform turns triangles inside-out, so the winding flip must be undone.
        var mirrored = transform.GetDeterminant() < 0;
        foreach (var part in geometry.Parts)
        {
            var look = PartAppearance.FromMaterial(part.MaterialName, part.Technique);
            if (look is null || part.Indices.Length < 3)
                continue;
            var textures = look.Value.Kind == PartKind.Glass ? null : MaterialTextures.FromPart(part);
            if (!groups.TryGetValue((look.Value, textures), out var builder))
                groups[(look.Value, textures)] = builder = new GeometryBuilder();

            // The game stores triangles clockwise (DirectX style); we need counter-clockwise.
            // Checked across all ~1300 vanilla models: ~90% are inside-out without this flip.
            builder.Add(geometry, part.Indices, transform, flip: !geometry.SwapWindingOrder ^ mirrored);
        }

        foreach (var dummy in main.Dummies)
        {
            if (!dummy.Name.StartsWith("subpart_", StringComparison.OrdinalIgnoreCase) ||
                !dummy.Properties.TryGetValue("file", out var file) || string.IsNullOrWhiteSpace(file))
                continue;

            var folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
            var subpartPath = Path.Combine(folder, file.Trim());
            if (!File.Exists(Resolve(subpartPath)))
                continue;

            // Dummy matrices carry the dummy box's size as scale; the game normalizes it away for subparts.
            var placement = WithoutScale(dummy.Matrix);
            try
            {
                AddModel(subpartPath, placement * transform, depth + 1, groups, visiting);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or EndOfStreamException or FormatException)
            {
                // A broken subpart shouldn't hide the rest of the block.
            }
        }

        visiting.Remove(relativePath);
    }

    private static System.Numerics.Matrix4x4 WithoutScale(System.Numerics.Matrix4x4 m)
    {
        var x = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(m.M11, m.M12, m.M13));
        var y = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(m.M21, m.M22, m.M23));
        var z = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(m.M31, m.M32, m.M33));
        return new System.Numerics.Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            m.M41, m.M42, m.M43, 1);
    }

    /// <summary>Accumulates triangles from several models (with their own transforms) into one mesh.</summary>
    private sealed class GeometryBuilder
    {
        public List<System.Numerics.Vector3> Positions { get; } = new();
        public List<System.Numerics.Vector2> TexCoords { get; } = new();
        public List<int> Indices { get; } = new();
        public bool HasTexCoords { get; private set; } = true;

        public void Add(MwmModel geometry, int[] indices, System.Numerics.Matrix4x4 transform, bool flip)
        {
            var offset = Positions.Count;
            var identity = transform.IsIdentity;
            foreach (var p in geometry.Positions)
                Positions.Add(identity ? p : System.Numerics.Vector3.Transform(p, transform));
            if (geometry.TexCoords.Length == geometry.Positions.Length)
                TexCoords.AddRange(geometry.TexCoords);
            else
            {
                HasTexCoords = false;
                TexCoords.AddRange(new System.Numerics.Vector2[geometry.Positions.Length]);
            }

            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                Indices.Add(indices[i] + offset);
                Indices.Add((flip ? indices[i + 2] : indices[i + 1]) + offset);
                Indices.Add((flip ? indices[i + 1] : indices[i + 2]) + offset);
            }
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
