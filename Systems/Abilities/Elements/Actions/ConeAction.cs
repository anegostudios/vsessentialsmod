using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Action that applies to entities inside a cone shape.
/// </summary>
public class ConeAction(
    Entity abilityUser,
    IList<IEffect> onHitEffects,
    float coneLength,
    float coneHalfAngleRad,
    float coneTruncateDistance,
    DebugDrawSystem debugDrawSystem
) : AbilityAction(abilityUser, onHitEffects)
{
    private readonly Entity abilityUser = abilityUser;
    private readonly long? debugDrawHandle = debugDrawSystem?.RequestHandle();

    private ConeShape cone = new(coneLength, coneHalfAngleRad, coneTruncateDistance);

    public override void Run(ITargetSelector targetSelector)
    {
        base.Run(targetSelector);

        if (coneLength <= 0 || coneHalfAngleRad <= 0) return;

        var target = targetSelector.GetTarget();

        var entityEyePos = (target.Pos.XYZ + target.LocalEyePos).ToVec3f() - target.Pos.AsBlockPos.ToVec3f();
        var entityLookDir = target.Pos.GetViewVector().NormalizedCopy();
        if (target is not EntityPlayer) entityLookDir *= -1f;

        cone.Origin = target.Pos.XYZ + target.LocalEyePos;
        cone.Axis = entityLookDir.ToVec3d();

        var hits = target.World.GetEntitiesAround(cone.Origin, coneLength, coneLength, nearbyEntity =>
        {
            if (nearbyEntity.EntityId == abilityUser.EntityId || nearbyEntity is IProjectile) return false;

            Vec3d targetCenter = nearbyEntity.Pos.XYZ + nearbyEntity.SelectionBox.Center;

            return cone.Contains(targetCenter);
        });

        OnHit(hits);

        debugDrawSystem?.RequestDebugDrawCone(new DebugDrawCone
        {
            Handle = (long)debugDrawHandle,
            BlockPosition = target.Pos.AsBlockPos,
            Axis = cone.Axis,
            RelativeOrigin = entityEyePos,
            Length = coneLength,
            HalfAngleRad = coneHalfAngleRad,
            TruncateDistance = cone.TruncateDistance,
            Color = DebugDrawColor,
            Duration = DebugDrawDuration
        });
    }
}
