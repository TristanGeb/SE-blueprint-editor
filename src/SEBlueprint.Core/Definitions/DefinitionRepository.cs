using System.Xml.Linq;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;
using SEBlueprint.Core.Xml;

namespace SEBlueprint.Core.Definitions;

/// <summary>
/// All known block types, loaded at runtime from the user's Space Engineers install
/// (Content\Data\CubeBlocks\*.sbc). Game files are never copied into this project.
/// </summary>
public sealed class DefinitionRepository
{
    private const string BuilderPrefix = "MyObjectBuilder_";

    private readonly Dictionary<(string TypeId, string SubtypeId), BlockDefinition> _byId = new();
    private readonly Dictionary<string, BlockDefinition> _bySubtype = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, GridSize), BlockDefinition> _unknownCache = new();

    public static DefinitionRepository Empty { get; } = new();

    public int Count => _byId.Count;

    public IEnumerable<BlockDefinition> All => _byId.Values;

    /// <summary>Loads every block definition from the game's Content folder.</summary>
    /// <param name="contentPath">e.g. "C:\...\steamapps\common\SpaceEngineers\Content"</param>
    public static DefinitionRepository LoadFromGame(string contentPath)
    {
        var localization = Localization.Empty;
        var resx = Path.Combine(contentPath, "Data", "Localization", "MyTexts.resx");
        if (File.Exists(resx))
            localization = Localization.LoadResx(resx);

        var repo = new DefinitionRepository();
        var folder = Path.Combine(contentPath, "Data", "CubeBlocks");
        foreach (var file in Directory.EnumerateFiles(folder, "*.sbc").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            repo.LoadFile(file, localization);
        return repo;
    }

    /// <summary>Loads the block definitions from one .sbc file. Later definitions override earlier ones.</summary>
    public void LoadFile(string path, Localization localization)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (System.Xml.XmlException)
        {
            return; // a broken file shouldn't stop the editor from starting
        }
        LoadDocument(doc, localization);
    }

    public void LoadDocument(XDocument doc, Localization localization)
    {
        var cubeBlocks = doc.Root?.Element("CubeBlocks");
        if (cubeBlocks is null)
            return;

        foreach (var def in cubeBlocks.Elements("Definition"))
        {
            var parsed = Parse(def, localization);
            if (parsed is not null)
                Add(parsed);
        }
    }

    public void Add(BlockDefinition def)
    {
        _byId[(def.TypeId, def.SubtypeId)] = def;
        if (!string.IsNullOrEmpty(def.SubtypeId))
            _bySubtype.TryAdd(def.SubtypeId, def);
    }

    /// <summary>Finds the definition for a block. Never returns null: unknown blocks get a 1x1x1 placeholder.</summary>
    public BlockDefinition Get(Block block) => Get(block.TypeId, block.SubtypeName, block.Grid.GridSize);

    public BlockDefinition Get(string typeId, string subtypeId, GridSize gridSize)
    {
        if (_byId.TryGetValue((typeId, subtypeId), out var def))
            return def;

        // Some blocks are saved with a different object builder type than their definition's TypeId,
        // but subtype names are almost always unique, so fall back to matching on subtype alone.
        if (!string.IsNullOrEmpty(subtypeId) && _bySubtype.TryGetValue(subtypeId, out def))
            return def;

        return _unknownCache.GetOrAdd(
            (typeId, subtypeId, gridSize),
            k => BlockDefinition.Unknown(k.Item1, k.Item2, k.Item3));
    }

    public bool Contains(Block block) => !Get(block).IsUnknown;

    private static BlockDefinition? Parse(XElement def, Localization localization)
    {
        // The Id is usually <Id><TypeId/><SubtypeId/></Id>, but some files use <Id Type="" Subtype=""/>.
        var id = def.Element("Id");
        if (id is null)
            return null;

        var typeId = id.ChildValue("TypeId") ?? id.Attribute("Type")?.Value;
        var subtypeId = id.ChildValue("SubtypeId") ?? id.Attribute("Subtype")?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(typeId))
            return null;
        if (typeId.StartsWith(BuilderPrefix, StringComparison.Ordinal))
            typeId = typeId[BuilderPrefix.Length..];

        var displayKey = def.ChildValue("DisplayName");
        var displayName = displayKey is null
            ? (string.IsNullOrEmpty(subtypeId) ? typeId : subtypeId)
            : localization.Translate(displayKey);

        var cubeSize = def.ChildValue("CubeSize") == "Small" ? GridSize.Small : GridSize.Large;
        var size = XmlHelpers.ReadVector3I(def.Element("Size")) ?? Vector3I.One;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            size = Vector3I.One;

        var model = def.ChildValue("Model");
        return new BlockDefinition(
            typeId,
            subtypeId,
            displayName,
            cubeSize,
            size,
            def.ChildValue("Icon"),
            string.IsNullOrWhiteSpace(model) ? null : model.Trim())
        {
            CubeTopology = def.Element("CubeDefinition")?.ChildValue("CubeTopology"),
            ModelOffset = XmlHelpers.ReadVector3(def.Element("ModelOffset")) ?? System.Numerics.Vector3.Zero,
        };
    }
}
