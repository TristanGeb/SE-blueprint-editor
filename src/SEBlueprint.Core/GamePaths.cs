using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SEBlueprint.Core;

/// <summary>Finds the Space Engineers install and the user's blueprint folder.</summary>
public static partial class GamePaths
{
    private const string GameFolderName = "SpaceEngineers";

    /// <summary>%AppData%\SpaceEngineers\Blueprints\local — where the game keeps the player's own blueprints.</summary>
    public static string LocalBlueprintsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        GameFolderName, "Blueprints", "local");

    /// <summary>True if the folder looks like a Space Engineers install (has Content\Data\CubeBlocks).</summary>
    public static bool IsGameFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        Directory.Exists(Path.Combine(folder, "Content", "Data", "CubeBlocks"));

    public static string ContentFolder(string gameFolder) => Path.Combine(gameFolder, "Content");

    /// <summary>
    /// Looks for the game in every Steam library folder. Returns null if it can't be found,
    /// in which case the user has to pick the folder manually.
    /// </summary>
    public static string? FindGameFolder()
    {
        foreach (var library in FindSteamLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
            if (IsGameFolder(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> FindSteamLibraries()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steamPath))
            yield break;

        steamPath = Path.GetFullPath(steamPath);
        yield return steamPath;

        // libraryfolders.vdf lists every Steam library, e.g.  "path"   "D:\\SteamLibrary"
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;

        foreach (Match m in VdfPathRegex().Matches(File.ReadAllText(vdf)))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPathRegex();
}
