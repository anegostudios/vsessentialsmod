using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Target entity", "Checks whether the NPC has a target entity in its memory behavior. Can optionally do a max distance check.")]
public class TargetEntityConditionDefinition : BaseConditionDefinition
{
    [JsonProperty]
    [AssetSchemaMetadata(null, "The maximum distance to the target entity. A negative value disables the distance check.")]
    [DefaultValue(-1.0)]
    public Variable<float> MaxDistance { get; set; } = -1.0f;

    [JsonProperty]
    [DefaultValue(null)]
    public ComplexTagCondition<TagSetFast> Tags { get; set; }

    public override ICondition Build(Entity entity)
    {
        return EntityBehaviorMemory.TryGet(entity, out EntityBehaviorMemory memory)
            ? new TargetEntityCondition(Invert, entity, memory, MaxDistance.GetValue(entity), Tags)
            : null;
    }
}
