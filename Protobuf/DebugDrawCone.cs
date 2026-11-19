using ProtoBuf;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[ProtoContract]
public class DebugDrawCone : DebugDraw
{
    /// <summary>
    /// Normalized direction axis
    /// </summary>
    [ProtoMember(20)]
    public Vec3d Axis;

    /// <summary>
    /// Length of the cone in blocks
    /// </summary>
    [ProtoMember(21)]
    public float Length;

    /// <summary>
    /// Half-angle of the cone in radians
    /// </summary>
    [ProtoMember(22)]
    public float HalfAngleRad;

    /// <summary>
    /// Truncate distance in blocks
    /// </summary>
    [ProtoMember(23)]
    public float TruncateDistance = 0;

    /// <summary>
    /// The number of line segments to use
    /// </summary>
    [ProtoMember(24)]
    public int NumSegments = 8;
}
