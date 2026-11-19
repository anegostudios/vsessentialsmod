using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Health", "Does a comparison check against the NPC's current health percentage.")]
public class HealthConditionDefinition : BaseConditionDefinition
{
    [JsonProperty]
    public ComparisonType ComparisonType { get; set; }

    [JsonProperty]
    [AssetSchemaMetadata(null, "Value between 0.0 and 1.0.")]
    public Variable<float> Value { get; set; }

    public override ICondition Build(Entity entity)
    {
        return new HealthCondition(Invert, entity.WatchedAttributes.GetTreeAttribute("health"), ComparisonType, Value.GetValue(entity));
    }
}
