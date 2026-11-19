using ProtoBuf;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[ProtoContract]
public class DebugDrawLine : DebugDraw
{
    /// <summary>
    /// End position relative to <see cref="DebugDraw.BlockPosition"/>
    /// </summary>
    [ProtoMember(20)]
    public Vec3f RelativeEnd;
}
