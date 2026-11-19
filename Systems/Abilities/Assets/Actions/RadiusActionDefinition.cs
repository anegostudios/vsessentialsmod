using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Radius", "Collects entities inside a sphere volume.")]
public class RadiusActionDefinition : IActionDefinition
{
    /// <summary>
    /// An optional offset for the search origin.
    /// </summary>
    [JsonProperty]
    public FastVec3d StartPositionOffset { get; set; }

    /// <summary>
    /// The search radius.
    /// </summary>
    [JsonProperty]
    [DefaultValue(5.0f)]
    public Variable<float> Radius { get; set; } = 5.0f;

    public AbilityAction Build(Entity user, IList<IEffect> onHitEffects, AnimationMetaData animationMetaData, DebugDrawSystem debugDrawSystem)
    {
        return new RadiusAction(user, onHitEffects, StartPositionOffset.ToVec3d(), Radius.GetValue(user), debugDrawSystem);
    }
}
