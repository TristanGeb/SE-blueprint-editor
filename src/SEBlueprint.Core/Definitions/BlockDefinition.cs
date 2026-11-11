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

    /// <summary>
    /// For armor-style blocks: the model of the first face tile (e.g. "Models\Cubes\Large\Armor\SquarePlate.mwm").
    /// Its material tells us which texture the armor uses (light vs heavy, large vs small).
    /// </summary>
    public string? ArmorTileModel { get; init; }

    /// <summary>
    /// Hit points of the fully built block: the sum of its components' integrity (e.g. light armor = 25 steel
    /// plates × 100 = 2,500). 0 if unknown.
    /// </summary>
    public float MaxIntegrity { get; init; }

    /// <summary>Damage taken is multiplied by this (below 1 = tougher). Almost always 1.</summary>
    public float GeneralDamageMultiplier { get; init; } = 1f;

    /// <summary>How much damage the block absorbs before it is destroyed.</summary>
    public float EffectiveHitPoints => MaxIntegrity / (GeneralDamageMultiplier > 0 ? GeneralDamageMultiplier : 1f);

    /// <summary>
    /// True for structural blocks (armor, panels, windows, interior walls): plain "CubeBlock"s without a function.
    /// </summary>
    public bool IsStructural => TypeId == "CubeBlock";

    /// <summary>Offset of the model from the block's center, in model space (meters). Almost always zero.</summary>
    public System.Numerics.Vector3 ModelOffset { get; init; }

    public static BlockDefinition Unknown(string typeId, string subtypeId, GridSize size) =>
        new(typeId, subtypeId, string.IsNullOrEmpty(subtypeId) ? typeId : subtypeId, size, Vector3I.One, null, null)
        {
            IsUnknown = true,
        };
}
