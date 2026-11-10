using System.Numerics;

namespace SEBlueprint.Core.Models;

/// <summary>
/// A renderable triangle mesh: one position and normal per corner, three corners per triangle
/// (counter-clockwise when seen from outside). Kept free of any rendering library so it can be built
/// on a background thread and tested.
/// </summary>
public sealed class MeshData
{
    public MeshData(Vector3[] positions, Vector3[] normals, int[] indices)
    {
        Positions = positions;
        Normals = normals;
        Indices = indices;
    }

    public Vector3[] Positions { get; }

    public Vector3[] Normals { get; }

    public int[] Indices { get; }

    public int TriangleCount => Indices.Length / 3;

    /// <summary>
    /// Builds a mesh from indexed triangles, computing normals. Corners whose faces meet at less than
    /// <paramref name="smoothingAngleDegrees"/> share a smoothed normal (curved surfaces look round);
    /// sharper edges keep separate normals (hard edges stay crisp).
    /// </summary>
    public static MeshData FromTriangles(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices, float smoothingAngleDegrees = 35f)
    {
        var triangleCount = indices.Count / 3;
        var faceNormals = new Vector3[triangleCount];
        var valid = new bool[triangleCount];
        for (var t = 0; t < triangleCount; t++)
        {
            int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
            if ((uint)a >= positions.Count || (uint)b >= positions.Count || (uint)c >= positions.Count)
                continue;
            var n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            var len = n.Length();
            if (len < 1e-12f)
                continue; // degenerate
            faceNormals[t] = n / len;
            valid[t] = true;
        }

        // Group corners by position (models often duplicate vertices along seams).
        var byPosition = new Dictionary<Vector3, List<int>>();
        for (var t = 0; t < triangleCount; t++)
        {
            if (!valid[t])
                continue;
            for (var k = 0; k < 3; k++)
            {
                var p = positions[indices[3 * t + k]];
                if (!byPosition.TryGetValue(p, out var list))
                    byPosition[p] = list = new List<int>(6);
                list.Add(t);
            }
        }

        var cosLimit = MathF.Cos(smoothingAngleDegrees * MathF.PI / 180f);
        var outPositions = new List<Vector3>(triangleCount * 3);
        var outNormals = new List<Vector3>(triangleCount * 3);
        for (var t = 0; t < triangleCount; t++)
        {
            if (!valid[t])
                continue;
            var fn = faceNormals[t];
            for (var k = 0; k < 3; k++)
            {
                var p = positions[indices[3 * t + k]];
                var sum = Vector3.Zero;
                foreach (var other in byPosition[p])
                {
                    if (Vector3.Dot(faceNormals[other], fn) >= cosLimit)
                        sum += faceNormals[other];
                }
                outPositions.Add(p);
                outNormals.Add(sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : fn);
            }
        }

        var outIndices = new int[outPositions.Count];
        for (var i = 0; i < outIndices.Length; i++)
            outIndices[i] = i;
        return new MeshData(outPositions.ToArray(), outNormals.ToArray(), outIndices);
    }

    /// <summary>Combines several meshes into one.</summary>
    public static MeshData Combine(IReadOnlyList<MeshData> meshes)
    {
        if (meshes.Count == 1)
            return meshes[0];
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        foreach (var m in meshes)
        {
            var offset = positions.Count;
            positions.AddRange(m.Positions);
            normals.AddRange(m.Normals);
            indices.AddRange(m.Indices.Select(i => i + offset));
        }
        return new MeshData(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    /// <summary>A unit cube centered on the origin (each side 1), used for unknown blocks and as a fallback.</summary>
    public static MeshData UnitCube { get; } = ArmorShapes.Hull(ArmorShapes.CubeCorners);
}
