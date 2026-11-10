using SEBlueprint.Core.Math;

namespace SEBlueprint.Core.Model;

/// <summary>
/// The six axis directions used by Space Engineers.
/// SE convention: Forward = -Z, Backward = +Z, Left = -X, Right = +X, Up = +Y, Down = -Y.
/// </summary>
public enum Base6Direction
{
    Forward,
    Backward,
    Left,
    Right,
    Up,
    Down,
}

public static class Base6DirectionExtensions
{
    public static Vector3I ToVector(this Base6Direction d) => d switch
    {
        Base6Direction.Forward => new(0, 0, -1),
        Base6Direction.Backward => new(0, 0, 1),
        Base6Direction.Left => new(-1, 0, 0),
        Base6Direction.Right => new(1, 0, 0),
        Base6Direction.Up => new(0, 1, 0),
        Base6Direction.Down => new(0, -1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };
}

/// <summary>
/// A block's rotation, stored in the blueprint as which way the block's Forward and Up point.
/// </summary>
public readonly record struct BlockOrientation(Base6Direction Forward, Base6Direction Up)
{
    public static readonly BlockOrientation Default = new(Base6Direction.Forward, Base6Direction.Up);

    /// <summary>Forward and Up must be perpendicular, otherwise the orientation is meaningless.</summary>
    public bool IsValid
    {
        get
        {
            var f = Forward.ToVector();
            var u = Up.ToVector();
            return f.X * u.X + f.Y * u.Y + f.Z * u.Z == 0;
        }
    }

    /// <summary>Where the block's local +X axis points in grid space.</summary>
    public Vector3I RightVector => Vector3I.Cross(Forward.ToVector(), Up.ToVector());

    /// <summary>Where the block's local +Y axis points in grid space.</summary>
    public Vector3I UpVector => Up.ToVector();

    /// <summary>Where the block's local +Z axis points in grid space (local +Z is Backward).</summary>
    public Vector3I BackwardVector => -Forward.ToVector();

    /// <summary>Rotates a vector from block-local space into grid space.</summary>
    public Vector3I Transform(Vector3I v) => RightVector * v.X + UpVector * v.Y + BackwardVector * v.Z;

    public override string ToString() => $"Forward={Forward}, Up={Up}";
}
