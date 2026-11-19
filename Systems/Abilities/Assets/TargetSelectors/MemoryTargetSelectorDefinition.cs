using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Memory Target", "Selects a target from the ability user's memory behavior.")]
public class MemoryTargetSelectorDefinition : ITargetSelectorDefinition
{
    public ITargetSelector Build(Entity abilityUser)
    {
        return new MemoryTargetSelector(abilityUser);
    }
}
