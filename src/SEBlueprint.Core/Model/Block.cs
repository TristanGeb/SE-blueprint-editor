using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Xml;

namespace SEBlueprint.Core.Model;

/// <summary>
/// One block in a grid. This is a thin wrapper around the block's original XML element:
/// properties read and write straight into <see cref="Element"/>, so every field we don't model
/// (inventories, scripts, terminal settings, ...) is preserved exactly when the blueprint is saved.
/// </summary>
public sealed class Block
{
    private const string BuilderPrefix = "MyObjectBuilder_";

    /// <summary>Default paint when a block has no ColorMaskHSV element (the game's default value).</summary>
    public static readonly Vector3 DefaultColorMaskHsv = new(0f, -1f, 0f);

    public Block(XElement element, CubeGrid grid)
    {
        Element = element;
        Grid = grid;
    }

    public XElement Element { get; }

    public CubeGrid Grid { get; }

    /// <summary>
    /// Block type without the "MyObjectBuilder_" prefix, e.g. "CubeBlock", "Thrust", "Warhead".
    /// Matches the TypeId used in the game's block definitions.
    /// </summary>
    public string TypeId
    {
        get
        {
            var raw = Element.Attribute(XmlHelpers.Xsi + "type")?.Value ?? "MyObjectBuilder_CubeBlock";
            return raw.StartsWith(BuilderPrefix, StringComparison.Ordinal) ? raw[BuilderPrefix.Length..] : raw;
        }
    }

    /// <summary>Block subtype, e.g. "LargeBlockArmorBlock". Can be empty for some vanilla blocks.</summary>
    public string SubtypeName
    {
        get => Element.ChildValue("SubtypeName") ?? string.Empty;
        set => XmlHelpers.GetOrCreate(Element, "SubtypeName").Value = value;
    }

    /// <summary>The player-given name shown in the terminal, if any.</summary>
    public string? CustomName
    {
        get => Element.ChildValue("CustomName");
        set
        {
            if (value is null)
                Element.Element("CustomName")?.Remove();
            else
                XmlHelpers.GetOrCreate(Element, "CustomName", "SubtypeName", "EntityId", "Min", "BlockOrientation", "ColorMaskHSV", "SkinSubtypeId").Value = value;
        }
    }

    /// <summary>The grid cell of the block's lowest corner (after rotation). Missing in XML means (0,0,0).</summary>
    public Vector3I Min
    {
        get => XmlHelpers.ReadVector3I(Element.Element("Min")) ?? Vector3I.Zero;
        set => XmlHelpers.WriteVector3I(XmlHelpers.GetOrCreate(Element, "Min", "SubtypeName", "EntityId"), value);
    }

    public BlockOrientation Orientation
    {
        get
        {
            var e = Element.Element("BlockOrientation");
            if (e is null)
                return BlockOrientation.Default;
            var forward = ParseDirection(e.Attribute("Forward")?.Value, Base6Direction.Forward);
            var up = ParseDirection(e.Attribute("Up")?.Value, Base6Direction.Up);
            var o = new BlockOrientation(forward, up);
            return o.IsValid ? o : BlockOrientation.Default;
        }
        set
        {
            var e = XmlHelpers.GetOrCreate(Element, "BlockOrientation", "SubtypeName", "EntityId", "Min");
            e.SetAttributeValue("Forward", value.Forward.ToString());
            e.SetAttributeValue("Up", value.Up.ToString());
        }
    }

    /// <summary>
    /// Paint color as SE's HSV "color mask": X = hue (0..1), Y = saturation offset, Z = value offset.
    /// Use <see cref="Colors.SeColor"/> to convert to RGB.
    /// </summary>
    public Vector3 ColorMaskHsv
    {
        get => XmlHelpers.ReadVector3(Element.Element("ColorMaskHSV")) ?? DefaultColorMaskHsv;
        set => XmlHelpers.WriteVector3(
            XmlHelpers.GetOrCreate(Element, "ColorMaskHSV", "SubtypeName", "EntityId", "Min", "BlockOrientation"),
            value);
    }

    /// <summary>Paint skin, e.g. "Battered_Armor". Null means the default (no skin).</summary>
    public string? SkinSubtypeId => Element.ChildValue("SkinSubtypeId");

    private static Base6Direction ParseDirection(string? s, Base6Direction fallback) =>
        Enum.TryParse<Base6Direction>(s, ignoreCase: false, out var d) ? d : fallback;

    public override string ToString() => $"{TypeId}/{SubtypeName} @ {Min}";
}
