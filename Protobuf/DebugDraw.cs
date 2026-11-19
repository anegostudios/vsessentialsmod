using System;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[ProtoContract]
[ProtoInclude(100, typeof(DebugDrawLine))]
[ProtoInclude(101, typeof(DebugDrawCone))]
[ProtoInclude(102, typeof(DebugDrawSphere))]
[ProtoInclude(103, typeof(DebugDrawOBB))]
public abstract class DebugDraw
{
    /// <summary>
    /// The request handle. Should be requested from DebugDrawSystem ahead of use, but can also be specified manually.
    /// </summary>
    [ProtoMember(1)]
    public long Handle;

    /// <summary>
    /// Origin block position (usually entity.Pos.AsBlockPos)
    /// </summary>
    [ProtoMember(2)]
    public BlockPos BlockPosition;

    /// <summary>
    /// Origin point, relative to <see cref="BlockPosition"/>
    /// </summary>
    [ProtoMember(3)]
    public Vec3f RelativeOrigin;

    /// <summary>
    /// RGBA color to render the shape in
    /// </summary>
    [ProtoMember(4)]
    public int Color;

    /// <summary>
    /// How long to render the shape for
    /// </summary>
    [ProtoMember(5)]
    public TimeSpan Duration = TimeSpan.FromSeconds(1d);
}
