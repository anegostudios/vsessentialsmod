using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that checks whether the entity is targeting a block.
/// </summary>
public class TargetBlockCondition(
    bool invert,
    Entity entity,
    EntityBehaviorMemory memory,
    float maxDistance
) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        var result = 0f;
        if (memory.TargetBlock != null)
        {
            if (maxDistance > 0.0f)
            {
                result = memory.TargetBlock.Position.DistanceTo(entity.Pos.XYZ) < maxDistance
                    ? 1.0f : 0.0f;
            }
            else
            {
                result = 1f;
            }
        }

        return base.Evaluate(result);
    }
}
