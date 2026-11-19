using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Action that applies to entities within a radius of its target.
/// </summary>
public class RadiusAction(
    Entity abilityUser,
    IList<IEffect> onHitEffects,
    Vec3d startPositionOffset,
    float radius,
    DebugDrawSystem debugDrawSystem
) : AbilityAction(abilityUser, onHitEffects)
{
    private readonly long? debugDrawHandle = debugDrawSystem?.RequestHandle();

    public override void Run(ITargetSelector targetSelector)
    {
        base.Run(targetSelector);

        var target = targetSelector.GetTarget();

        var worldOrigin = target.Pos.XYZ + target.SelectionBox.Center + startPositionOffset;
        var localOrigin = worldOrigin - target.Pos.AsBlockPos.ToVec3d();

        debugDrawSystem?.RequestDebugDrawSphere(new DebugDrawSphere
        {
            Handle = (long)debugDrawHandle,
            BlockPosition = target.Pos.AsBlockPos,
            RelativeOrigin = localOrigin.ToVec3f(),
            Color = DebugDrawColor,
            Duration = DebugDrawDuration,
            Radius = radius
        });

        var targets = target.World.GetEntitiesAround(worldOrigin, radius, radius,
            e => e.EntityId != target.EntityId);

        OnHit(targets);
    }
}
