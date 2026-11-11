using SEBlueprint.Core.Editing;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class GridLinkTests
{
    /// <summary>
    /// Main grid: armor, a rotor base (10) holding a head (20) on "Arm", and a connector (30) docked
    /// to a connector (40) on "Drone". "Arm" also has a head (50) whose base isn't in the blueprint.
    /// </summary>
    private static Blueprint Linked() => BlueprintIO.Parse("""
        <?xml version="1.0"?>
        <Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <ShipBlueprints><ShipBlueprint>
            <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="Linked" />
            <CubeGrids>
              <CubeGrid><GridSizeEnum>Large</GridSizeEnum><CubeBlocks>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>LargeBlockArmorBlock</SubtypeName></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_MotorStator"><SubtypeName>LargeStator</SubtypeName><EntityId>10</EntityId><TopBlockId>20</TopBlockId><RotorEntityId xsi:nil="true" /></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_ShipConnector"><SubtypeName>Connector</SubtypeName><EntityId>30</EntityId><ConnectedEntityId>40</ConnectedEntityId></MyObjectBuilder_CubeBlock>
              </CubeBlocks><DisplayName>Main</DisplayName></CubeGrid>
              <CubeGrid><GridSizeEnum>Large</GridSizeEnum><CubeBlocks>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_MotorRotor"><SubtypeName>LargeRotor</SubtypeName><EntityId>20</EntityId><ParentEntityId>10</ParentEntityId></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_MotorRotor"><SubtypeName>LargeRotor</SubtypeName><EntityId>50</EntityId><ParentEntityId>999</ParentEntityId></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>LargeBlockArmorBlock</SubtypeName></MyObjectBuilder_CubeBlock>
              </CubeBlocks><DisplayName>Arm</DisplayName></CubeGrid>
              <CubeGrid><GridSizeEnum>Small</GridSizeEnum><CubeBlocks>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_ShipConnector"><SubtypeName>ConnectorSmall</SubtypeName><EntityId>40</EntityId><ConnectedEntityId>30</ConnectedEntityId></MyObjectBuilder_CubeBlock>
                <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock"><SubtypeName>SmallBlockArmorBlock</SubtypeName></MyObjectBuilder_CubeBlock>
              </CubeBlocks><DisplayName>Drone</DisplayName></CubeGrid>
            </CubeGrids>
          </ShipBlueprint></ShipBlueprints>
        </Definitions>
        """);

    private static Block ById(Blueprint bp, string id) => bp.AllBlocks.Single(b => b.EntityId == id);

    [Fact]
    public void Deleting_a_rotor_base_reports_its_head_once()
    {
        var bp = Linked();
        var broken = GridLinks.FindBrokenBy(bp, new[] { ById(bp, "10") });

        var link = Assert.Single(broken); // base→head and head→base are the same pair
        Assert.Same(ById(bp, "10"), link.Deleted);
        Assert.Same(ById(bp, "20"), link.Remaining);
        Assert.Equal(GridLinkKind.Mechanical, link.Kind);
    }

    [Fact]
    public void Deleting_a_rotor_head_reports_its_base()
    {
        var bp = Linked();
        var link = Assert.Single(GridLinks.FindBrokenBy(bp, new[] { ById(bp, "20") }));
        Assert.Same(ById(bp, "10"), link.Remaining);
    }

    [Fact]
    public void Deleting_a_docked_connector_reports_the_other_connector()
    {
        var bp = Linked();
        var link = Assert.Single(GridLinks.FindBrokenBy(bp, new[] { ById(bp, "40") }));
        Assert.Same(ById(bp, "30"), link.Remaining);
        Assert.Equal(GridLinkKind.Connector, link.Kind);
    }

    [Fact]
    public void Deleting_both_ends_or_unlinked_blocks_reports_nothing()
    {
        var bp = Linked();
        Assert.Empty(GridLinks.FindBrokenBy(bp, new[] { ById(bp, "10"), ById(bp, "20") }));
        Assert.Empty(GridLinks.FindBrokenBy(bp, bp.AllBlocks.Where(b => b.EntityId is null)));
        Assert.Empty(GridLinks.FindBrokenBy(bp, new[] { ById(bp, "50") })); // its base isn't in the blueprint
    }
}
