using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Editing;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class EditingTests
{
    /// <summary>A grid with five 1-cell armor blocks in a row, named B0..B4, plus a one-block grid.</summary>
    private static Blueprint FiveBlocks()
    {
        var blocks = string.Concat(Enumerable.Range(0, 5).Select(i => $"""
            <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock">
              <SubtypeName>LargeBlockArmorBlock</SubtypeName>
              <Min x="{i}" y="0" z="0" />
              {(i == 2 ? "" : $"<ColorMaskHSV x=\"0.{i}\" y=\"0\" z=\"0\" />")}
              <CustomName>B{i}</CustomName>
            </MyObjectBuilder_CubeBlock>
            """));
        return BlueprintIO.Parse($"""
            <?xml version="1.0"?>
            <Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <ShipBlueprints><ShipBlueprint>
                <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="Five" />
                <CubeGrids>
                  <CubeGrid><GridSizeEnum>Large</GridSizeEnum><CubeBlocks>{blocks}</CubeBlocks><DisplayName>Main</DisplayName></CubeGrid>
                  <CubeGrid><GridSizeEnum>Small</GridSizeEnum><CubeBlocks>
                    <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>SmallBlockArmorBlock</SubtypeName></MyObjectBuilder_CubeBlock>
                  </CubeBlocks><DisplayName>Sub</DisplayName></CubeGrid>
                </CubeGrids>
              </ShipBlueprint></ShipBlueprints>
            </Definitions>
            """);
    }

    private static string[] Names(CubeGrid grid) => grid.Blocks.Select(b => b.CustomName!).ToArray();

    private static string[] XmlNames(CubeGrid grid) =>
        grid.CubeBlocksElement.Elements().Select(e => e.Element("CustomName")!.Value).ToArray();

    [Fact]
    public void Paint_and_undo_restore_the_exact_xml()
    {
        var bp = FiveBlocks();
        var session = new EditorSession(bp);
        var before = BlueprintIO.ToXml(bp);

        // B2 has no ColorMaskHSV element at all; undo must remove the one paint added.
        session.Execute(new PaintBlocksCommand(bp.Grids[0].Blocks, new Vector3(0.5f, 0.1f, 0.2f)));
        Assert.All(bp.Grids[0].Blocks, b => Assert.Equal(new Vector3(0.5f, 0.1f, 0.2f), b.ColorMaskHsv));

        session.Undo();
        Assert.Equal(before, BlueprintIO.ToXml(bp));

        session.Redo();
        Assert.All(bp.Grids[0].Blocks, b => Assert.Equal(new Vector3(0.5f, 0.1f, 0.2f), b.ColorMaskHsv));
    }

    [Fact]
    public void Delete_keeps_list_and_xml_in_sync()
    {
        var bp = FiveBlocks();
        var grid = bp.Grids[0];
        var session = new EditorSession(bp);

        session.Execute(new DeleteBlocksCommand(new[] { grid.Blocks[3], grid.Blocks[0], grid.Blocks[4] }));

        Assert.Equal(new[] { "B1", "B2" }, Names(grid));
        Assert.Equal(new[] { "B1", "B2" }, XmlNames(grid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1, 3)]
    [InlineData(0, 1, 2, 3)]
    [InlineData(4, 0, 2)]
    public void Delete_then_undo_restores_the_exact_xml(params int[] indexes)
    {
        var bp = FiveBlocks();
        var grid = bp.Grids[0];
        var session = new EditorSession(bp);
        var before = BlueprintIO.ToXml(bp);

        session.Execute(new DeleteBlocksCommand(indexes.Select(i => grid.Blocks[i]).ToList()));
        Assert.Equal(5 - indexes.Length, grid.Blocks.Count);

        session.Undo();
        Assert.Equal(before, BlueprintIO.ToXml(bp));
        Assert.Equal(new[] { "B0", "B1", "B2", "B3", "B4" }, Names(grid));

        session.Redo();
        Assert.Equal(XmlNames(grid), Names(grid));
        session.Undo();
        Assert.Equal(before, BlueprintIO.ToXml(bp));
    }

    [Fact]
    public void Delete_across_two_grids_undoes_both()
    {
        var bp = FiveBlocks();
        var session = new EditorSession(bp);
        var before = BlueprintIO.ToXml(bp);

        var change = new DeleteBlocksCommand(new[] { bp.Grids[0].Blocks[1] });
        session.Execute(change);
        Assert.Throws<InvalidOperationException>(() => new DeleteBlocksCommand(bp.Grids[1].Blocks));

        session.Undo();
        Assert.Equal(before, BlueprintIO.ToXml(bp));
    }

    [Fact]
    public void Deleting_every_block_of_a_grid_is_refused()
    {
        var bp = FiveBlocks();
        Assert.Throws<InvalidOperationException>(() => new DeleteBlocksCommand(bp.Grids[0].Blocks));
    }

    [Fact]
    public void Dirty_flag_follows_save_point()
    {
        var bp = FiveBlocks();
        var session = new EditorSession(bp);
        Assert.False(session.IsDirty);

        session.Execute(new PaintBlocksCommand(bp.Grids[0].Blocks.Take(1), Vector3.Zero));
        Assert.True(session.IsDirty);
        Assert.Equal("Paint block", session.UndoName);

        session.Undo();
        Assert.False(session.IsDirty);
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void New_edit_clears_redo()
    {
        var bp = FiveBlocks();
        var session = new EditorSession(bp);
        session.Execute(new PaintBlocksCommand(bp.Grids[0].Blocks.Take(1), Vector3.Zero));
        session.Undo();
        session.Execute(new PaintBlocksCommand(bp.Grids[0].Blocks.Take(2), Vector3.One));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Changed_event_reports_grids_and_structure()
    {
        var bp = FiveBlocks();
        var session = new EditorSession(bp);
        var changes = new List<EditChange>();
        session.Changed += (_, c) => changes.Add(c);

        session.Execute(new PaintBlocksCommand(bp.Grids[0].Blocks.Take(1), Vector3.Zero));
        session.Execute(new DeleteBlocksCommand(bp.Grids[0].Blocks.Take(1)));

        Assert.False(changes[0].StructureChanged);
        Assert.True(changes[1].StructureChanged);
        Assert.Equal(bp.Grids[0], Assert.Single(changes[1].Grids));
    }

    [Fact]
    public void Save_as_new_creates_a_renamed_blueprint_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "sebp-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var original = Path.Combine(root, "Five");
            Directory.CreateDirectory(original);
            File.WriteAllText(Path.Combine(original, "bp.sbc"), BlueprintIO.ToXml(FiveBlocks()));
            File.WriteAllBytes(Path.Combine(original, "thumb.png"), new byte[] { 1, 2, 3 });

            var session = new EditorSession(BlueprintIO.Load(original));
            session.Execute(new PaintBlocksCommand(session.Blueprint.Grids[0].Blocks, Vector3.Zero));
            session.SaveAsNew(root, "Five v2");

            var copy = BlueprintIO.Load(Path.Combine(root, "Five v2"));
            Assert.Equal("Five v2", copy.Name);
            Assert.True(File.Exists(Path.Combine(root, "Five v2", "thumb.png")));
            Assert.False(session.IsDirty);
            Assert.Equal("Five", BlueprintIO.Load(original).Name); // original untouched
            Assert.Throws<IOException>(() => session.SaveAsNew(root, "Five v2"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
