using ProtoBuf;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[ProtoContract]
public class DebugDrawOBB : DebugDraw
{
    [ProtoMember(20)]
    public float[] LocalMatrix;

    [ProtoMember(21)]
    public Vec3f LocalMin;

    [ProtoMember(22)]
    public Vec3f LocalMax;
}
