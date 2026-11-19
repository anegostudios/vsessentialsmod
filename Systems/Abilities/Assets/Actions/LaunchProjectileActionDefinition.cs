using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Launch projectile", "Launches a projectile. The first entity it collides with is hit by the ability.")]
public class LaunchProjectileActionDefinition : IActionDefinition
{
    /// <summary>
    /// The AbilityEntityProjectile asset to use.
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public AssetLocation ProjectileAsset { get; set; }

    /// <summary>
    /// Launch speed in blocks per second.
    /// </summary>
    [JsonProperty]
    [DefaultValue(15)]
    public Variable<double> ProjectileSpeed { get; set; } = 15;

    [JsonProperty]
    [AssetSchemaMetadata(null, "Specifies whether the projectile should despawn when it collides with the world or hits an entity.")]
    [DefaultValue(true)]
    public bool DespawnOnImpact { get; set; } = true;

    protected EntityProperties GetProjectileProperties(Entity user)
    {
        EntityProperties properties = user.World.GetEntityType(ProjectileAsset);
        if (properties == null)
        {
            throw new System.Exception($"LaunchProjectileActionDefinition: attempted to use non-existent projectile {ProjectileAsset}");
        }

        return properties;
    }

    protected double GetGravityFactor(EntityProperties properties)
    {
        // Behaviors aren't initialized until the projectile is spawned, so we have to extract gravityFactor from the JSON if it's there
        double gravityFactor = 1.0;
        foreach (var behaviorJson in properties.Server.BehaviorsAsJsonObj)
        {
            if (behaviorJson["code"]?.AsString() == "passivephysics")
            {
                gravityFactor = behaviorJson["gravityFactor"].AsDouble(gravityFactor);
            }
        }

        return gravityFactor;
    }

    public virtual AbilityAction Build(Entity user, IList<IEffect> onHitEffects, AnimationMetaData animationMetaData, DebugDrawSystem debugDrawSystem)
    {
        EntityProperties properties = GetProjectileProperties(user);
        double gravityFactor = GetGravityFactor(properties);

        return new LaunchProjectileAction(
            user,
            onHitEffects,
            properties,
            ProjectileSpeed.GetValue(user),
            gravityFactor,
            DespawnOnImpact);
    }
}
