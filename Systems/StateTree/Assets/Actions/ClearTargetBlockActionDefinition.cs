using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Clear target block", "Clears the NPC's target block from its memory behavior.")]
public class ClearTargetBlockActionDefinition : ActionDefinition
{
    public override Action Build(Entity entity)
    {
        return EntityBehaviorMemory.TryGet(entity, out EntityBehaviorMemory memory)
            ? Build(new ClearTargetBlockAction(Weight, memory))
            : null;
    }
}
