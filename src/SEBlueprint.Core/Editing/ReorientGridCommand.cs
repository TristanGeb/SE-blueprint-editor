using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;
using SEBlueprint.Core.Xml;

namespace SEBlueprint.Core.Editing;

/// <summary>
/// Turns a grid's local axes so that a reference block (usually the cockpit) faces Forward with its top Up.
/// Every block's cell and rotation is rotated, and the grid's world rotation is turned the opposite way,
/// so the ship stays exactly where it was in the world: only which way counts as "forward" changes.
/// Subgrids therefore stay correctly attached without being touched.
/// </summary>
/// <remarks>
/// Grid-level data in grid coordinates is rotated too (conveyor lines, oxygen rooms). Armor deformation
/// (&lt;Skeleton&gt;) and build-mode mirror planes are removed, because they can't be rotated reliably.
/// </remarks>
public sealed class ReorientGridCommand : IEditCommand
{
    /// <summary>Grid-level elements this command removes rather than rotates.</summary>
    private static readonly string[] RemovedElements =
        { "Skeleton", "XMirroxPlane", "YMirroxPlane", "ZMirroxPlane", "XMirroxOdd", "YMirroxOdd", "ZMirroxOdd" };

    private readonly Block _reference;
    private readonly Func<Block, Vector3I> _sizeOf;

    // Undo snapshot, taken by Do().
    private (Block Block, XElement? Min, XElement? Orientation)[] _blockOriginals = Array.Empty<(Block, XElement?, XElement?)>();
    private XNode[] _gridChildren = Array.Empty<XNode>();

    /// <param name="reference">The block that should face Forward/Up afterwards.</param>
    /// <param name="sizeOf">Each block's size in cells, unrotated (from its definition).</param>
    public ReorientGridCommand(Block reference, Func<Block, Vector3I> sizeOf)
    {
        _reference = reference;
        _sizeOf = sizeOf;
        if (reference.Orientation == BlockOrientation.Default)
            throw new InvalidOperationException(
                $"Grid \"{reference.Grid.DisplayName}\" already has this block facing forward.");
    }

    public CubeGrid Grid => _reference.Grid;

    public string Name => "Align grid to block";

    /// <summary>True if the grid has armor deformation that this command will remove.</summary>
    public bool RemovesDeformation => Grid.Element.Element("Skeleton")?.HasElements == true;

    /// <summary>True if the grid has build-mode mirror planes that this command will remove.</summary>
    public bool RemovesMirrorPlanes =>
        RemovedElements.Where(n => n.EndsWith("Plane", StringComparison.Ordinal)).Any(n => Grid.Element.Element(n) is not null);

    public EditChange Do()
    {
        var grid = Grid;
        var rotate = RotationFor(_reference.Orientation);

        // Snapshot for undo. The CubeBlocks element stays live (blocks wrap its children); the rest is copied.
        _gridChildren = grid.Element.Nodes()
            .Select(n => n is XElement { Name.LocalName: "CubeBlocks" } ? n : CloneNode(n))
            .ToArray();
        _blockOriginals = grid.Blocks
            .Select(b => (b, Clone(b.Element.Element("Min")), Clone(b.Element.Element("BlockOrientation"))))
            .ToArray();

        // The reference's directions, read before its own orientation is rotated.
        var oldPlacement = grid.Placement;
        var refForward = _reference.Orientation.Forward.ToVector();
        var refUp = _reference.Orientation.Up.ToVector();

        foreach (var block in grid.Blocks)
        {
            var orientation = block.Orientation;
            var min = block.Min;
            var max = BlockGeometry.Max(min, _sizeOf(block), orientation);
            var a = rotate(min);
            var b = rotate(max);
            block.Orientation = new BlockOrientation(
                Base6DirectionExtensions.FromVector(rotate(orientation.Forward.ToVector())),
                Base6DirectionExtensions.FromVector(rotate(orientation.Up.ToVector())));
            block.Min = Vector3I.Min(a, b);
        }

        // Counter-rotate the grid in the world: its new Forward/Up are where the reference block pointed.
        var worldRotation = oldPlacement.Rotation;
        var newForward = Vector3.Normalize(Vector3.Transform(ToVector3(refForward), worldRotation));
        var newUp = Vector3.Normalize(Vector3.Transform(ToVector3(refUp), worldRotation));
        WritePlacement(grid.Element, newForward, newUp);

        RotateConveyorLines(grid.Element.Element("ConveyorLines"), rotate);
        RotateOxygenRooms(grid.Element.Element("OxygenRooms"), rotate);
        foreach (var name in RemovedElements)
            grid.Element.Element(name)?.Remove();

        return Change();
    }

    public EditChange Undo()
    {
        var grid = Grid;
        grid.Element.RemoveNodes();
        // CubeBlocks is re-added as the same object (it has no parent now, so Add doesn't clone it).
        grid.Element.Add(_gridChildren.Select(n => n is XElement { Name.LocalName: "CubeBlocks" } ? n : CloneNode(n)));

        foreach (var (block, min, orientation) in _blockOriginals)
        {
            Restore(block.Element, "Min", min);
            Restore(block.Element, "BlockOrientation", orientation);
        }
        return Change();
    }

    /// <summary>
    /// The rotation that takes the reference block's Forward/Up to the grid's Forward/Up.
    /// The block's orientation maps block-local axes to grid axes; its inverse (transpose) is what we want.
    /// </summary>
    public static Func<Vector3I, Vector3I> RotationFor(BlockOrientation reference)
    {
        var right = reference.RightVector;
        var up = reference.UpVector;
        var back = reference.BackwardVector;
        return v => new Vector3I(Dot(right, v), Dot(up, v), Dot(back, v));
    }

    /// <summary>The quaternion SE stores next to Forward/Up in &lt;PositionAndOrientation&gt;.</summary>
    public static Quaternion OrientationFor(Vector3 forward, Vector3 up) =>
        Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new GridPlacement(default, forward, up).Rotation));

    private static void WritePlacement(XElement gridElement, Vector3 forward, Vector3 up)
    {
        var pao = XmlHelpers.GetOrCreate(gridElement, "PositionAndOrientation", "PersistentFlags", "EntityId", "SubtypeName");
        // Position stays as written (it's a double; rewriting it would only lose digits).
        XmlHelpers.WriteVector3(XmlHelpers.GetOrCreate(pao, "Forward", "Position"), forward);
        XmlHelpers.WriteVector3(XmlHelpers.GetOrCreate(pao, "Up", "Forward"), up);

        if (pao.Element("Orientation") is { } q)
        {
            var quat = OrientationFor(forward, up);
            q.RemoveNodes();
            q.Add(
                new XElement("X", XmlHelpers.Format(quat.X)),
                new XElement("Y", XmlHelpers.Format(quat.Y)),
                new XElement("Z", XmlHelpers.Format(quat.Z)),
                new XElement("W", XmlHelpers.Format(quat.W)));
        }
    }

    private static void RotateConveyorLines(XElement? lines, Func<Vector3I, Vector3I> rotate)
    {
        if (lines is null)
            return;
        foreach (var line in lines.Elements())
        {
            RotateCell(line.Element("StartPosition"), rotate);
            RotateCell(line.Element("EndPosition"), rotate);
            RotateDirectionText(line.Element("StartDirection"), rotate);
            RotateDirectionText(line.Element("EndDirection"), rotate);
            foreach (var section in line.Element("Sections")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (section.Attribute("Direction") is { } attr && Enum.TryParse<Base6Direction>(attr.Value, out var d))
                    attr.Value = Base6DirectionExtensions.FromVector(rotate(d.ToVector())).ToString();
            }
        }
    }

    private static void RotateOxygenRooms(XElement? rooms, Func<Vector3I, Vector3I> rotate)
    {
        if (rooms is null)
            return;
        foreach (var start in rooms.Elements().Select(r => r.Element("StartingPosition")).OfType<XElement>())
        {
            var x = start.Element("X");
            var y = start.Element("Y");
            var z = start.Element("Z");
            if (x is null || y is null || z is null)
                continue;
            var p = rotate(new Vector3I(XmlHelpers.ParseInt(x.Value), XmlHelpers.ParseInt(y.Value), XmlHelpers.ParseInt(z.Value)));
            x.Value = XmlHelpers.Format(p.X);
            y.Value = XmlHelpers.Format(p.Y);
            z.Value = XmlHelpers.Format(p.Z);
        }
    }

    private static void RotateCell(XElement? e, Func<Vector3I, Vector3I> rotate)
    {
        if (XmlHelpers.ReadVector3I(e) is { } v)
            XmlHelpers.WriteVector3I(e!, rotate(v));
    }

    private static void RotateDirectionText(XElement? e, Func<Vector3I, Vector3I> rotate)
    {
        if (e is not null && Enum.TryParse<Base6Direction>(e.Value, out var d))
            e.Value = Base6DirectionExtensions.FromVector(rotate(d.ToVector())).ToString();
    }

    /// <summary>Do() always leaves the element in place, so undo either swaps the original back or removes it.</summary>
    private static void Restore(XElement blockElement, string name, XElement? original)
    {
        var current = blockElement.Element(name);
        if (original is not null)
            current?.ReplaceWith(new XElement(original));
        else
            current?.Remove();
    }

    private static XElement? Clone(XElement? e) => e is null ? null : new XElement(e);

    private static XNode CloneNode(XNode n) => n switch
    {
        XElement e => new XElement(e),
        XText t => new XText(t),
        XComment c => new XComment(c),
        _ => n,
    };

    private static int Dot(Vector3I a, Vector3I b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vector3 ToVector3(Vector3I v) => new(v.X, v.Y, v.Z);

    // Every grid is reported because the scene is laid out relative to the main grid's world rotation.
    private EditChange Change() => new(Grid.Blueprint.Grids, StructureChanged: true, PlacementChanged: true);
}
