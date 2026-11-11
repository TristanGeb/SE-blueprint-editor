using System.Numerics;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.Editing;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class PlacementTests
{
    /// <summary>Armor cubes at (0,0,0) and (1,0,0), and a 1×1×3 "Long" block at (0,2,-2) covering z -2..0.</summary>
    private static Blueprint Sample() => BlueprintIO.Parse("""
        <?xml version="1.0"?>
        <Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <ShipBlueprints><ShipBlueprint>
            <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="Place" />
            <CubeGrids>
              <CubeGrid><EntityId>1</EntityId><GridSizeEnum>Large</GridSizeEnum><CubeBlocks>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>LargeBlockArmorBlock</SubtypeName></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>LargeBlockArmorBlock</SubtypeName><Min x="1" y="0" z="0" /></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>Long</SubtypeName><Min x="0" y="2" z="-2" /></MyObjectBuilder_CubeBlock>
              </CubeBlocks><DisplayName>Main</DisplayName></CubeGrid>
            </CubeGrids>
          </ShipBlueprint></ShipBlueprints>
        </Definitions>
        """);

    private static Vector3I SizeOf(Block b) => b.SubtypeName == "Long" ? new Vector3I(1, 1, 3) : Vector3I.One;

    private static GridOccupancy Occupancy(Blueprint bp) => new(bp.Grids[0], SizeOf);

    [Fact]
    public void Occupancy_covers_every_cell_of_multi_cell_blocks()
    {
        var occ = Occupancy(Sample());
        Assert.NotNull(occ.BlockAt(new Vector3I(0, 2, -1)));
        Assert.Null(occ.BlockAt(new Vector3I(0, 1, 0)));
        Assert.Equal(new Vector3I(0, 0, -2), occ.Min);
        Assert.Equal(new Vector3I(1, 2, 0), occ.Max);
        Assert.False(occ.IsFree(new Vector3I(0, 2, 0), Vector3I.One, BlockOrientation.Default));
        Assert.True(occ.IsFree(new Vector3I(0, 1, 0), Vector3I.One, BlockOrientation.Default));
    }

    [Fact]
    public void Ray_from_above_hits_the_top_face_of_the_first_block()
    {
        var occ = Occupancy(Sample());
        // Straight down onto cell (1,0,0), from above everything.
        var hit = occ.Raycast(new Vector3(1.2f, 10f, 0.1f), new Vector3(0, -1, 0));
        Assert.NotNull(hit);
        Assert.Equal(new Vector3I(1, 0, 0), hit!.Value.Cell);
        Assert.Equal(new Vector3I(0, 1, 0), hit.Value.Normal);
        Assert.Equal(9.5f, hit.Value.Distance, 3);
    }

    [Fact]
    public void Diagonal_ray_steps_through_empty_cells_and_reports_the_entry_face()
    {
        var occ = Occupancy(Sample());
        // From the +X side, sloping slightly down, at the height of row y=0: enters (1,0,0) through its +X face.
        var hit = occ.Raycast(new Vector3(6f, 0.3f, 0.2f), new Vector3(-1f, -0.02f, 0f));
        Assert.Equal(new Vector3I(1, 0, 0), hit!.Value.Cell);
        Assert.Equal(new Vector3I(1, 0, 0), hit.Value.Normal);
    }

    [Fact]
    public void Ray_that_misses_everything_returns_null()
    {
        var occ = Occupancy(Sample());
        Assert.Null(occ.Raycast(new Vector3(10, 10, 10), new Vector3(1, 0, 0)));
        Assert.Null(occ.Raycast(new Vector3(0, 1, 5), new Vector3(0, 0, 1))); // pointing away
    }

    [Fact]
    public void Block_against_a_face_touches_it_and_extends_away()
    {
        var hitTop = new CellHit(new Vector3I(0, 0, 0), new Vector3I(0, 1, 0), null!, 0);
        Assert.Equal(new Vector3I(0, 1, 0), GridOccupancy.MinAgainstFace(hitTop, new Vector3I(1, 1, 1)));
        // A 1×3×1 block on a bottom face hangs down: its top cell is just under the face.
        var hitBottom = hitTop with { Normal = new Vector3I(0, -1, 0) };
        Assert.Equal(new Vector3I(0, -3, 0), GridOccupancy.MinAgainstFace(hitBottom, new Vector3I(1, 3, 1)));
        // A 3×1×1 block on a top face is centered over the cell.
        Assert.Equal(new Vector3I(-1, 1, 0), GridOccupancy.MinAgainstFace(hitTop, new Vector3I(3, 1, 1)));
    }

    [Fact]
    public void Work_plane_picks_the_cell_under_the_ray_in_its_layer()
    {
        // Looking down from (2.3, 10, -1.6) at the floor through layer y=3.
        var cell = GridOccupancy.CellOnPlane(new Vector3(2.3f, 10f, -1.6f), new Vector3(0, -1, 0), PlaneAxis.Y, 3);
        Assert.Equal(new Vector3I(2, 3, -2), cell);
        Assert.Null(GridOccupancy.CellOnPlane(new Vector3(0, 10, 0), new Vector3(0, 1, 0), PlaneAxis.Y, 3)); // away
        Assert.Null(GridOccupancy.CellOnPlane(new Vector3(0, 10, 0), new Vector3(1, 0, 0), PlaneAxis.Y, 3)); // parallel
        Assert.Equal(new Vector3I(1, 3, -2), GridOccupancy.MinOnPlane(cell!.Value, PlaneAxis.Y, new Vector3I(3, 2, 1)));
    }

    [Fact]
    public void Quarter_turns_around_an_axis_come_back_after_four()
    {
        var o = new BlockOrientation(Base6Direction.Forward, Base6Direction.Up);
        var yawed = o.Rotated(new Vector3I(0, 1, 0));
        Assert.Equal(new BlockOrientation(Base6Direction.Left, Base6Direction.Up), yawed);
        Assert.Equal(o, yawed.Rotated(new Vector3I(0, 1, 0)).Rotated(new Vector3I(0, 1, 0)).Rotated(new Vector3I(0, 1, 0)));
        Assert.Equal(o, yawed.Rotated(new Vector3I(0, -1, 0)));
    }

    [Fact]
    public void Added_block_saves_like_the_games_and_undo_removes_it_exactly()
    {
        var bp = Sample();
        var session = new EditorSession(bp);
        var before = BlueprintIO.ToXml(bp);
        var thrust = new BlockDefinition("Thrust", "LargeBlockSmallThrust", "Small Thruster", GridSize.Large, Vector3I.One, null, null);

        var block = BlockFactory.Create(bp.Grids[0], thrust, new Vector3I(0, 1, 0),
            new BlockOrientation(Base6Direction.Down, Base6Direction.Forward), new Vector3(0.5f, 0f, 0f));
        session.Execute(new AddBlockCommand(block, "Small Thruster"));

        Assert.Equal(4, bp.Grids[0].Blocks.Count);
        var reloaded = BlueprintIO.Parse(BlueprintIO.ToXml(bp)).Grids[0].Blocks[3];
        Assert.Equal("Thrust", reloaded.TypeId);
        Assert.Equal("LargeBlockSmallThrust", reloaded.SubtypeName);
        Assert.Equal(new Vector3I(0, 1, 0), reloaded.Min);
        Assert.Equal(new BlockOrientation(Base6Direction.Down, Base6Direction.Forward), reloaded.Orientation);
        Assert.NotNull(reloaded.EntityId);

        session.Undo();
        Assert.Equal(before, BlueprintIO.ToXml(bp));
        session.Redo();
        Assert.Same(block, bp.Grids[0].Blocks[3]);
    }

    [Fact]
    public void Plain_armor_gets_no_entity_id()
    {
        var bp = Sample();
        var armor = new BlockDefinition("CubeBlock", "LargeBlockArmorBlock", "Light Armor Block", GridSize.Large, Vector3I.One, null, null);
        var block = BlockFactory.Create(bp.Grids[0], armor, new Vector3I(5, 5, 5), BlockOrientation.Default, Block.DefaultColorMaskHsv);
        Assert.Null(block.EntityId);
    }
}
