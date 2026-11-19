using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

/// <summary>
/// Selects a target from EntityBehaviorMemory.
/// </summary>
public class MemoryTargetSelector(Entity abilityUser) : ITargetSelector
{
    public Entity GetTarget()
    {
        return EntityBehaviorMemory.TryGet(abilityUser, out var entityMemory)
            ? entityMemory.GetTargetEntity()
            : null;
    }
}
