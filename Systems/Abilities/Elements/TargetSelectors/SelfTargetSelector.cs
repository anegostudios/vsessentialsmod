using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

/// <summary>
/// Selects the ability user as the target.
/// </summary>
public class SelfTargetSelector(Entity abilityUser): ITargetSelector
{
    public Entity GetTarget()
    {
        return abilityUser;
    }
}
