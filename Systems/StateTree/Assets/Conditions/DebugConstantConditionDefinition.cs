using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Debug constant", "Returns a constant value for debugging/testing.")]
public class DebugConstantConditionDefinition : BaseConditionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public float Value { get; set; }

    public override ICondition Build(Entity entity)
    {
        return new DebugConstantCondition(Invert, Value);
    }
}
