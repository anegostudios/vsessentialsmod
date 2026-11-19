using System;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// A cone volume that optionally has its tip truncated away.
/// </summary>
public struct ConeShape(float length, float halfAngleRad, float truncateDistance)
{
    /// <summary>
    /// The point to project the cone from, in world space.
    /// </summary>
    public Vec3d Origin;

    /// <summary>
    /// The direction the cone points in. Must be normalized.
    /// </summary>
    public Vec3d Axis;

    /// <summary>
    /// How far the cone reaches from the origin, in blocks.
    /// </summary>
    public readonly float Length = length;

    public readonly float HalfAngleCos = (float)Math.Cos(halfAngleRad);

    /// <summary>
    /// How much to truncate the cone from its tip, in blocks. This is forced to 0f when the cone's full angle >= 180°
    /// since truncation is unlikely to be useful in that scenario and would just complicate things.
    /// </summary>
    public readonly float TruncateDistance = halfAngleRad >= MathF.PI / 2 ? 0f : MathF.Max(0f, truncateDistance);

    /// <summary>
    /// Whether the given point lies inside the volume.
    /// </summary>
    public bool Contains(Vec3d point)
    {
        var offset = point - Origin;
        if (offset.LengthSq() > (double)Length * Length) return false;

        if (TruncateDistance > 0 && offset.Dot(Axis) < 0) return false;

        var vector = offset + Axis * TruncateDistance;
        return vector.Dot(Axis) >= vector.Length() * HalfAngleCos;
    }
}
