using System.Numerics;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;
using static SEBlueprint.Core.Model.Base6Direction;

namespace SEBlueprint.Tests;

public class GeometryTests
{
    [Fact]
    public void Default_orientation_does_not_rotate()
    {
        var o = BlockOrientation.Default;
        Assert.Equal(new Vector3I(1, 0, 0), o.RightVector);
        Assert.Equal(new Vector3I(0, 1, 0), o.UpVector);
        Assert.Equal(new Vector3I(0, 0, 1), o.BackwardVector);
        Assert.Equal(new Vector3I(3, 2, 4), BlockGeometry.RotatedSize(new Vector3I(3, 2, 4), o));
    }

    [Theory]
    [InlineData(Forward, Up, 3, 2, 4)]
    [InlineData(Up, Forward, 3, 4, 2)]   // pointing up: length (z) becomes height (y)
    [InlineData(Down, Forward, 3, 4, 2)]
    [InlineData(Left, Up, 4, 2, 3)]      // turned sideways: length becomes width
    [InlineData(Right, Down, 4, 2, 3)]
    [InlineData(Forward, Left, 2, 3, 4)] // rolled onto its side: width and height swap
    public void Rotated_size_swaps_axes(Base6Direction forward, Base6Direction up, int x, int y, int z)
    {
        var size = BlockGeometry.RotatedSize(new Vector3I(3, 2, 4), new BlockOrientation(forward, up));
        Assert.Equal(new Vector3I(x, y, z), size);
    }

    [Theory]
    [InlineData(Forward, Backward)]
    [InlineData(Up, Up)]
    [InlineData(Left, Right)]
    public void Parallel_forward_and_up_are_invalid(Base6Direction forward, Base6Direction up)
    {
        Assert.False(new BlockOrientation(forward, up).IsValid);
    }

    [Fact]
    public void All_24_valid_orientations_keep_a_right_handed_basis()
    {
        var count = 0;
        foreach (var f in Enum.GetValues<Base6Direction>())
            foreach (var u in Enum.GetValues<Base6Direction>())
            {
                var o = new BlockOrientation(f, u);
                if (!o.IsValid)
                    continue;
                count++;
                Assert.Equal(o.BackwardVector, Vector3I.Cross(o.RightVector, o.UpVector));
            }
        Assert.Equal(24, count);
    }

    [Fact]
    public void Occupied_cells_cover_the_rotated_box()
    {
        var cells = BlockGeometry.OccupiedCells(new Vector3I(10, 0, 0), new Vector3I(1, 1, 3), new BlockOrientation(Left, Up)).ToList();

        Assert.Equal(3, cells.Count);
        Assert.Contains(new Vector3I(10, 0, 0), cells);
        Assert.Contains(new Vector3I(12, 0, 0), cells);
    }

    [Fact]
    public void Local_box_is_in_meters_and_centered_on_the_cells()
    {
        var bp = BlueprintIO.Parse(SampleBlueprints.TwoBlocks);
        var repo = new DefinitionRepository();
        repo.LoadDocument(System.Xml.Linq.XDocument.Parse(SampleBlueprints.Definitions), Localization.Empty);

        var armor = bp.Grids[0].Blocks[0];
        var (center, extents) = BlockGeometry.LocalBox(armor, repo.Get(armor));
        Assert.Equal(Vector3.Zero, center);
        Assert.Equal(new Vector3(2.5f), extents);

        // Thruster 3x2x4 at Min (-1,2,3), Forward=Down Up=Forward -> rotated size 3x4x2 -> cells x -1..1, y 2..5, z 3..4
        var thruster = bp.Grids[0].Blocks[1];
        (center, extents) = BlockGeometry.LocalBox(thruster, repo.Get(thruster));
        Assert.Equal(new Vector3(0f, 3.5f * 2.5f, 3.5f * 2.5f), center);
        Assert.Equal(new Vector3(7.5f, 10f, 5f), extents);
    }
}
