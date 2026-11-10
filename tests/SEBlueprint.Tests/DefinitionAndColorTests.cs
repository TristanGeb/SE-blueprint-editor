using System.Numerics;
using System.Xml.Linq;
using SEBlueprint.Core.Colors;
using SEBlueprint.Core.Definitions;
using SEBlueprint.Core.IO;
using SEBlueprint.Core.Math;
using SEBlueprint.Core.Model;

namespace SEBlueprint.Tests;

public class DefinitionTests
{
    private static DefinitionRepository LoadSample()
    {
        var loc = new Localization(new Dictionary<string, string> { ["DisplayName_Block_LargeThrust"] = "Large Ion Thruster" });
        var repo = new DefinitionRepository();
        repo.LoadDocument(XDocument.Parse(SampleBlueprints.Definitions), loc);
        return repo;
    }

    [Fact]
    public void Reads_both_id_styles_and_translates_names()
    {
        var repo = LoadSample();

        var thrust = repo.Get("Thrust", "LargeBlockLargeThrust", GridSize.Large);
        Assert.Equal("Large Ion Thruster", thrust.DisplayName);
        Assert.Equal(new Vector3I(3, 2, 4), thrust.Size);

        var armor = repo.Get("CubeBlock", "LargeBlockArmorBlock", GridSize.Large);
        Assert.Equal("Light Armor Block", armor.DisplayName);
        Assert.False(armor.IsUnknown);
    }

    [Fact]
    public void Falls_back_to_subtype_when_type_differs()
    {
        var def = LoadSample().Get("TerminalBlock", "LargeBlockLargeThrust", GridSize.Large);
        Assert.Equal("Thrust", def.TypeId);
    }

    [Fact]
    public void Unknown_blocks_get_a_one_cell_placeholder()
    {
        var def = LoadSample().Get("ModdedThing", "SuperGun", GridSize.Small);
        Assert.True(def.IsUnknown);
        Assert.Equal(Vector3I.One, def.Size);
        Assert.Equal("SuperGun", def.DisplayName);
    }

    [Fact]
    public void Looks_up_blocks_from_a_blueprint()
    {
        var repo = LoadSample();
        var bp = BlueprintIO.Parse(SampleBlueprints.TwoBlocks);
        Assert.All(bp.AllBlocks, b => Assert.True(repo.Contains(b)));
    }
}

public class ColorTests
{
    [Fact]
    public void Default_grey_paint_is_a_neutral_grey()
    {
        var rgb = SeColor.MaskToRgb(new Vector3(0f, -0.8f, -0.13f));
        Assert.Equal(rgb.X, rgb.Y, 3);
        Assert.Equal(rgb.Y, rgb.Z, 3);
        Assert.Equal(0.42f, rgb.X, 3);
    }

    [Fact]
    public void Full_red_converts_to_red()
    {
        // hue 0, saturation 1 (stored 0.2), value 1 (stored 0.45)
        var rgb = SeColor.MaskToRgb(new Vector3(0f, 0.2f, 0.45f));
        Assert.Equal(new Vector3(1f, 0f, 0f), rgb);
    }

    [Theory]
    [InlineData(0.0f, 1f, 1f)]
    [InlineData(0.33f, 0.5f, 0.8f)]
    [InlineData(0.75f, 0.25f, 0.3f)]
    public void Hsv_and_rgb_round_trip(float h, float s, float v)
    {
        var back = SeColor.RgbToHsv(SeColor.HsvToRgb(new Vector3(h, s, v)));
        Assert.Equal(h, back.X, 3);
        Assert.Equal(s, back.Y, 3);
        Assert.Equal(v, back.Z, 3);
    }

    [Fact]
    public void Mask_and_hsv_round_trip()
    {
        var mask = new Vector3(0.6f, -0.3f, 0.1f);
        var back = SeColor.HsvToMask(SeColor.MaskToHsv(mask));
        Assert.Equal(mask.X, back.X, 4);
        Assert.Equal(mask.Y, back.Y, 4);
        Assert.Equal(mask.Z, back.Z, 4);
    }
}
