using System.IO;
using System.Text.Json;

namespace SEBlueprint.App.Services;

/// <summary>User settings, stored as JSON in %AppData%\SEBlueprintEditor\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SEBlueprintEditor", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Space Engineers install folder (contains Content\Data\CubeBlocks).</summary>
    public string? GameFolder { get; set; }

    /// <summary>Folder the Open dialog starts in.</summary>
    public string? LastOpenFolder { get; set; }

    /// <summary>How detailed block models are in the 3D view.</summary>
    public SEBlueprint.Core.Models.ModelDetail ModelDetail { get; set; } = SEBlueprint.Core.Models.ModelDetail.Medium;

    /// <summary>Draw the game's textures (off = flat colors).</summary>
    public bool ShowTextures { get; set; } = true;

    /// <summary>Maximum texture resolution; lower uses less memory and loads faster.</summary>
    public SEBlueprint.Rendering.TextureQuality TextureQuality { get; set; } = SEBlueprint.Rendering.TextureQuality.High;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: start fresh rather than crash.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save settings is not worth interrupting the user for.
        }
    }
}
