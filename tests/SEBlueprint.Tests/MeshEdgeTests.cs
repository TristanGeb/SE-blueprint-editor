using SEBlueprint.Core.Models;

namespace SEBlueprint.Tests;

public class MeshEdgeTests
{
    [Fact]
    public void Cube_has_twelve_edges_and_no_face_diagonals()
    {
        var edges = MeshEdges.FeatureEdges(MeshData.UnitCube);
        Assert.Equal(24, edges.Length); // 12 segments × 2 points

        // Every edge runs along one axis: a face diagonal would change two coordinates.
        for (var i = 0; i < edges.Length; i += 2)
        {
            var d = edges[i + 1] - edges[i];
            var changed = (MathF.Abs(d.X) > 1e-4f ? 1 : 0) + (MathF.Abs(d.Y) > 1e-4f ? 1 : 0) + (MathF.Abs(d.Z) > 1e-4f ? 1 : 0);
            Assert.Equal(1, changed);
        }
    }

    [Fact]
    public void Slope_has_the_nine_edges_of_a_wedge()
    {
        var slope = ArmorShapes.Get("Slope")!;
        Assert.Equal(18, MeshEdges.FeatureEdges(slope).Length);
    }
}
