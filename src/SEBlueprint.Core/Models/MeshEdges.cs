using System.Numerics;

namespace SEBlueprint.Core.Models;

/// <summary>Finds the visible edges of a mesh, for drawing block outlines.</summary>
public static class MeshEdges
{
    /// <summary>
    /// The mesh's "feature" edges as line segments (pairs of points): edges where two faces meet at an angle
    /// of at least <paramref name="minAngleDegrees"/>, plus open edges that belong to only one face.
    /// The diagonals that split a flat face into triangles are left out.
    /// </summary>
    public static Vector3[] FeatureEdges(MeshData mesh, float minAngleDegrees = 10f)
    {
        var maxDot = MathF.Cos(minAngleDegrees * MathF.PI / 180f);
        var positions = mesh.Positions;
        var indices = mesh.Indices;

        // Meshes duplicate corners per face (for flat normals), so weld by rounded position first.
        var weld = new Dictionary<(int, int, int), int>();
        var welded = new int[positions.Length];
        var points = new List<Vector3>();
        for (var i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            var key = ((int)MathF.Round(p.X * 10000f), (int)MathF.Round(p.Y * 10000f), (int)MathF.Round(p.Z * 10000f));
            if (!weld.TryGetValue(key, out var id))
            {
                weld[key] = id = points.Count;
                points.Add(p);
            }
            welded[i] = id;
        }

        var faces = new Dictionary<(int, int), List<Vector3>>();
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = welded[indices[t]], b = welded[indices[t + 1]], c = welded[indices[t + 2]];
            var n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            if (n.LengthSquared() < 1e-12f)
                continue; // degenerate triangle
            n = Vector3.Normalize(n);
            AddFace(a, b, n);
            AddFace(b, c, n);
            AddFace(c, a, n);
        }

        var result = new List<Vector3>();
        foreach (var ((a, b), normals) in faces)
        {
            if (IsFeature(normals, maxDot))
            {
                result.Add(points[a]);
                result.Add(points[b]);
            }
        }
        return result.ToArray();

        void AddFace(int a, int b, Vector3 normal)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!faces.TryGetValue(key, out var list))
                faces[key] = list = new List<Vector3>(2);
            list.Add(normal);
        }
    }

    private static bool IsFeature(List<Vector3> normals, float maxDot)
    {
        if (normals.Count == 1)
            return true; // open edge
        for (var i = 0; i < normals.Count; i++)
            for (var j = i + 1; j < normals.Count; j++)
                if (Vector3.Dot(normals[i], normals[j]) < maxDot)
                    return true;
        return false;
    }
}
