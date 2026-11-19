using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Memory", "Queries the NPC's memory for recorded entities.")]
public class MemoryConditionDefinition : BaseConditionDefinition
{
    /// <summary>
    /// The memory category to query against.
    /// </summary>
    [JsonProperty(Required = Required.Always)]
    public EntityBehaviorMemory.Category MemoryCategory { get; set; }

    [JsonProperty]
    [AssetSchemaMetadata(null, "The maximum time since any entities were last recorded to the collection. 0 effectively means 'now' (for checking e.g. 'am I seeing' instead of 'did I see').")]
    [DefaultValue(0)]
    public Variable<long> MaxMemoryAgeMs { get; set; }

    /// <summary>
    /// The minimum number of entities in the collection (after <see cref="MaxMemoryAgeMs"/> is applied).
    /// </summary>
    [JsonProperty]
    [DefaultValue(1)]
    public Variable<int> MinEntities { get; set; } = 1;

    [JsonProperty]
    [DefaultValue(null)]
    public ComplexTagCondition<TagSetFast> Tags { get; set; }

    public override ICondition Build(Entity entity)
    {
        return EntityBehaviorMemory.TryGet(entity, out var memory)
            ? new MemoryCondition(Invert, memory, MemoryCategory, MaxMemoryAgeMs.GetValue(entity), MinEntities.GetValue(entity), Tags)
            : throw new ArgumentException("Missing required EntityBehaviorMemory");
    }
}
