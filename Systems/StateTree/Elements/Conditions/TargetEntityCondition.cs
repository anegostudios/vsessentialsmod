using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that checks whether the entity is targeting an entity.
/// </summary>
public class TargetEntityCondition(
    bool invert,
    Entity entity,
    EntityBehaviorMemory memory,
    float maxDistance,
    ComplexTagCondition<TagSetFast> tags
) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        var result = 0f;
        var target = memory.TargetEntity;
        if (target != null && (maxDistance <= 0f || target.LastKnownPosition.Distance(entity.Pos.XYZ) < maxDistance))
        {
            result = tags.Matches(target.Entity.Tags) ? 1.0f : 0.0f;
        }

        return base.Evaluate(result);
    }
}
