using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Math;

namespace SEBlueprint.Core.Xml;

/// <summary>
/// Small helpers for reading and writing SE's XML. All numbers use the invariant culture,
/// so a PC set to e.g. German (comma decimal separator) never corrupts a blueprint.
/// </summary>
internal static class XmlHelpers
{
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string? ChildValue(this XElement e, string name) => e.Element(name)?.Value;

    public static float ParseFloat(string? s, float fallback = 0f) =>
        float.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : fallback;

    public static double ParseDouble(string? s, double fallback = 0d) =>
        double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : fallback;

    public static int ParseInt(string? s, int fallback = 0) =>
        int.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : fallback;

    public static string Format(float v) => v.ToString("R", Inv);

    public static string Format(int v) => v.ToString(Inv);

    /// <summary>Reads &lt;Name x="1" y="2" z="3" /&gt;.</summary>
    public static Vector3I? ReadVector3I(XElement? e) =>
        e is null
            ? null
            : new Vector3I(
                ParseInt(e.Attribute("x")?.Value),
                ParseInt(e.Attribute("y")?.Value),
                ParseInt(e.Attribute("z")?.Value));

    public static Vector3? ReadVector3(XElement? e) =>
        e is null
            ? null
            : new Vector3(
                ParseFloat(e.Attribute("x")?.Value),
                ParseFloat(e.Attribute("y")?.Value),
                ParseFloat(e.Attribute("z")?.Value));

    public static void WriteVector3I(XElement e, Vector3I v)
    {
        e.SetAttributeValue("x", Format(v.X));
        e.SetAttributeValue("y", Format(v.Y));
        e.SetAttributeValue("z", Format(v.Z));
    }

    public static void WriteVector3(XElement e, Vector3 v)
    {
        e.SetAttributeValue("x", Format(v.X));
        e.SetAttributeValue("y", Format(v.Y));
        e.SetAttributeValue("z", Format(v.Z));
    }

    /// <summary>
    /// Returns the child element called <paramref name="name"/>, creating it if missing.
    /// The game omits fields that hold their default value, so we sometimes need to add them.
    /// New elements go right after the last existing sibling listed in <paramref name="insertAfter"/>
    /// (to keep the order the game writes), or first if none of those exist.
    /// </summary>
    public static XElement GetOrCreate(XElement parent, string name, params string[] insertAfter)
    {
        var existing = parent.Element(name);
        if (existing is not null)
            return existing;

        var created = new XElement(name);
        XElement? anchor = null;
        foreach (var candidate in insertAfter)
            anchor = parent.Element(candidate) ?? anchor;

        if (anchor is not null)
            anchor.AddAfterSelf(created);
        else
            parent.AddFirst(created);
        return created;
    }
}
