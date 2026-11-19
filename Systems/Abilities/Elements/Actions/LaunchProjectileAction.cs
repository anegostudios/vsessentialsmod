using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Action that aims and launches a projectile at its target. It applies effects to the entity hit by the projectile.
/// </summary>
public class LaunchProjectileAction(
    Entity abilityUser,
    IList<IEffect> onHitEffects,
    EntityProperties projectileProperties,
    double projectileSpeed,
    double gravityFactor,
    bool despawnOnImpact
) : AbilityAction(abilityUser, onHitEffects)
{
    public override void Run(ITargetSelector targetSelector)
    {
        base.Run(targetSelector);

        var target = targetSelector.GetTarget();
        if (target == null) return;

        var world = abilityUser.World;

        if (world.ClassRegistry.CreateEntity(projectileProperties) is not AbilityEntityProjectile projectile) return;

        projectile.FiredBy = abilityUser;
        projectile.World = world;
        projectile.Pos.SetPosWithDimension(abilityUser.Pos.XYZ.Add(0, abilityUser.LocalEyePos.Y, 0));
        projectile.DespawnOnImpact = despawnOnImpact;

        InitEvents(projectile);
        Fire(projectile, target);

        projectile.PreInitialize();
        abilityUser.World.SpawnEntity(projectile);
    }

    protected virtual void InitEvents(AbilityEntityProjectile projectile)
    {
        projectile.OnHitEntity += OnProjectileHit;
    }

    protected virtual void OnProjectileHit(Entity entity) => OnHit([entity]);

    private void Fire(AbilityEntityProjectile projectile, Entity target)
    {
        Vec3d pos = abilityUser.Pos.XYZ.Add(0, abilityUser.LocalEyePos.Y, 0);
        Vec3d targetPos = target.Pos.XYZ.Add(0, target.LocalEyePos.Y, 0);

        double speedPerTick = projectileSpeed / 60f;
        double distance = (targetPos - pos).Length();
        double approximateTimeTicks = distance / speedPerTick;

        targetPos += target.Pos.Motion * approximateTimeTicks;

        double gravityAcceleration = gravityFactor * GlobalConstants.GravityPerSecond / 60;
        Vec3d gravity = new Vec3d(0, -gravityAcceleration, 0);

        if (!BallisticsUtil.TrySolve(pos, targetPos, speedPerTick, gravity, out var aimDirection))
        {
            aimDirection = (targetPos - pos).Normalize();
        }

        projectile.Pos.Motion.Set(aimDirection * speedPerTick);
        projectile.SetInitialRotation();
    }
}
