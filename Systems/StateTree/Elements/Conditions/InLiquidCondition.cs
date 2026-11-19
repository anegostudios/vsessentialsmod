using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that checks whether the entity is in a liquid block.
/// </summary>
public class InLiquidCondition(bool invert, Entity entity) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        return base.Evaluate(entity.Swimming ? 1.0f : 0.0f);
    }
}
