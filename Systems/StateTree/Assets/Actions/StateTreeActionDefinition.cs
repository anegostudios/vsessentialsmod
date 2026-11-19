using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Run state tree", "Runs another state tree by ID. This will automatically ensure any duplicated senses are only checked once per tick. Be careful about infinite recursion!")]
public class StateTreeActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public string Id { get; set; }

    public override Action Build(Entity entity)
    {
        if (!EntityBehaviorStateTree.TryGet(entity, out EntityBehaviorStateTree entityStateTree))
            return null;

        var tree = entityStateTree.RequestActionStateTree(Id);
        if (tree == null)
        {
            entity.Api?.Logger?.Error($"StateTreeActionDefinition: Failed to obtain sub-tree '{Id}' for entity '{entity.Code}'");
            return null;
        }

        return Build(new StateTreeAction(Weight, tree));
    }
}
