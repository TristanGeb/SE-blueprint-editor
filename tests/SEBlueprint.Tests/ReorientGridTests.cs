using System.Numerics;
using SEBlueprint.Core.Editing;
using SEBlueprint.Core.Geometry;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class ReorientGridTests
{
    /// <summary>
    /// A tilted grid with a cockpit facing Left, a 1×1×3 "Long" block along Z, a conveyor line, an oxygen room,
    /// armor deformation and a mirror plane; plus a subgrid that must not change.
    /// </summary>
    private static Blueprint Sideways() => BlueprintIO.Parse("""
        <?xml version="1.0"?>
        <Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <ShipBlueprints><ShipBlueprint>
            <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="Sideways" />
            <CubeGrids>
              <CubeGrid>
                <EntityId>1</EntityId>
                <PositionAndOrientation>
                  <Position x="-821677.54090140969" y="-1951125.124496371" z="-1816940.7818659139" />
                  <Forward x="-0.5042062" y="0.571096063" z="-0.647785068" />
                  <Up x="0.797918856" y="0.0211776569" z="-0.6023928" />
                  <Orientation>
                    <X>-0.0135249319</X>
                    <Y>0.419433624</Y>
                    <Z>-0.6994485</Z>
                    <W>0.578501761</W>
                  </Orientation>
                </PositionAndOrientation>
                <GridSizeEnum>Large</GridSizeEnum>
                <CubeBlocks>
                  <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_Cockpit">
                    <SubtypeName>LargeBlockCockpit</SubtypeName>
                    <EntityId>10</EntityId>
                    <Min x="1" y="0" z="0" />
                    <BlockOrientation Forward="Left" Up="Up" />
                  </MyObjectBuilder_CubeBlock>
                  <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock">
                    <SubtypeName>Long</SubtypeName>
                  </MyObjectBuilder_CubeBlock>
                </CubeBlocks>
                <DisplayName>Main</DisplayName>
                <ConveyorLines>
                  <MyObjectBuilder_ConveyorLine>
                    <StartPosition x="0" y="0" z="1" />
                    <StartDirection>Backward</StartDirection>
                    <EndPosition x="0" y="0" z="2" />
                    <EndDirection>Forward</EndDirection>
                    <Sections><Section Direction="Left" Length="1" /></Sections>
                  </MyObjectBuilder_ConveyorLine>
                </ConveyorLines>
                <XMirroxPlane x="0" y="0" z="0" />
                <Skeleton><BoneInfo><BonePosition x="1" y="1" z="1" /><BoneOffset x="127" y="127" z="121" /></BoneInfo></Skeleton>
                <OxygenRooms><OxygenRoom OxygenAmount="5"><StartingPosition><X>0</X><Y>0</Y><Z>2</Z></StartingPosition></OxygenRoom></OxygenRooms>
              </CubeGrid>
              <CubeGrid>
                <PositionAndOrientation>
                  <Position x="-821670" y="-1951120" z="-1816940" />
                  <Forward x="0" y="0" z="-1" />
                  <Up x="0" y="1" z="0" />
                </PositionAndOrientation>
                <GridSizeEnum>Small</GridSizeEnum>
                <CubeBlocks>
                  <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>SmallBlockArmorBlock</SubtypeName></MyObjectBuilder_CubeBlock>
                </CubeBlocks>
                <DisplayName>Sub</DisplayName>
              </CubeGrid>
            </CubeGrids>
          </ShipBlueprint></ShipBlueprints>
        </Definitions>
        """);

    private static Vector3I SizeOf(Block b) => b.SubtypeName == "Long" ? new Vector3I(1, 1, 3) : Vector3I.One;

    private static ReorientGridCommand Command(Blueprint bp) => new(bp.Grids[0].Blocks[0], SizeOf);

    /// <summary>World position of a block's center, relative to the grid's world position.</summary>
    private static Vector3 WorldCenter(Block b)
    {
        var max = BlockGeometry.Max(b.Min, SizeOf(b), b.Orientation);
        var local = new Vector3(b.Min.X + max.X, b.Min.Y + max.Y, b.Min.Z + max.Z) * (b.Grid.CellSize / 2f);
        return Vector3.Transform(local, b.Grid.Placement.Rotation);
    }

    private static Vector3 WorldDirection(Block b, Base6Direction d)
    {
        var v = d.ToVector();
        return Vector3.Transform(new Vector3(v.X, v.Y, v.Z), b.Grid.Placement.Rotation);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void Reference_block_ends_up_facing_forward_and_up()
    {
        var bp = Sideways();
        new EditorSession(bp).Execute(Command(bp));
        Assert.Equal(BlockOrientation.Default, bp.Grids[0].Blocks[0].Orientation);
    }

    [Fact]
    public void Blocks_rotate_in_the_grid_but_stay_put_in_the_world()
    {
        var bp = Sideways();
        var blocks = bp.Grids[0].Blocks;
        var centersBefore = blocks.Select(WorldCenter).ToArray();
        var forwardsBefore = blocks.Select(b => WorldDirection(b, b.Orientation.Forward)).ToArray();
        var subBefore = bp.Grids[1].Element.ToString();

        new EditorSession(bp).Execute(Command(bp));

        // Cockpit facing Left means R(x,y,z) = (-z, y, x): the long block's cells z 0..2 become x -2..0.
        Assert.Equal(new Vector3I(-2, 0, 0), blocks[1].Min);
        Assert.Equal(new BlockOrientation(Base6Direction.Right, Base6Direction.Up), blocks[1].Orientation);
        Assert.Equal(new Vector3I(0, 0, 1), blocks[0].Min);

        for (var i = 0; i < blocks.Count; i++)
        {
            AssertClose(centersBefore[i], WorldCenter(blocks[i]));
            AssertClose(forwardsBefore[i], WorldDirection(blocks[i], blocks[i].Orientation.Forward));
        }
        Assert.Equal(subBefore, bp.Grids[1].Element.ToString()); // subgrid untouched
        Assert.Contains("<Position x=\"-821677.54090140969\"", BlueprintIO.ToXml(bp));
    }

    [Fact]
    public void Stored_quaternion_matches_forward_and_up()
    {
        // Values from a blueprint saved by the game.
        var q = ReorientGridCommand.OrientationFor(
            new Vector3(-0.5042062f, 0.571096063f, -0.647785068f), new Vector3(0.797918856f, 0.0211776569f, -0.6023928f));
        var stored = new Quaternion(-0.0135249319f, 0.419433624f, -0.6994485f, 0.578501761f);
        Assert.True(MathF.Abs(Quaternion.Dot(q, stored)) > 0.99999f, $"got {q}");
    }

    [Fact]
    public void Grid_data_is_rotated_or_removed()
    {
        var bp = Sideways();
        new EditorSession(bp).Execute(Command(bp));
        var grid = bp.Grids[0].Element;

        var line = grid.Element("ConveyorLines")!.Elements().Single();
        Assert.Equal("x=\"-1\" y=\"0\" z=\"0\"", string.Join(" ", line.Element("StartPosition")!.Attributes()));
        Assert.Equal("Left", line.Element("StartDirection")!.Value);   // Backward (0,0,1) → (-1,0,0)
        Assert.Equal("Right", line.Element("EndDirection")!.Value);
        Assert.Equal("Forward", line.Element("Sections")!.Elements().Single().Attribute("Direction")!.Value);

        var start = grid.Element("OxygenRooms")!.Element("OxygenRoom")!.Element("StartingPosition")!;
        Assert.Equal(("-2", "0", "0"), (start.Element("X")!.Value, start.Element("Y")!.Value, start.Element("Z")!.Value));

        Assert.Null(grid.Element("Skeleton"));
        Assert.Null(grid.Element("XMirroxPlane"));
    }

    [Fact]
    public void Undo_restores_the_exact_xml_and_redo_repeats()
    {
        var bp = Sideways();
        var session = new EditorSession(bp);
        var before = BlueprintIO.ToXml(bp);

        session.Execute(Command(bp));
        var after = BlueprintIO.ToXml(bp);
        session.Undo();
        Assert.Equal(before, BlueprintIO.ToXml(bp));
        session.Redo();
        Assert.Equal(after, BlueprintIO.ToXml(bp));

        // Blocks still wrap the live XML after undo/redo.
        Assert.Same(bp.Grids[0].CubeBlocksElement, bp.Grids[0].Blocks[0].Element.Parent);
    }

    [Fact]
    public void Already_aligned_block_is_refused()
    {
        var bp = Sideways();
        Assert.Throws<InvalidOperationException>(() => new ReorientGridCommand(bp.Grids[0].Blocks[1], SizeOf));
    }
}
