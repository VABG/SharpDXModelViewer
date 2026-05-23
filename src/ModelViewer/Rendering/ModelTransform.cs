using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Encapsulates a model-space (world) transform: position, rotation (as Euler angles),
/// and uniform scale.  Computes the combined world matrix on demand.
/// </summary>
public struct ModelTransform
{
    /// <summary>Default identity transform.</summary>
    public static readonly ModelTransform Identity = new(
        Vector3.Zero, Vector3.Zero, 1.0f);

    /// <summary>Translation component (world-space position).</summary>
    public Vector3 Position;

    /// <summary>Rotation component as Euler angles (radians) in X-Y-Z order.</summary>
    public Vector3 Rotation;

    /// <summary>Uniform scale factor.</summary>
    public float Scale;

    public ModelTransform(Vector3 position, Vector3 rotation, float scale = 1.0f)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    /// <summary>
    /// Computes the combined world matrix: Scale × Rotation(X,Y,Z) × Translation.
    /// Result is in row-major order ready for HLSL column-major consumption.
    /// </summary>
    public Matrix ToMatrix()
    {
        var result = Matrix.Identity;

        // Translation
        result.M41 = Position.X;
        result.M42 = Position.Y;
        result.M43 = Position.Z;

        // Rotation: apply Z, then Y, then X (intrinsic rotations)
        var cosX = MathF.Cos(Rotation.X);
        var sinX = MathF.Sin(Rotation.X);
        var cosY = MathF.Cos(Rotation.Y);
        var sinY = MathF.Sin(Rotation.Y);
        var cosZ = MathF.Cos(Rotation.Z);
        var sinZ = MathF.Sin(Rotation.Z);

        // Combined rotation matrix (XYZ order)
        result.M11 = cosY * cosZ;
        result.M12 = cosY * sinZ;
        result.M13 = -sinY;

        result.M21 = sinX * sinY * cosZ - cosX * sinZ;
        result.M22 = sinX * sinY * sinZ + cosX * cosZ;
        result.M23 = sinX * cosY;

        result.M31 = cosX * sinY * cosZ + sinX * sinZ;
        result.M32 = cosX * sinY * sinZ - sinX * cosZ;
        result.M33 = cosX * cosY;

        // Scale
        result.M11 *= Scale;
        result.M12 *= Scale;
        result.M13 *= Scale;
        result.M21 *= Scale;
        result.M22 *= Scale;
        result.M23 *= Scale;
        result.M31 *= Scale;
        result.M32 *= Scale;
        result.M33 *= Scale;

        return result;
    }

    /// <summary>
    /// Rotates this transform around the given axis by the specified angle (radians).
    /// Adds to the existing Euler rotation component.
    /// </summary>
    public ModelTransform WithRotationAdded(Vector3 axisAngle)
    {
        return new ModelTransform(Position, Rotation + axisAngle, Scale);
    }

    public override string ToString() =>
        $"Pos:({Position.X:F2},{Position.Y:F2},{Position.Z:F2}) " +
        $"Rot:({Rotation.X:F3},{Rotation.Y:F3},{Rotation.Z:F3}) " +
        $"Scl:{Scale:F2}";
}
