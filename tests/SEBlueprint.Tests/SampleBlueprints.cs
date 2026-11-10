namespace SEBlueprint.Tests;

/// <summary>Small hand-written blueprints in the same format the game writes.</summary>
internal static class SampleBlueprints
{
    public const string TwoBlocks = """
        <?xml version="1.0"?>
        <Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <ShipBlueprints>
            <ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">
              <Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="Test Ship" />
              <DisplayName>Tester</DisplayName>
              <CubeGrids>
                <CubeGrid>
                  <SubtypeName />
                  <EntityId>1</EntityId>
                  <PersistentFlags>CastShadows InScene</PersistentFlags>
                  <PositionAndOrientation>
                    <Position x="100.5" y="-20" z="3000" />
                    <Forward x="0" y="0" z="-1" />
                    <Up x="0" y="1" z="0" />
                  </PositionAndOrientation>
                  <GridSizeEnum>Large</GridSizeEnum>
                  <CubeBlocks>
                    <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock">
                      <SubtypeName>LargeBlockArmorBlock</SubtypeName>
                    </MyObjectBuilder_CubeBlock>
                    <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_Thrust">
                      <SubtypeName>LargeBlockLargeThrust</SubtypeName>
                      <EntityId>2</EntityId>
                      <Min x="-1" y="2" z="3" />
                      <BlockOrientation Forward="Down" Up="Forward" />
                      <ColorMaskHSV x="0.5" y="0.2" z="0.45" />
                      <CustomName>Main Thruster</CustomName>
                      <ShowInTerminal>true</ShowInTerminal>
                      <SomeFutureField answer="42">keep me</SomeFutureField>
                    </MyObjectBuilder_CubeBlock>
                  </CubeBlocks>
                  <DisplayName>Test Grid</DisplayName>
                  <DestructibleBlocks>true</DestructibleBlocks>
                </CubeGrid>
              </CubeGrids>
              <WorkshopId>0</WorkshopId>
            </ShipBlueprint>
          </ShipBlueprints>
        </Definitions>
        """;

    public const string Definitions = """
        <?xml version="1.0" encoding="utf-8"?>
        <Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <CubeBlocks>
            <Definition xsi:type="MyObjectBuilder_ThrustDefinition">
              <Id>
                <TypeId>Thrust</TypeId>
                <SubtypeId>LargeBlockLargeThrust</SubtypeId>
              </Id>
              <DisplayName>DisplayName_Block_LargeThrust</DisplayName>
              <CubeSize>Large</CubeSize>
              <Size x="3" y="2" z="4" />
            </Definition>
            <Definition>
              <Id Type="MyObjectBuilder_CubeBlock" Subtype="LargeBlockArmorBlock" />
              <DisplayName>Light Armor Block</DisplayName>
              <CubeSize>Large</CubeSize>
              <Size x="1" y="1" z="1" />
            </Definition>
          </CubeBlocks>
        </Definitions>
        """;
}
