using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("In liquid", "Checks whether the NPC is in liquid.")]
public class InLiquidConditionDefinition : BaseConditionDefinition
{
    public override ICondition Build(Entity entity)
    {
        return new InLiquidCondition(Invert, entity);
    }
}
