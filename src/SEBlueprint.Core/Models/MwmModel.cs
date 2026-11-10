using System.Numerics;

namespace SEBlueprint.Core.Models;

/// <summary>
/// Geometry read from a Space Engineers .mwm model file. Positions are in meters, in the block's
/// model space (centered on the block, Forward = -Z, Up = +Y).
/// </summary>
public sealed class MwmModel
{
    public required int Version { get; init; }

    public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();

    /// <summary>Texture coordinates per vertex (same count as <see cref="Positions"/>), or empty if the model has none.</summary>
    public Vector2[] TexCoords { get; init; } = Array.Empty<Vector2>();

    public IReadOnlyList<MwmMeshPart> Parts { get; init; } = Array.Empty<MwmMeshPart>();

    /// <summary>True if triangles are stored clockwise and must be flipped.</summary>
    public bool SwapWindingOrder { get; init; }

    public Vector3 BoundsMin { get; init; }

    public Vector3 BoundsMax { get; init; }

    /// <summary>
    /// Newer models keep their geometry in a separate file; this is its path relative to Content,
    /// without extension (e.g. "Models\Cubes\large\ButtonPanel_LOD0"). Null if geometry is in this file.
    /// </summary>
    public string? GeometryAsset { get; init; }

    /// <summary>Lower-detail versions of the model (relative paths without extension), from most to least detailed.</summary>
    public IReadOnlyList<MwmLod> Lods { get; init; } = Array.Empty<MwmLod>();

    /// <summary>
    /// Named points inside the model ("dummies"). The game uses them for interaction areas, conveyor ports and,
    /// importantly, "subpart_X" dummies that mark where moving parts (turret heads, rotor tops, doors) attach.
    /// </summary>
    public IReadOnlyList<MwmDummy> Dummies { get; init; } = Array.Empty<MwmDummy>();

    public bool HasGeometry => Positions.Length > 0 && Parts.Any(p => p.Indices.Length > 0);

    public int TriangleCount => Parts.Sum(p => p.Indices.Length) / 3;
}

/// <summary>A group of triangles sharing one material.</summary>
public sealed class MwmMeshPart
{
    public required int[] Indices { get; init; }

    public string? MaterialName { get; init; }

    /// <summary>Render technique, e.g. "MESH", "GLASS", "ALPHA_MASKED", "DECAL".</summary>
    public string? Technique { get; init; }

    /// <summary>Texture slots, e.g. "ColorMetalTexture" → "Textures\...\_cm.dds".</summary>
    public IReadOnlyDictionary<string, string> Textures { get; init; } = new Dictionary<string, string>();
}

public sealed record MwmLod(float Distance, string Model);

/// <summary>A named point with a position/rotation (row-vector matrix) and free-form properties.</summary>
public sealed record MwmDummy(string Name, Matrix4x4 Matrix, IReadOnlyDictionary<string, string> Properties);
