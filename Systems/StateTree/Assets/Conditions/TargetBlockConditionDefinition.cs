using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Target block", "Checks whether the NPC has a target block in its memory behavior. Can optionally do a max distance check.")]
public class TargetBlockConditionDefinition : BaseConditionDefinition
{
    [JsonProperty]
    [AssetSchemaMetadata(null, "The maximum distance to the target block. A negative value disables the distance check.")]
    [DefaultValue(-1.0)]
    public Variable<float> MaxDistance { get; set; } = -1.0f;

    public override ICondition Build(Entity entity)
    {
        return EntityBehaviorMemory.TryGet(entity, out var memory)
            ? new TargetBlockCondition(Invert, entity, memory, MaxDistance.GetValue(entity))
            : throw new ArgumentException("Missing required EntityBehaviorMemory");
    }
}
