using System.Numerics;

namespace SEBlueprint.Core.Models;

/// <summary>
/// Geometry for armor-style blocks. In the game these are assembled from per-face tiles at runtime,
/// so there is no single model file; instead each shape ("CubeTopology") is described here as one or two
/// convex solids given by their corner points.
///
/// Coordinates are in a unit block centered on the origin, in the block's default orientation:
/// x = Right, y = Up, z = Backward (so Front is z = -0.5). Shapes were derived from, and are checked
/// against, the mount points in the game's block definitions (see ArmorShapeTests).
/// Rounded shapes use their sharp-edged equivalents.
/// </summary>
public static class ArmorShapes
{
    private const float H = 0.5f;

    private static Vector3 P(float x, float y, float z) => new(x, y, z);

    public static readonly Vector3[] CubeCorners =
    {
        P(-H, -H, -H), P(H, -H, -H), P(-H, H, -H), P(H, H, -H),
        P(-H, -H, H), P(H, -H, H), P(-H, H, H), P(H, H, H),
    };

    private static readonly Vector3[] Bottom = { P(-H, -H, -H), P(H, -H, -H), P(-H, -H, H), P(H, -H, H) };

    private static Vector3[] CubeWithout(params Vector3[] removed) => CubeCorners.Where(c => !removed.Contains(c)).ToArray();

    private static Vector3[] With(this Vector3[] a, params Vector3[] more) => a.Concat(more).ToArray();

    /// <summary>Each shape as a list of convex pieces, each piece a list of corner points.</summary>
    private static readonly Dictionary<string, Vector3[][]> Pieces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Box"] = new[] { CubeCorners },
        ["StandaloneBox"] = new[] { CubeCorners },

        // Full front and bottom faces, sloping from the top-front edge to the bottom-back edge.
        ["Slope"] = new[] { Bottom.With(P(-H, H, -H), P(H, H, -H)) },
        ["RoundSlope"] = new[] { Bottom.With(P(-H, H, -H), P(H, H, -H)) },

        // Tetrahedron in the right-bottom-front corner.
        ["Corner"] = new[] { new[] { P(H, -H, -H), P(-H, -H, -H), P(H, H, -H), P(H, -H, H) } },
        ["RoundCorner"] = new[] { new[] { P(H, -H, -H), P(-H, -H, -H), P(H, H, -H), P(H, -H, H) } },

        // Cube with the right-bottom-front corner cut off.
        ["InvCorner"] = new[] { CubeWithout(P(H, -H, -H)) },
        ["RoundInvCorner"] = new[] { CubeWithout(P(H, -H, -H)) },

        // Half-thickness slab against the front face.
        ["HalfBox"] = new[] { new[] { P(-H, -H, -H), P(H, -H, -H), P(-H, H, -H), P(H, H, -H), P(-H, -H, 0), P(H, -H, 0), P(-H, H, 0), P(H, H, 0) } },

        // Small wedge in the bottom-front edge.
        ["HalfSlopeBox"] = new[] { new[] { P(-H, -H, -H), P(H, -H, -H), P(-H, 0, -H), P(H, 0, -H), P(-H, -H, 0), P(H, -H, 0) } },

        // 2:1 slope, split over two blocks.
        ["Slope2Base"] = new[] { Bottom.With(P(-H, H, -H), P(H, H, -H), P(-H, 0, H), P(H, 0, H)) },
        ["Slope2Tip"] = new[] { Bottom.With(P(-H, 0, -H), P(H, 0, -H)) },

        // 2:1 corner, split over two blocks.
        ["Corner2Base"] = new[] { new[] { P(-H, -H, -H), P(-H, H, -H), P(-H, -H, H), P(H, -H, -H), P(H, 0, -H), P(H, -H, 0) } },
        ["Corner2Tip"] = new[] { new[] { P(H, -H, -H), P(0, -H, -H), P(H, 0, -H), P(H, -H, H) } },

        // 2:1 inverted corner, split over two blocks.
        ["InvCorner2Base"] = new[] { CubeWithout(P(H, -H, -H)).With(P(0, -H, -H), P(H, 0, -H)) },
        ["InvCorner2Tip"] = new[] { CubeWithout(P(H, -H, -H), P(H, -H, H)).With(P(0, -H, H), P(H, 0, H)) },

        // Half-height triangular prism in the left-front.
        ["HalfCorner"] = new[] { new[] { P(-H, -H, -H), P(H, -H, -H), P(-H, -H, H), P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H) } },

        // Pyramid over the bottom face with its tip above the left-front corner.
        ["CornerSquare"] = new[] { Bottom.With(P(-H, H, -H)) },
        ["CornerSquareInverted"] = new[]
        {
            Bottom.With(P(-H, H, -H), P(-H, H, H)),
            Bottom.With(P(-H, H, -H), P(H, H, -H)),
        },

        // Low tetrahedron (half height) in the right-back-bottom corner.
        ["HalfSlopeCorner"] = new[] { new[] { P(H, -H, H), P(-H, -H, H), P(H, -H, -H), P(H, 0, H) } },
        // Cube with a low tetrahedron cut from the left-front-top corner.
        ["HalfSlopeCornerInverted"] = new[] { CubeWithout(P(-H, H, -H)).With(P(-H, 0, -H)) },

        ["HalfSlopedCorner"] = new[] { new[] { P(-H, -H, -H), P(H, -H, -H), P(-H, H, -H), P(-H, -H, H) } },
        ["HalfSlopedCornerBase"] = new[] { Bottom.With(P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H)) },
        ["SquareSlopedCornerTipInv"] = new[] { Bottom.With(P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H)) },

        // Cube with the top-right edge cut back to half.
        ["HalfSlopeInverted"] = new[] { CubeWithout(P(H, H, -H), P(H, H, H)).With(P(0, H, -H), P(0, H, H), P(H, 0, -H), P(H, 0, H)) },

        // Sloped top: full height at left-front, half at right-front and left-back, zero at right-back.
        ["SlopedCorner"] = new[] { Bottom.With(P(-H, H, -H), P(H, 0, -H), P(-H, 0, H)) },
        ["SlopeTransition"] = new[] { Bottom.With(P(-H, H, -H), P(H, 0, -H), P(-H, 0, H)) },
        ["SlopeTransitionMirrored"] = new[] { Bottom.With(P(-H, H, H), P(H, 0, H), P(-H, 0, -H)) },
        ["SlopeTransitionBase"] = new[] { Bottom.With(P(-H, H, -H), P(H, H, -H), P(-H, 0, H)) },
        ["SlopeTransitionBaseMirrored"] = new[] { Bottom.With(P(-H, H, H), P(H, H, H), P(-H, 0, -H)) },
        ["SlopeTransitionTip"] = new[] { new[] { P(-H, -H, -H), P(H, -H, -H), P(H, -H, 0), P(-H, -H, H), P(-H, 0, -H), P(H, 0, -H) } },
        ["SlopeTransitionTipMirrored"] = new[] { new[] { P(-H, -H, H), P(H, -H, H), P(H, -H, 0), P(-H, -H, -H), P(-H, 0, H), P(H, 0, H) } },

        // Cube with a low tetrahedron cut from the left-back-top corner.
        ["SlopedCornerBase"] = new[] { CubeWithout(P(-H, H, H)).With(P(-H, 0, H)) },
        // Low tetrahedron in the left-back-bottom corner.
        ["SlopedCornerTip"] = new[] { new[] { P(-H, -H, H), P(H, -H, H), P(-H, -H, -H), P(-H, 0, H) } },

        // Half-height block with a corner tetrahedron on top, peaking above the left-front corner.
        ["RaisedSlopedCorner"] = new[]
        {
            Bottom.With(P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H), P(H, 0, H)),
            new[] { P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H), P(-H, H, -H) },
        },
        ["SquareSlopedCornerBase"] = new[]
        {
            Bottom.With(P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H), P(H, 0, H)),
            new[] { P(-H, 0, -H), P(H, 0, -H), P(-H, 0, H), P(-H, H, -H) },
        },
        // Low pyramid with its tip above the left-front corner.
        ["SquareSlopedCornerTip"] = new[] { Bottom.With(P(-H, 0, -H)) },
    };

    private static readonly Dictionary<string, MeshData> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> KnownTopologies => Pieces.Keys;

    public static bool IsKnown(string? topology) => topology is not null && Pieces.ContainsKey(topology);

    /// <summary>The convex pieces (as corner points) of a shape, or null if the topology is unknown.</summary>
    public static IReadOnlyList<Vector3[]>? GetPieces(string topology) => Pieces.TryGetValue(topology, out var p) ? p : null;

    /// <summary>The mesh for a shape in the unit block, or null if the topology is unknown.</summary>
    public static MeshData? Get(string? topology)
    {
        if (topology is null || !Pieces.TryGetValue(topology, out var pieces))
            return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(topology, out var mesh))
                Cache[topology] = mesh = MeshData.Combine(pieces.Select(Hull).ToList());
            return mesh;
        }
    }

    /// <summary>
    /// Convex hull of a small point set, as a flat-shaded mesh with outward-facing triangles.
    /// Brute force (fine for the ≤ 10 points used here): every plane through three points that has all
    /// other points on one side is a face.
    /// </summary>
    public static MeshData Hull(IReadOnlyList<Vector3> points)
    {
        const float eps = 1e-4f;
        var pts = points.Distinct().ToArray();
        var center = pts.Aggregate(Vector3.Zero, (a, b) => a + b) / pts.Length;
        var planes = new List<(Vector3 N, float D)>();

        for (var i = 0; i < pts.Length; i++)
            for (var j = i + 1; j < pts.Length; j++)
                for (var k = j + 1; k < pts.Length; k++)
                {
                    var n = Vector3.Cross(pts[j] - pts[i], pts[k] - pts[i]);
                    if (n.LengthSquared() < 1e-10f)
                        continue;
                    n = Vector3.Normalize(n);
                    var d = Vector3.Dot(n, pts[i]);
                    if (Vector3.Dot(n, center) > d)
                    {
                        n = -n; // make it point away from the inside
                        d = -d;
                    }
                    if (pts.All(p => Vector3.Dot(n, p) <= d + eps) &&
                        !planes.Any(pl => Vector3.Dot(pl.N, n) > 1 - eps && MathF.Abs(pl.D - d) < eps))
                        planes.Add((n, d));
                }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        foreach (var (n, d) in planes)
        {
            // Points on this face, sorted counter-clockwise around the face center as seen from outside.
            var face = pts.Where(p => MathF.Abs(Vector3.Dot(n, p) - d) < eps).ToList();
            var fc = face.Aggregate(Vector3.Zero, (a, b) => a + b) / face.Count;
            var u = Vector3.Normalize(face[0] - fc);
            var v = Vector3.Cross(n, u);
            face.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
            float Angle(Vector3 p) => MathF.Atan2(Vector3.Dot(p - fc, v), Vector3.Dot(p - fc, u));

            for (var t = 1; t + 1 < face.Count; t++)
            {
                positions.Add(face[0]);
                positions.Add(face[t]);
                positions.Add(face[t + 1]);
                normals.Add(n);
                normals.Add(n);
                normals.Add(n);
            }
        }

        return new MeshData(positions.ToArray(), normals.ToArray(), Enumerable.Range(0, positions.Count).ToArray());
    }
}
