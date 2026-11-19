using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Sense that collects entities based on a sight cone.
/// </summary>
public class SightSense(
    Entity entity,
    float sightDistance,
    float sightHalfAngleRad,
    float sightConeTruncateDistance,
    bool excludeInanimateEntities,
    DebugDrawSystem debugDrawSystem)
    : Sense(entity, excludeInanimateEntities)
{
    private readonly long? debugDrawHandle = debugDrawSystem?.RequestHandle();

    public override float GetRange() => sightDistance;

    private ConeShape cone = new(sightDistance, sightHalfAngleRad, sightConeTruncateDistance);

    public override void Check(Entity[] entities, EntityBehaviorMemory memory)
    {
        cone.Origin = self.Pos.XYZ + self.LocalEyePos;
        // NPCs have an inverted view vector for some reason
        cone.Axis = self.Pos.GetViewVector().Normalize().ToVec3d() * -1f;

        var sensedAny = false;
        foreach (var e in entities)
        {
            if (!CanSense(e)) continue;

            Vec3d targetCenter = e.Pos.XYZ + e.SelectionBox.Center;

            if (!cone.Contains(targetCenter)) continue;

            if (e.HasBehavior<EntityBehaviorVisibility>() && !e.GetBehavior<EntityBehaviorVisibility>()!.IsVisibleTo(self))
            {
                continue;
            }

            memory.Record(EntityBehaviorMemory.Category.Saw, e);
            sensedAny = true;
        }

        debugDrawSystem?.RequestDebugDrawCone(new DebugDrawCone
        {
            Handle = (long)debugDrawHandle,
            BlockPosition = self.Pos.AsBlockPos,
            Axis = cone.Axis,
            RelativeOrigin = (cone.Origin - self.Pos.AsBlockPos.ToVec3d()).ToVec3f(),
            Length = sightDistance,
            HalfAngleRad = sightHalfAngleRad,
            TruncateDistance = cone.TruncateDistance,
            Color = sensedAny ? DebugDrawColorPositive : DebugDrawColorNegative
        });
    }
}
