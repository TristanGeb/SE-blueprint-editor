using System.Numerics;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class BlueprintIOTests
{
    [Fact]
    public void Parses_grids_and_blocks()
    {
        var bp = BlueprintIO.Parse(SampleBlueprints.TwoBlocks);

        Assert.Equal("Test Ship", bp.Name);
        var grid = Assert.Single(bp.Grids);
        Assert.Equal("Test Grid", grid.DisplayName);
        Assert.Equal(GridSize.Large, grid.GridSize);
        Assert.Equal(2.5f, grid.CellSize);
        Assert.Equal(2, grid.Blocks.Count);
    }

    [Fact]
    public void Missing_fields_use_game_defaults()
    {
        var armor = BlueprintIO.Parse(SampleBlueprints.TwoBlocks).Grids[0].Blocks[0];

        Assert.Equal("CubeBlock", armor.TypeId);
        Assert.Equal(Vector3I.Zero, armor.Min);
        Assert.Equal(BlockOrientation.Default, armor.Orientation);
        Assert.Equal(Block.DefaultColorMaskHsv, armor.ColorMaskHsv);
        Assert.Null(armor.CustomName);
    }

    [Fact]
    public void Reads_block_fields()
    {
        var thruster = BlueprintIO.Parse(SampleBlueprints.TwoBlocks).Grids[0].Blocks[1];

        Assert.Equal("Thrust", thruster.TypeId);
        Assert.Equal("LargeBlockLargeThrust", thruster.SubtypeName);
        Assert.Equal(new Vector3I(-1, 2, 3), thruster.Min);
        Assert.Equal(new BlockOrientation(Base6Direction.Down, Base6Direction.Forward), thruster.Orientation);
        Assert.Equal(new Vector3(0.5f, 0.2f, 0.45f), thruster.ColorMaskHsv);
        Assert.Equal("Main Thruster", thruster.CustomName);
    }

    [Fact]
    public void Round_trip_keeps_fields_the_editor_does_not_know_about()
    {
        var bp = BlueprintIO.Parse(SampleBlueprints.TwoBlocks);
        bp.Grids[0].Blocks[1].ColorMaskHsv = new Vector3(0.1f, 0f, 0f);

        var xml = BlueprintIO.ToXml(bp);
        var reloaded = BlueprintIO.Parse(xml);

        Assert.Contains("<SomeFutureField answer=\"42\">keep me</SomeFutureField>", xml);
        Assert.Contains("<DestructibleBlocks>true</DestructibleBlocks>", xml);
        Assert.Equal(new Vector3(0.1f, 0f, 0f), reloaded.Grids[0].Blocks[1].ColorMaskHsv);
    }

    [Fact]
    public void Writing_a_missing_field_inserts_it_in_the_games_order()
    {
        var bp = BlueprintIO.Parse(SampleBlueprints.TwoBlocks);
        var armor = bp.Grids[0].Blocks[0];

        armor.Min = new Vector3I(4, 5, 6);
        armor.ColorMaskHsv = new Vector3(0.3f, 0f, 0f);

        var names = armor.Element.Elements().Select(e => e.Name.LocalName).ToArray();
        Assert.Equal(new[] { "SubtypeName", "Min", "ColorMaskHSV" }, names);
        Assert.Equal(new Vector3I(4, 5, 6), BlueprintIO.Parse(BlueprintIO.ToXml(bp)).Grids[0].Blocks[0].Min);
    }

    [Fact]
    public void Numbers_are_written_with_a_dot_even_on_comma_locales()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var bp = BlueprintIO.Parse(SampleBlueprints.TwoBlocks);
            bp.Grids[0].Blocks[0].ColorMaskHsv = new Vector3(0.25f, -0.5f, 0.125f);

            var xml = BlueprintIO.ToXml(bp);

            Assert.Contains("x=\"0.25\" y=\"-0.5\" z=\"0.125\"", xml);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Save_makes_backup_and_deletes_binary_cache()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sebp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var bpPath = Path.Combine(folder, BlueprintIO.BlueprintFileName);
            File.WriteAllText(bpPath, SampleBlueprints.TwoBlocks);
            File.WriteAllText(Path.Combine(folder, BlueprintIO.BinaryCacheFileName), "stale cache");

            var bp = BlueprintIO.Load(folder);
            bp.Grids[0].Blocks[0].Min = new Vector3I(9, 9, 9);
            BlueprintIO.Save(bp);

            Assert.False(File.Exists(Path.Combine(folder, BlueprintIO.BinaryCacheFileName)));
            Assert.Equal(SampleBlueprints.TwoBlocks, File.ReadAllText(Path.Combine(folder, BlueprintIO.BackupFileName)));
            Assert.Equal(new Vector3I(9, 9, 9), BlueprintIO.Load(bpPath).Grids[0].Blocks[0].Min);
            Assert.False(File.Exists(bpPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
