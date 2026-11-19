using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Action that aims and launches a projectile at its target. On impact, it applies effects to entities within the given
/// impact radius.
/// </summary>
public class LaunchAoEProjectileAction(
    Entity abilityUser,
    IList<IEffect> onHitEffects,
    EntityProperties projectileProperties,
    double projectileSpeed,
    double gravityFactor,
    float impactRadius,
    bool despawnOnImpact,
    DebugDrawSystem debugDrawSystem
) : LaunchProjectileAction(abilityUser, onHitEffects, projectileProperties, projectileSpeed, gravityFactor, despawnOnImpact)
{
    private readonly long? debugDrawHandle = debugDrawSystem?.RequestHandle();

    protected override void InitEvents(AbilityEntityProjectile projectile)
    {
        base.InitEvents(projectile);
        projectile.OnCollision += OnProjectileImpact;
    }

    protected override void OnProjectileHit(Entity entity)
    {
        HandleImpact(entity.Pos, entity.SelectionBox.Center);
    }

    private void OnProjectileImpact(EntityPos impactPosition)
    {
        HandleImpact(impactPosition, Vec3d.Zero);
    }

    private void HandleImpact(EntityPos impactPosition, Vec3d impactOffset)
    {
        var worldOrigin = impactPosition.XYZ + impactOffset;
        var localOrigin = worldOrigin - impactPosition.AsBlockPos.ToVec3d();

        debugDrawSystem?.RequestDebugDrawSphere(new DebugDrawSphere
        {
            Handle = (long)debugDrawHandle,
            BlockPosition = impactPosition.AsBlockPos,
            RelativeOrigin = localOrigin.ToVec3f(),
            Color = DebugDrawColor,
            Duration = DebugDrawDuration,
            Radius = impactRadius
        });

        var targets = abilityUser.World.GetEntitiesAround(worldOrigin, impactRadius, impactRadius,
            e => e.EntityId != abilityUser.EntityId);

        OnHit(targets);
    }
}
