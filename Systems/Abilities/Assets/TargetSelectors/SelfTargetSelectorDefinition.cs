using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Self-Target", "Selects the ability user as the target.")]
public class SelfTargetSelectorDefinition : ITargetSelectorDefinition
{
    public ITargetSelector Build(Entity abilityUser)
    {
        return new SelfTargetSelector(abilityUser);
    }
}
