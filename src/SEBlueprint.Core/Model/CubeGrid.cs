using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Xml;

namespace SEBlueprint.Core.Model;

public enum GridSize
{
    Large,
    Small,
}

/// <summary>
/// One grid (ship/station/subgrid) inside a blueprint. Like <see cref="Block"/>, it wraps the original XML.
/// A blueprint can hold several grids: the main grid plus subgrids on rotors, pistons and hinges.
/// </summary>
public sealed class CubeGrid
{
    public CubeGrid(XElement element, Blueprint blueprint)
    {
        Element = element;
        Blueprint = blueprint;
        var cubeBlocks = element.Element("CubeBlocks");
        if (cubeBlocks is not null)
        {
            foreach (var b in cubeBlocks.Elements("MyObjectBuilder_CubeBlock"))
                Blocks.Add(new Block(b, this));
        }
    }

    public XElement Element { get; }

    public Blueprint Blueprint { get; }

    public List<Block> Blocks { get; } = new();

    public string DisplayName
    {
        get => Element.ChildValue("DisplayName") ?? "(unnamed grid)";
        set => XmlHelpers.GetOrCreate(Element, "DisplayName", "CubeBlocks").Value = value;
    }

    public GridSize GridSize =>
        Element.ChildValue("GridSizeEnum") == "Small" ? GridSize.Small : GridSize.Large;

    /// <summary>Edge length of one grid cell in meters: 2.5 for large grids, 0.5 for small grids.</summary>
    public float CellSize => GridSize == GridSize.Large ? 2.5f : 0.5f;

    /// <summary>
    /// The grid's world position and rotation when the blueprint was made. Used to place
    /// subgrids correctly relative to the main grid.
    /// </summary>
    public GridPlacement Placement
    {
        get
        {
            var pao = Element.Element("PositionAndOrientation");
            if (pao is null)
                return GridPlacement.Identity;

            var posEl = pao.Element("Position");
            var position = new Vector3D(
                XmlHelpers.ParseDouble(posEl?.Attribute("x")?.Value),
                XmlHelpers.ParseDouble(posEl?.Attribute("y")?.Value),
                XmlHelpers.ParseDouble(posEl?.Attribute("z")?.Value));
            var forward = XmlHelpers.ReadVector3(pao.Element("Forward")) ?? -Vector3.UnitZ;
            var up = XmlHelpers.ReadVector3(pao.Element("Up")) ?? Vector3.UnitY;
            return new GridPlacement(position, Vector3.Normalize(forward), Vector3.Normalize(up));
        }
    }

    public override string ToString() => $"{DisplayName} ({GridSize}, {Blocks.Count} blocks)";
}

/// <summary>Double-precision position: SE world coordinates can be millions of meters from the origin.</summary>
public readonly record struct Vector3D(double X, double Y, double Z)
{
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
}

/// <summary>Where a grid sits in the world: position plus Forward/Up directions.</summary>
public readonly record struct GridPlacement(Vector3D Position, Vector3 Forward, Vector3 Up)
{
    public static readonly GridPlacement Identity = new(default, -Vector3.UnitZ, Vector3.UnitY);

    /// <summary>
    /// Rotation-only matrix (row-vector convention, as used by System.Numerics):
    /// rows are Right, Up, Backward.
    /// </summary>
    public Matrix4x4 Rotation
    {
        get
        {
            var right = Vector3.Normalize(Vector3.Cross(Forward, Up));
            var up = Vector3.Normalize(Vector3.Cross(right, Forward));
            var back = -Forward;
            return new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                back.X, back.Y, back.Z, 0,
                0, 0, 0, 1);
        }
    }
}
