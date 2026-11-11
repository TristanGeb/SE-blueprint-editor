using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;
using SEBlueprint.Core.Xml;

namespace SEBlueprint.Core.Editing;

/// <summary>Builds the XML for a new block, the way the game writes it in a blueprint.</summary>
public static class BlockFactory
{
    /// <summary>
    /// A new block for <paramref name="grid"/> that isn't in it yet (use <see cref="AddBlockCommand"/> to add it).
    /// Only the essentials are written; the game fills in defaults for everything else when it loads the blueprint.
    /// Functional blocks get a fresh EntityId (plain armor has none, like in the game's own files), unless
    /// <paramref name="assignEntityId"/> is false (for previews that are never added: finding a free id scans the blueprint).
    /// </summary>
    public static Block Create(
        CubeGrid grid, BlockDefinition definition, Vector3I min, BlockOrientation orientation, Vector3 colorMaskHsv,
        bool assignEntityId = true)
    {
        var element = new XElement("MyObjectBuilder_CubeBlock",
            new XAttribute(XmlHelpers.Xsi + "type", "MyObjectBuilder_" + definition.TypeId),
            new XElement("SubtypeName", definition.SubtypeId));
        if (assignEntityId && !definition.IsStructural)
            element.Add(new XElement("EntityId", NewEntityId(grid.Blueprint)));

        var block = new Block(element, grid)
        {
            Min = min,
            Orientation = orientation,
            ColorMaskHsv = colorMaskHsv,
        };
        return block;
    }

    /// <summary>A random id in the range the game uses, not used by any block or grid in the blueprint.</summary>
    private static string NewEntityId(Blueprint blueprint)
    {
        var used = blueprint.AllBlocks.Select(b => b.EntityId)
            .Concat(blueprint.Grids.Select(g => g.Element.ChildValue("EntityId")))
            .OfType<string>()
            .ToHashSet();
        while (true)
        {
            var id = Random.Shared.NextInt64(100_000_000_000_000_000L, 999_999_999_999_999_999L)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!used.Contains(id))
                return id;
        }
    }
}

/// <summary>Adds one block (made by <see cref="BlockFactory"/>) to the end of its grid.</summary>
public sealed class AddBlockCommand : IEditCommand
{
    private readonly Block _block;
    private readonly string _displayName;

    public AddBlockCommand(Block block, string displayName)
    {
        if (block.Grid.Blocks.Contains(block))
            throw new ArgumentException("The block is already in its grid.", nameof(block));
        _block = block;
        _displayName = displayName;
    }

    public Block Block => _block;

    public string Name => $"Place {_displayName}";

    public EditChange Do()
    {
        _block.Grid.Blocks.Add(_block);
        _block.Grid.CubeBlocksElement.Add(_block.Element);
        return Change();
    }

    public EditChange Undo()
    {
        _block.Grid.Blocks.Remove(_block);
        _block.Element.Remove();
        return Change();
    }

    private EditChange Change() => new(new[] { _block.Grid }, StructureChanged: true);
}
