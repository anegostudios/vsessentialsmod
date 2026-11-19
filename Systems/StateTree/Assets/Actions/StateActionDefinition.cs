using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Set state", "Set the NPC's current state to a named state in its states behavior.")]
public class StateActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public string State { get; set; }

    public override Action Build(Entity entity)
    {
        return EntityBehaviorStates.TryGet(entity, out EntityBehaviorStates entityStates)
            ? Build(new StateAction(Weight, entityStates, entityStates.GetStateIndexFromKey(State)))
            : null ;
    }
}
