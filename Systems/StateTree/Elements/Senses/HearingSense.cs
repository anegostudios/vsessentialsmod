using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Sense that collects entities based on a hearing radius.
/// </summary>
public class HearingSense(
    Entity entity,
    float radius,
    bool excludeInanimateEntities,
    DebugDrawSystem debugDrawSystem
) : Sense(entity, excludeInanimateEntities)
{
    private readonly long? debugDrawHandle = debugDrawSystem?.RequestHandle();

    public override float GetRange() => radius;

    public override void Check(Entity[] entities, EntityBehaviorMemory memory)
    {
        var sensedAny = false;
        foreach (var e in entities)
        {
            if (CanSense(e))
            {
                memory.Record(EntityBehaviorMemory.Category.Heard, e);
                sensedAny = true;
            }
        }

        debugDrawSystem?.RequestDebugDrawSphere(new DebugDrawSphere
        {
            Handle = (long)debugDrawHandle,
            BlockPosition = self.Pos.AsBlockPos,
            RelativeOrigin = (self.Pos.XYZ - self.Pos.AsBlockPos.ToVec3d()).ToVec3f(),
            Color = sensedAny ? DebugDrawColorPositive : DebugDrawColorNegative,
            Duration = DebugDrawDuration,
            Radius = radius
        });
    }

    protected override bool CanSense(Entity other)
    {
        if (!base.CanSense(other)) return false;

        if (other.Pos.Motion.Length() == 0) return false;

        if (other is EntityPlayer player)
            return !player.OnGround || !player.Controls.Sneak;

        return true;
    }
}
