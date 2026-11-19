using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public class AbilityEntityProjectile : EntityProjectile
{
    public Action<Entity> OnHitEntity;
    public Action<EntityPos> OnCollision;

    public bool DespawnOnImpact = false;
    private bool hasImpacted;

    public override void PreInitialize()
    {
        SetInitialRotation();
    }

    public override void OnCollided()
    {
        base.OnCollided();
        OnCollision?.Invoke(Pos);
        hasImpacted = true;
    }

    public override bool ShouldDespawn => !Alive || (DespawnOnImpact && hasImpacted);

    protected override void ImpactOnEntity(Entity target)
    {
        if (!Alive) return;

        if (World.Side == EnumAppSide.Server && CanDealDamage(target))
        {
            World.PlaySoundAt(impactSound, this, null, false, 24);

            OnHitEntity?.Invoke(target);

            DamageProjectile(target);

            if (FiredBy is EntityPlayer player)
            {
                World.PlaySoundFor(hitNotifySound, player.Player, false, 24);
            }
        }

        EntityHit = true;
        hasImpacted = true;
        msCollide = World.ElapsedMilliseconds;
        Pos.Motion.Set(0, 0, 0);
    }
}
