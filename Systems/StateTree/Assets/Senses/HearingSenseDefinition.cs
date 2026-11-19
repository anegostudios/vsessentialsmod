using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Hearing", """
Allows the NPC to record moving entities to its memory behavior through a sphere-based 'hearing' check. Reads the following NPC state attributes:

hearingRadius (float)
""")]
public class HearingSenseDefinition : SenseDefinition
{
    public override Sense Build(Entity entity, EntityBehaviorStates states)
    {
        var hearingRadius = states.GetAttribute("hearingRadius")?.ToObject<float>() ?? -1;

        if (hearingRadius <= 0)
        {
            entity.World.Logger?.Warning($"Invalid sense parameters: hearingRadius={hearingRadius}");
        }

        return new HearingSense(entity, hearingRadius, ExcludeInanimateEntities, GetDebugDrawSystem(entity));
    }
}
