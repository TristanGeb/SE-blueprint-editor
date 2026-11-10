using System.Numerics;
using System.Text;
using SEBlueprint.Core.Models;

namespace SEBlueprint.Tests;

public class ArmorShapeTests
{
    /// <summary>Signed volume via the divergence theorem; positive only if triangles face outward.</summary>
    private static double Volume(MeshData m)
    {
        double v = 0;
        for (var i = 0; i < m.Indices.Length; i += 3)
            v += Vector3.Dot(m.Positions[m.Indices[i]], Vector3.Cross(m.Positions[m.Indices[i + 1]], m.Positions[m.Indices[i + 2]])) / 6.0;
        return v;
    }

    [Theory]
    [InlineData("Box", 1.0)]
    [InlineData("Slope", 0.5)]
    [InlineData("Corner", 1.0 / 6)]
    [InlineData("InvCorner", 5.0 / 6)]
    [InlineData("HalfBox", 0.5)]
    [InlineData("HalfSlopeBox", 0.125)]
    [InlineData("Slope2Base", 0.75)]
    [InlineData("Slope2Tip", 0.25)]
    [InlineData("CornerSquare", 1.0 / 3)]
    public void Shapes_have_the_right_volume_and_face_outward(string topology, double expected)
    {
        var mesh = ArmorShapes.Get(topology)!;
        Assert.Equal(expected, Volume(mesh), 3);
    }

    [Fact]
    public void Every_shape_is_inside_the_unit_block_with_positive_volume()
    {
        foreach (var topology in ArmorShapes.KnownTopologies)
        {
            var mesh = ArmorShapes.Get(topology)!;
            // Smallest real shape is Corner2Tip: a ½ × ½ × 1 tetrahedron, volume 1/24.
            Assert.True(Volume(mesh) > 0.04, $"{topology} has volume {Volume(mesh)}");
            Assert.All(mesh.Positions, p => Assert.True(MathF.Abs(p.X) <= 0.5f && MathF.Abs(p.Y) <= 0.5f && MathF.Abs(p.Z) <= 0.5f, topology));
        }
    }

    [Fact]
    public void Hull_normals_point_away_from_the_center()
    {
        var mesh = ArmorShapes.Get("InvCorner")!;
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var center = (mesh.Positions[i] + mesh.Positions[i + 1] + mesh.Positions[i + 2]) / 3f;
            Assert.True(Vector3.Dot(mesh.Normals[i], center) > 0);
        }
    }

    [Fact]
    public void Unknown_topology_returns_null() => Assert.Null(ArmorShapes.Get("NotAShape"));
}

public class MeshDataTests
{
    [Fact]
    public void Sharp_edges_keep_separate_normals_and_shallow_ones_are_smoothed()
    {
        // Two triangles sharing an edge, folded 90°: normals must stay apart.
        var positions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
        var folded = MeshData.FromTriangles(positions, new[] { 0, 1, 2, 0, 3, 1 });
        Assert.Equal(new Vector3(0, 0, 1), folded.Normals[0]);
        Assert.Equal(new Vector3(0, 1, 0), folded.Normals[3]);

        // Folded only ~11°: the shared corners get one averaged normal.
        var shallow = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0.5f, -1, 0.2f) };
        var smooth = MeshData.FromTriangles(shallow, new[] { 0, 1, 2, 0, 3, 1 });
        Assert.Equal(smooth.Normals[0], smooth.Normals[3]);
    }

    [Fact]
    public void Degenerate_and_out_of_range_triangles_are_dropped()
    {
        var positions = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
        var mesh = MeshData.FromTriangles(positions, new[] { 0, 1, 2, 0, 0, 1, 0, 1, 9 });
        Assert.Equal(1, mesh.TriangleCount);
    }
}

public class MwmReaderTests
{
    /// <summary>Writes a minimal .mwm the way the game lays it out: debug header, section index, sections.</summary>
    private static byte[] BuildMwm(bool withLods = true)
    {
        var sections = new List<(string Tag, Action<BinaryWriter> Write)>
        {
            ("Vertices", w =>
            {
                var verts = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1.25f, 0) };
                w.Write(verts.Length);
                foreach (var v in verts)
                {
                    w.Write(BitConverter.HalfToUInt16Bits((Half)v.X));
                    w.Write(BitConverter.HalfToUInt16Bits((Half)v.Y));
                    w.Write(BitConverter.HalfToUInt16Bits((Half)v.Z));
                    w.Write(BitConverter.HalfToUInt16Bits((Half)1f));
                }
            }),
            ("BoundingBox", w => { w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(1.25f); w.Write(0f); }),
            ("SwapWindingOrder", w => w.Write(false)),
            ("MeshParts", w =>
            {
                w.Write(2); // two parts
                // part 1: plain material
                w.Write(123); w.Write(3); w.Write(0); w.Write(1); w.Write(2);
                w.Write(true); w.Write("PaintedMetal_Colorable");
                w.Write(1); w.Write("ColorMetalTexture"); w.Write(@"Textures\x_cm.dds");
                w.Write(0); w.Write("MESH");
                // part 2: glass has three extra fields
                w.Write(456); w.Write(3); w.Write(2); w.Write(1); w.Write(0);
                w.Write(true); w.Write("GenericGlass");
                w.Write(0); w.Write(0); w.Write("GLASS"); w.Write(""); w.Write(""); w.Write(true);
            }),
            ("LODs", w =>
            {
                w.Write(withLods ? 1 : 0);
                if (withLods) { w.Write(20f); w.Write(@"Models\Test_LOD1"); w.Write(""); }
            }),
            ("Dummies", w =>
            {
                w.Write(1);
                w.Write("subpart_Head");
                // Row-vector matrix: 0.1 scale on the axes (the dummy box size), translation (0, 2, 0)
                foreach (var f in new[] { 0.1f, 0, 0, 0, 0, 0.1f, 0, 0, 0, 0, 0.1f, 0, 0, 2f, 0, 1 })
                    w.Write(f);
                w.Write(2);
                w.Write("file"); w.Write("TestHead");
                w.Write("IsNull"); w.Write("True");
            }),
            ("ModelInfo", w => w.Write(0)),
        };

        // Lay out: header, index (with placeholder offsets), then sections; then patch offsets.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write("Debug"); w.Write(1); w.Write("Version:01157002");
        w.Write(sections.Count);
        var offsetSlots = new List<long>();
        foreach (var (tag, _) in sections)
        {
            w.Write(tag);
            offsetSlots.Add(ms.Position);
            w.Write(0);
        }
        var offsets = new List<int>();
        foreach (var (tag, write) in sections)
        {
            offsets.Add((int)ms.Position);
            w.Write(tag);
            write(w);
        }
        for (var i = 0; i < offsets.Count; i++)
        {
            ms.Position = offsetSlots[i];
            w.Write(offsets[i]);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Reads_positions_parts_materials_and_lods()
    {
        var model = MwmReader.Read(new MemoryStream(BuildMwm()));

        Assert.Equal(1157002, model.Version);
        Assert.Equal(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1.25f, 0) }, model.Positions);
        Assert.Equal(2, model.Parts.Count);
        Assert.Equal("PaintedMetal_Colorable", model.Parts[0].MaterialName);
        Assert.Equal(@"Textures\x_cm.dds", model.Parts[0].Textures["ColorMetalTexture"]);
        Assert.Equal("GLASS", model.Parts[1].Technique);
        Assert.Equal(new[] { 2, 1, 0 }, model.Parts[1].Indices);
        Assert.Equal(new Vector3(1, 1.25f, 0), model.BoundsMax);
        Assert.Equal(@"Models\Test_LOD1", Assert.Single(model.Lods).Model);
        Assert.Equal(2, model.TriangleCount);
    }

    [Fact]
    public void Reads_subpart_dummies()
    {
        var model = MwmReader.Read(new MemoryStream(BuildMwm()));

        var dummy = Assert.Single(model.Dummies);
        Assert.Equal("subpart_Head", dummy.Name);
        Assert.Equal("TestHead", dummy.Properties["file"]);
        Assert.Equal(new Vector3(0, 2, 0), dummy.Matrix.Translation);
        Assert.Equal(0.1f, dummy.Matrix.M11);
    }

    [Fact]
    public void Rejects_files_that_are_not_models()
    {
        Assert.Throws<InvalidDataException>(() => MwmReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("\u0005Hello"))));
    }

    [Theory]
    [InlineData("PaintedMetal_Colorable", "MESH", PartKind.Paint)]
    [InlineData("PaintedMetal_Darker", "MESH", PartKind.Fixed)]
    [InlineData("Emissive", "MESH", PartKind.Emissive)]
    [InlineData("GenericGlass", "GLASS", PartKind.Glass)]
    [InlineData("LargeButtonPanel_LOD2", "MESH", PartKind.Paint)]
    public void Classifies_materials(string material, string technique, PartKind expected)
    {
        Assert.Equal(expected, PartAppearance.FromMaterial(material, technique)!.Value.Kind);
    }

    [Fact]
    public void Decals_are_not_drawn() => Assert.Null(PartAppearance.FromMaterial("Atlas3_Colorable", "DECAL_CUTOUT"));
}
