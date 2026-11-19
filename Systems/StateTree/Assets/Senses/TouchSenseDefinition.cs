using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Touch", "Allows the NPC to record entities to its memory behavior through a collision-based 'touch' check.")]
public class TouchSenseDefinition : SenseDefinition
{
    public override Sense Build(Entity entity, EntityBehaviorStates states)
    {
        return new TouchSense(entity, ExcludeInanimateEntities, GetDebugDrawSystem(entity));
    }
}
