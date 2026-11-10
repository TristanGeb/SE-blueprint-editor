using System.Xml.Linq;
using SEBlueprint.Core.Xml;

namespace SEBlueprint.Core.Model;

/// <summary>
/// A loaded blueprint (the contents of one bp.sbc). Holds the whole XML document so that
/// saving writes back everything, including parts this editor doesn't understand.
/// </summary>
public sealed class Blueprint
{
    public Blueprint(XDocument document, string? filePath)
    {
        Document = document;
        FilePath = filePath;

        ShipBlueprint = document.Root?.Element("ShipBlueprints")?.Element("ShipBlueprint")
            ?? throw new InvalidDataException("This file is not a Space Engineers ship blueprint (no <ShipBlueprints>/<ShipBlueprint> element).");

        var grids = ShipBlueprint.Element("CubeGrids");
        if (grids is not null)
        {
            foreach (var g in grids.Elements("CubeGrid"))
                Grids.Add(new CubeGrid(g, this));
        }
    }

    public XDocument Document { get; }

    /// <summary>Path of the bp.sbc this was loaded from, or null if it was created in memory.</summary>
    public string? FilePath { get; internal set; }

    public XElement ShipBlueprint { get; }

    public List<CubeGrid> Grids { get; } = new();

    /// <summary>The blueprint's name (from &lt;Id Subtype="..."/&gt;), which matches its folder name.</summary>
    public string Name => ShipBlueprint.Element("Id")?.Attribute("Subtype")?.Value ?? "(unnamed)";

    public string? DisplayName => ShipBlueprint.ChildValue("DisplayName");

    public int TotalBlockCount => Grids.Sum(g => g.Blocks.Count);

    public IEnumerable<Block> AllBlocks => Grids.SelectMany(g => g.Blocks);
}
