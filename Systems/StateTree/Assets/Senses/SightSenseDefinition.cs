using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Sight", """
Allows the NPC to record entities to its memory behavior through a cone-based 'sight' check. Reads the following NPC state attributes:

sightDistance (float)
sightAngleDeg (float)
sightConeTruncateDistance (float) - ignored when sightAngleDeg is 180 or greater
""")]
public class SightSenseDefinition : SenseDefinition
{
    public override Sense Build(Entity entity, EntityBehaviorStates states)
    {
        var sightDistance = states.GetAttribute("sightDistance")?.ToObject<float>() ?? -1;
        var sightAngleDeg = states.GetAttribute("sightAngleDeg")?.ToObject<float>() ?? -1;
        var sightConeTruncateDistance = states.GetAttribute("sightConeTruncateDistance")?.ToObject<float>() ?? 0;

        if (sightDistance <= 0)
        {
            entity.World.Logger?.Warning($"Invalid sightDistance: {sightDistance}");
        }

        if (sightAngleDeg is <= 0 or > 360)
        {
            entity.World.Logger?.Warning($"Invalid sightAngleDeg: {sightAngleDeg}");
        }

        if (sightConeTruncateDistance < 0)
        {
            entity.World.Logger?.Warning($"Invalid sightConeTruncateDistance: {sightConeTruncateDistance}");
        }

        var sightHalfAngleRad = (sightAngleDeg / 2f) * GameMath.DEG2RAD;

        return new SightSense(entity, sightDistance, sightHalfAngleRad, sightConeTruncateDistance, ExcludeInanimateEntities, GetDebugDrawSystem(entity));
    }
}
