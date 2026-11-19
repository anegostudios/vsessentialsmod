using ProtoBuf;

namespace Vintagestory.GameContent;

[ProtoContract]
public class DebugDrawSphere : DebugDraw
{
    /// <summary>
    /// Radius of the sphere
    /// </summary>
    [ProtoMember(20)]
    public float Radius;
}
