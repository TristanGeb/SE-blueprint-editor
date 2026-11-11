using System.Text;
using System.Xml.Linq;
using SEBlueprint.Core.Analysis;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class ArmorAnalysisTests
{
    private static DefinitionRepository Definitions()
    {
        var repo = new DefinitionRepository();
        repo.LoadComponents(XDocument.Parse("""
            <Definitions><Components>
              <Component><Id><TypeId>Component</TypeId><SubtypeId>SteelPlate</SubtypeId></Id><MaxIntegrity>100</MaxIntegrity></Component>
              <Component><Id><TypeId>Component</TypeId><SubtypeId>Reactor</SubtypeId></Id><MaxIntegrity>200</MaxIntegrity></Component>
            </Components></Definitions>
            """));
        repo.LoadDocument(XDocument.Parse("""
            <Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><CubeBlocks>
              <Definition>
                <Id><TypeId>CubeBlock</TypeId><SubtypeId>LargeBlockArmorBlock</SubtypeId></Id>
                <CubeSize>Large</CubeSize><Size x="1" y="1" z="1" />
                <CubeDefinition><CubeTopology>Box</CubeTopology></CubeDefinition>
                <Components><Component Subtype="SteelPlate" Count="25" /></Components>
              </Definition>
              <Definition>
                <Id><TypeId>Reactor</TypeId><SubtypeId>SmallReactor</SubtypeId></Id>
                <CubeSize>Large</CubeSize><Size x="1" y="1" z="1" />
                <Components><Component Subtype="Reactor" Count="5" /><Component Subtype="SteelPlate" Count="10" /></Components>
              </Definition>
            </CubeBlocks></Definitions>
            """), Localization.Empty);
        return repo;
    }

    /// <summary>A reactor at (0,0,0) inside a 3×3×3 cube of light armor.</summary>
    private static Blueprint ArmoredReactor()
    {
        var blocks = new StringBuilder();
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                for (var z = -1; z <= 1; z++)
                {
                    var (type, subtype) = x == 0 && y == 0 && z == 0
                        ? ("MyObjectBuilder_Reactor", "SmallReactor")
                        : ("MyObjectBuilder_CubeBlock", "LargeBlockArmorBlock");
                    blocks.Append($"""<MyObjectBuilder_CubeBlock xsi:type="{type}"><SubtypeName>{subtype}</SubtypeName><Min x="{x}" y="{y}" z="{z}" /></MyObjectBuilder_CubeBlock>""");
                }
        return BlueprintIO.Parse($"""
            <?xml version="1.0"?>
            <Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><ShipBlueprints><ShipBlueprint>
              <Id Subtype="Box" /><CubeGrids><CubeGrid><GridSizeEnum>Large</GridSizeEnum>
              <CubeBlocks>{blocks}</CubeBlocks></CubeGrid></CubeGrids>
            </ShipBlueprint></ShipBlueprints></Definitions>
            """);
    }

    private static Block At(Blueprint bp, int x, int y, int z) => bp.AllBlocks.Single(b => b.Min == new Vector3I(x, y, z));

    [Fact]
    public void Block_hit_points_come_from_components()
    {
        var defs = Definitions();
        Assert.Equal(2500f, defs.Get("CubeBlock", "LargeBlockArmorBlock", GridSize.Large).EffectiveHitPoints);
        Assert.Equal(2000f, defs.Get("Reactor", "SmallReactor", GridSize.Large).EffectiveHitPoints);
    }

    [Fact]
    public void Enclosed_reactor_is_shielded_by_one_armor_layer()
    {
        var bp = ArmoredReactor();
        var result = ArmorAnalysis.Analyze(bp, Definitions(), ArmorAnalysisKind.Exposure);

        var reactor = Assert.Single(result.Measurements.Values);
        Assert.Equal(At(bp, 0, 0, 0), reactor.Block);
        Assert.Equal(2500f, reactor.HitPoints);
    }

    [Fact]
    public void Removing_one_armor_block_exposes_the_reactor_from_that_side()
    {
        var bp = ArmoredReactor();
        var defs = Definitions();
        var hole = At(bp, 1, 0, 0);
        new SEBlueprint.Core.Editing.DeleteBlocksCommand(new[] { hole }).Do();

        var reactor = ArmorAnalysis.Analyze(bp, defs, ArmorAnalysisKind.Exposure).Measurements[At(bp, 0, 0, 0)];

        Assert.Equal(0f, reactor.HitPoints);
        Assert.Equal(new Vector3I(1, 0, 0), reactor.WeakestDirection);
    }

    [Fact]
    public void Diagonal_rays_do_not_slip_between_blocks_that_only_touch_at_edges()
    {
        // Remove the corner block: the corner is now empty, but every block around it is armor, so a shot
        // aimed diagonally still has to go through one of the blocks meeting at that corner.
        var bp = ArmoredReactor();
        new SEBlueprint.Core.Editing.DeleteBlocksCommand(new[] { At(bp, 1, 1, 1) }).Do();

        var reactor = ArmorAnalysis.Analyze(bp, Definitions(), ArmorAnalysisKind.Exposure).Measurements[At(bp, 0, 0, 0)];

        Assert.Equal(2500f, reactor.HitPoints);
    }

    [Fact]
    public void Thickness_measures_armor_behind_each_outer_face()
    {
        var bp = ArmoredReactor();
        var result = ArmorAnalysis.Analyze(bp, Definitions(), ArmorAnalysisKind.Thickness);

        // Face centre: only itself before reaching the reactor.
        Assert.Equal(2500f, result.Measurements[At(bp, 1, 0, 0)].HitPoints);
        // Corner: three armor blocks in a row behind every outer face.
        Assert.Equal(7500f, result.Measurements[At(bp, 1, 1, 1)].HitPoints);
        // Functional blocks are not part of a thickness analysis.
        Assert.False(result.Measurements.ContainsKey(At(bp, 0, 0, 0)));
    }

    [Fact]
    public void Weakest_lists_lowest_first()
    {
        var bp = ArmoredReactor();
        var result = ArmorAnalysis.Analyze(bp, Definitions(), ArmorAnalysisKind.Thickness);
        var values = result.Weakest.Select(m => m.HitPoints).ToList();
        Assert.Equal(values.OrderBy(v => v), values);
    }
}
