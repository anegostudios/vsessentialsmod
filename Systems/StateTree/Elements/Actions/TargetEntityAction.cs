using System;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that sets the entity's current target entity from memory.
/// </summary>
public class TargetEntityAction(
    float weight,
    EntityBehaviorMemory memory,
    EntityBehaviorMemory.Category memoryCategory,
    TargetEntityActionMode mode,
    long lockDurationMs,
    long clearAfterMs
) : Action(weight)
{
    private bool SelectTarget(EntityBehaviorMemory.Category category)
    {
        EntityMemory? target = mode switch
        {
            TargetEntityActionMode.MostRecent => memory.Newest(category),
            TargetEntityActionMode.Random => memory.Random(category),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        if (target == null) return false;

        memory.SetTargetEntity(target.Value, lockDurationMs, clearAfterMs);
        return true;
    }

    public override void Execute(float deltaTime)
    {
        if (memoryCategory == EntityBehaviorMemory.Category.Any)
        {
            foreach (var category in EntityBehaviorMemory.AllCategories)
            {
                if (SelectTarget(category)) break;
            }

            return;
        }

        SelectTarget(memoryCategory);
    }
}
