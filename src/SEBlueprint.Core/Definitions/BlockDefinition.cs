using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.Definitions;

/// <summary>What the game says about one block type: its size, name and so on.</summary>
public sealed record BlockDefinition(
    string TypeId,
    string SubtypeId,
    string DisplayName,
    GridSize CubeSize,
    Vector3I Size,
    string? IconPath,
    string? ModelPath)
{
    /// <summary>True for the placeholder used when a block's definition can't be found (e.g. mod blocks).</summary>
    public bool IsUnknown { get; init; }

    /// <summary>
    /// For armor-style blocks (which have no single model): the shape, e.g. "Box", "Slope", "Corner".
    /// Null for blocks with a model.
    /// </summary>
    public string? CubeTopology { get; init; }

    /// <summary>Offset of the model from the block's center, in model space (meters). Almost always zero.</summary>
    public System.Numerics.Vector3 ModelOffset { get; init; }

    public static BlockDefinition Unknown(string typeId, string subtypeId, GridSize size) =>
        new(typeId, subtypeId, string.IsNullOrEmpty(subtypeId) ? typeId : subtypeId, size, Vector3I.One, null, null)
        {
            IsUnknown = true,
        };
}
