using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

/// <summary>
/// Effect that applies a new DamageSource to the target entity.
/// </summary>
public class DamageEffect(EnumDamageType type, float damage, float knockbackStrength) : IMultiplierAwareEffect
{
    public void Apply(Entity sourceEntity, Entity targetEntity) => Apply(sourceEntity, targetEntity, 1f);

    public void Apply(Entity sourceEntity, Entity targetEntity, float multiplier)
    {
        if (sourceEntity.World.Side != EnumAppSide.Server) return;

        var source = new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = sourceEntity,
            Type = type,
            DamageTier = 0,
            KnockbackStrength = knockbackStrength
        };

        targetEntity.ReceiveDamage(source, damage * multiplier);
    }
}
