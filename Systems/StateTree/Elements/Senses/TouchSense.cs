using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Sense that collects entities based on intersection/touch.
/// </summary>
public class TouchSense(Entity entity, bool excludeInanimateEntities, DebugDrawSystem? debugDrawSystem)
    : Sense(entity, excludeInanimateEntities)
{
    private readonly long? debugDrawHandle = debugDrawSystem?.RequestHandle();
    private readonly int debugDrawColorNegative = ColorUtil.ColorFromRgba(43, 105, 251, 255);

    private const float CheckRadius = 3.0f;

    public override float GetRange() => CheckRadius;

    public override void Check(Entity[] entities, EntityBehaviorMemory memory)
    {
        var sensedAny = false;
        foreach (var e in entities)
        {
            if (!CanSense(e)) continue;

            Cuboidd a = self.CollisionBox.OffsetCopyDouble(self.Pos.X, self.Pos.Y, self.Pos.Z);
            Cuboidd b = e.CollisionBox.OffsetCopyDouble(e.Pos.X, e.Pos.Y, e.Pos.Z);
            if (!a.IntersectsOrTouches(b)) continue;

            memory.Record(EntityBehaviorMemory.Category.Touched, e);
            sensedAny = true;
        }

        debugDrawSystem?.RequestDebugDrawSphere(new DebugDrawSphere
        {
            Handle = (long)debugDrawHandle,
            BlockPosition = self.Pos.AsBlockPos,
            RelativeOrigin = (self.Pos.XYZ - self.Pos.AsBlockPos.ToVec3d()).ToVec3f(),
            Color = sensedAny ? DebugDrawColorPositive : debugDrawColorNegative,
            Duration = DebugDrawDuration,
            Radius = CheckRadius
        });
    }
}
