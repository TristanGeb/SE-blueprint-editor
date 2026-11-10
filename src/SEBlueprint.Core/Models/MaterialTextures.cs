namespace SEBlueprint.Core.Models;

/// <summary>
/// The texture files a material uses, as paths relative to the game's Content folder.
/// ColorMetal (_cm.dds) holds the surface color (RGB) and metalness (A); AddMaps (_add.dds) holds
/// ambient occlusion, emissive and the paint mask; NormalGloss (_ng.dds) holds surface bumps and gloss.
/// </summary>
public sealed record MaterialTextures(string ColorMetal, string? AddMaps, string? NormalGloss)
{
    public static MaterialTextures? FromPart(MwmMeshPart part)
    {
        var cm = Get(part, "ColorMetalTexture") ?? Get(part, "DiffuseTexture");
        return cm is null ? null : new MaterialTextures(cm, Get(part, "AddMapsTexture"), Get(part, "NormalGlossTexture"));
    }

    private static string? Get(MwmMeshPart part, string key) =>
        part.Textures.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path) ? path.Trim() : null;
}
