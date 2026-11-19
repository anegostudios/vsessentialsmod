using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Smell", """
Allows the NPC to record entities to its memory behavior through a simple sphere-based 'smell' check. Reads the following NPC state attributes:

smellRadius (float)
""")]
public class SmellSenseDefinition : SenseDefinition
{
    public override Sense Build(Entity entity, EntityBehaviorStates states)
    {
        var smellRadius = states.GetAttribute("smellRadius")?.ToObject<float>() ?? -1;

        if (smellRadius <= 0)
        {
            entity.World.Logger?.Warning($"Invalid sense parameter: smellRadius={smellRadius}");
        }

        return new SmellSense(entity, smellRadius, ExcludeInanimateEntities, GetDebugDrawSystem(entity));
    }
}
