using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("World time", "Checks whether the current world time is within a specified time range.")]
public class WorldTimeConditionDefinition : BaseConditionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public Variable<float> After { get; set; }

    [JsonProperty(Required = Required.Always)]
    public Variable<float> Before { get; set; }

    public override ICondition Build(Entity entity)
    {
        if (entity.World?.Calendar == null)
            throw new ArgumentException("Entity has no world calendar");

        return new WorldTimeCondition(Invert, entity.World.Calendar, After.GetValue(entity), Before.GetValue(entity));
    }
}
