using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Cone", "Collects entities inside a cone volume.")]
public class ConeActionDefinition : IActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    [AssetSchemaMetadata(null, "How far to project the cone in blocks.")]
    public Variable<float> Length { get; set; }

    [JsonProperty(Required = Required.Always)]
    [AssetSchemaMetadata(null, "The cone angle in degrees.")]
    public Variable<float> Angle { get; set; }

    [JsonProperty(Required = Required.Always)]
    [AssetSchemaMetadata(null, "Set to a positive value (in blocks) to truncate the cone. A higher value widens the near end of the cone. Values of 0 or below are ineffective. This property is not used when Angle is 180 or greater.")]
    [DefaultValue(0.0f)]
    public Variable<float> TruncateDistance { get; set; }

    public AbilityAction Build(Entity user, IList<IEffect> onHitEffects, AnimationMetaData animationMetaData, DebugDrawSystem debugDrawSystem)
    {
        var halfAngleRad = (Angle.GetValue(user) / 2f) * GameMath.DEG2RAD;
        return new ConeAction(user, onHitEffects, Length.GetValue(user), halfAngleRad, TruncateDistance.GetValue(user), debugDrawSystem);
    }
}
