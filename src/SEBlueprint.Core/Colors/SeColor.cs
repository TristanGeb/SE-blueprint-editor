using System.Numerics;

namespace SEBlueprint.Core.Colors;

/// <summary>
/// Converts between SE's paint "color mask" and normal colors.
///
/// SE stores paint as HSV where saturation and value are stored as offsets:
///   stored.Y = saturation - 0.8,  stored.Z = value - 0.55
/// So the default grey (0, -0.8, -0.13) means hue 0, saturation 0, value 0.42.
/// The game also tints the result with the block's material, so on-screen colors here are close but not exact.
/// </summary>
public static class SeColor
{
    public const float SaturationOffset = 0.8f;
    public const float ValueOffset = 0.55f;

    /// <summary>Color mask (as stored in the blueprint) to plain HSV, each component in 0..1.</summary>
    public static Vector3 MaskToHsv(Vector3 mask) => new(
        Wrap01(mask.X),
        System.Math.Clamp(mask.Y + SaturationOffset, 0f, 1f),
        System.Math.Clamp(mask.Z + ValueOffset, 0f, 1f));

    /// <summary>Plain HSV (0..1) to the color mask stored in the blueprint.</summary>
    public static Vector3 HsvToMask(Vector3 hsv) => new(
        Wrap01(hsv.X),
        System.Math.Clamp(hsv.Y, 0f, 1f) - SaturationOffset,
        System.Math.Clamp(hsv.Z, 0f, 1f) - ValueOffset);

    /// <summary>Color mask to linear-ish RGB, each component in 0..1.</summary>
    public static Vector3 MaskToRgb(Vector3 mask) => HsvToRgb(MaskToHsv(mask));

    public static Vector3 HsvToRgb(Vector3 hsv)
    {
        var (h, s, v) = (hsv.X, hsv.Y, hsv.Z);
        if (s <= 0f)
            return new Vector3(v, v, v);

        var sector = h * 6f;
        var i = (int)MathF.Floor(sector) % 6;
        var f = sector - MathF.Floor(sector);
        var p = v * (1f - s);
        var q = v * (1f - s * f);
        var t = v * (1f - s * (1f - f));
        return i switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    public static Vector3 RgbToHsv(Vector3 rgb)
    {
        var max = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z));
        var min = MathF.Min(rgb.X, MathF.Min(rgb.Y, rgb.Z));
        var delta = max - min;

        float h = 0f;
        if (delta > 0f)
        {
            if (max == rgb.X)
                h = ((rgb.Y - rgb.Z) / delta) % 6f;
            else if (max == rgb.Y)
                h = (rgb.Z - rgb.X) / delta + 2f;
            else
                h = (rgb.X - rgb.Y) / delta + 4f;
            h /= 6f;
        }

        var s = max <= 0f ? 0f : delta / max;
        return new Vector3(Wrap01(h), s, max);
    }

    private static float Wrap01(float x)
    {
        x %= 1f;
        return x < 0f ? x + 1f : x;
    }
}
