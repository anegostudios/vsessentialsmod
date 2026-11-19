using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that queries <see cref="EntityBehaviorMemory"/>.
/// </summary>
public class MemoryCondition(
    bool invert,
    EntityBehaviorMemory memory,
    EntityBehaviorMemory.Category memoryCategory,
    long maxMemoryAge,
    int minEntities,
    ComplexTagCondition<TagSetFast> tags
) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        int count = memory.Count(memoryCategory, maxMemoryAge, tags);
        return base.Evaluate(count >= minEntities ? 1f : 0f);
    }
}
