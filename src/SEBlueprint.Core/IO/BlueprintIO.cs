using System.Text;
using System.Xml;
using System.Xml.Linq;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Core.IO;

/// <summary>Loads and saves bp.sbc files.</summary>
public static class BlueprintIO
{
    public const string BlueprintFileName = "bp.sbc";

    /// <summary>The game's binary cache of bp.sbc. Must be deleted after editing so the game re-reads the XML.</summary>
    public const string BinaryCacheFileName = "bp.sbcB5";

    public const string BackupFileName = "bp.sbc.bak";

    /// <summary>Loads a blueprint from a bp.sbc file, or from a blueprint folder containing one.</summary>
    public static Blueprint Load(string path)
    {
        if (Directory.Exists(path))
            path = Path.Combine(path, BlueprintFileName);

        using var stream = File.OpenRead(path);
        var blueprint = Load(stream);
        blueprint.FilePath = path;
        return blueprint;
    }

    public static Blueprint Load(Stream stream)
    {
        var doc = XDocument.Load(stream, LoadOptions.None);
        return new Blueprint(doc, filePath: null);
    }

    public static Blueprint Parse(string xml) => new(XDocument.Parse(xml), filePath: null);

    /// <summary>
    /// Saves the blueprint. When saving over an existing bp.sbc this:
    /// 1. keeps a one-time backup of the original as bp.sbc.bak,
    /// 2. writes to a temp file first and swaps it in, so a crash never leaves a half-written file,
    /// 3. deletes bp.sbcB5 so the game loads the edited XML instead of its stale binary cache.
    /// </summary>
    public static void Save(Blueprint blueprint, string? path = null)
    {
        path ??= blueprint.FilePath ?? throw new InvalidOperationException("No file path given and the blueprint was not loaded from a file.");
        if (Directory.Exists(path))
            path = Path.Combine(path, BlueprintFileName);

        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);

        var backup = Path.Combine(folder, BackupFileName);
        if (File.Exists(path) && !File.Exists(backup))
            File.Copy(path, backup);

        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            Save(blueprint, stream);
        File.Move(temp, path, overwrite: true);

        var cache = Path.Combine(folder, BinaryCacheFileName);
        if (File.Exists(cache))
            File.Delete(cache);

        blueprint.FilePath = path;
    }

    public static void Save(Blueprint blueprint, Stream stream)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        using var writer = XmlWriter.Create(stream, settings);
        blueprint.Document.Save(writer);
    }

    public static string ToXml(Blueprint blueprint)
    {
        using var ms = new MemoryStream();
        Save(blueprint, ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
