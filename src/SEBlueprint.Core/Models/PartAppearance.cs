using System.Numerics;

namespace SEBlueprint.Core.Models;

public enum PartKind
{
    /// <summary>Takes the block's paint color.</summary>
    Paint,

    /// <summary>A fixed color that paint doesn't change (dark trim, chrome, rubber...).</summary>
    Fixed,

    /// <summary>Light-emitting surface.</summary>
    Emissive,

    /// <summary>See-through glass.</summary>
    Glass,
}

/// <summary>How one part of a model is colored. Fixed colors are RGB 0..1.</summary>
public readonly record struct PartAppearance(PartKind Kind, Vector3 Color)
{
    public static readonly PartAppearance Paint = new(PartKind.Paint, Vector3.One);

    /// <summary>
    /// Guesses a part's look from its material name. Without the game's textures this is an approximation,
    /// but the names are consistent: "PaintedMetal_Colorable" takes paint, "PaintedMetal_Darker" is dark trim, etc.
    /// Returns null for parts that should not be drawn (decals and holograms are overlays that would z-fight).
    /// </summary>
    public static PartAppearance? FromMaterial(string? materialName, string? technique)
    {
        switch (technique)
        {
            case "DECAL" or "DECAL_CUTOUT" or "DECAL_NOPREMULT" or "HOLO" or "FOLIAGE" or "SHIELD":
                return null;
            case "GLASS":
                return new PartAppearance(PartKind.Glass, new Vector3(0.55f, 0.7f, 0.8f));
        }

        var name = materialName ?? string.Empty;
        bool Has(string s) => name.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (Has("Colorable") || Has("_LOD"))
            return Paint; // "_LODn" materials are baked atlases of the whole block, mostly painted areas
        if (Has("Emissive") || Has("Light") || Has("Screen"))
            return new PartAppearance(PartKind.Emissive, new Vector3(0.85f, 0.92f, 1f));
        if (Has("Glass"))
            return new PartAppearance(PartKind.Glass, new Vector3(0.55f, 0.7f, 0.8f));
        if (Has("VeryDark") || Has("Black"))
            return Gray(0.08f);
        if (Has("Rubber") || Has("Tire") || Has("Tires"))
            return Gray(0.06f);
        if (Has("Darker") || Has("Dark"))
            return Gray(0.18f);
        if (Has("Yellow"))
            return new PartAppearance(PartKind.Fixed, new Vector3(0.8f, 0.6f, 0.08f));
        if (Has("Red"))
            return new PartAppearance(PartKind.Fixed, new Vector3(0.65f, 0.1f, 0.08f));
        if (Has("Chrome") || Has("Shiny"))
            return Gray(0.72f);
        if (Has("White") || Has("Clean"))
            return Gray(0.82f);
        if (Has("Metal") || Has("Steel") || Has("Gear"))
            return Gray(0.45f);
        return Paint; // block-specific materials ("ATM_Screen", "CockpitSmall"...) mostly cover the painted body
    }

    private static PartAppearance Gray(float v) => new(PartKind.Fixed, new Vector3(v));
}
