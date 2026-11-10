namespace SEBlueprint.Core.Math;

/// <summary>An integer 3D vector, used for grid cell coordinates and block sizes.</summary>
public readonly record struct Vector3I(int X, int Y, int Z)
{
    public static readonly Vector3I Zero = new(0, 0, 0);
    public static readonly Vector3I One = new(1, 1, 1);

    public static Vector3I operator +(Vector3I a, Vector3I b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3I operator -(Vector3I a, Vector3I b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3I operator -(Vector3I a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3I operator *(Vector3I a, int s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vector3I Min(Vector3I a, Vector3I b) =>
        new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Min(a.Z, b.Z));

    public static Vector3I Max(Vector3I a, Vector3I b) =>
        new(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y), System.Math.Max(a.Z, b.Z));

    public Vector3I Abs() => new(System.Math.Abs(X), System.Math.Abs(Y), System.Math.Abs(Z));

    public static Vector3I Cross(Vector3I a, Vector3I b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public override string ToString() => $"({X}, {Y}, {Z})";
}
