using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Launch AoE projectile", "Launches a projectile with an AoE effect on impact. Entities inside the AoE radius are hit by the ability.")]
public class LaunchAoEProjectileActionDefinition : LaunchProjectileActionDefinition
{
    /// <summary>
    /// Search radius at the impact position.
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public Variable<float> ImpactRadius { get; set; }

    public override AbilityAction Build(Entity user, IList<IEffect> onHitEffects, AnimationMetaData animationMetaData, DebugDrawSystem debugDrawSystem)
    {
        EntityProperties properties = GetProjectileProperties(user);
        double gravityFactor = GetGravityFactor(properties);

        return new LaunchAoEProjectileAction(
            user,
            onHitEffects,
            properties,
            ProjectileSpeed.GetValue(user),
            gravityFactor,
            ImpactRadius.GetValue(user),
            DespawnOnImpact,
            debugDrawSystem);
    }
}
