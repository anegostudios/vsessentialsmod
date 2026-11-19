using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("State", "Checks the NPC's current states state.")]
public class StateConditionDefinition : BaseConditionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public string State { get; set; }

    public override ICondition Build(Entity entity)
    {
        return EntityBehaviorStates.TryGet(entity, out var entityStates)
            ? new StateCondition(Invert, entityStates, entityStates.GetStateIndexFromKey(State))
            : throw new ArgumentException("Missing required EntityBehaviorStates");
    }
}
