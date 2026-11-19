using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Clear target entity", "Clears the NPC's target entity from its memory behavior.")]
public class ClearTargetEntityActionDefinition : ActionDefinition
{
    public override Action Build(Entity entity)
    {
        return EntityBehaviorMemory.TryGet(entity, out EntityBehaviorMemory memory)
            ? Build(new ClearTargetEntityAction(Weight, memory))
            : null;
    }
}
