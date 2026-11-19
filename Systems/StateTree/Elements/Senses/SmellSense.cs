using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Sense that collects entities based on scent.
/// </summary>
public class SmellSense(
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
            if (!CanSense(e)) continue;

            // TODO: Check if the entity is emitting a scent. For now, just assume it is.
            memory.Record(EntityBehaviorMemory.Category.Smelled, e);
            sensedAny = true;
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
}
