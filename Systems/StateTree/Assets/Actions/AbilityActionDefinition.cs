using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Execute ability", "Executes an ability by 'code'. The ability must be included in the NPC's abilities server behavior.")]
public class AbilityActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public string Ability { get; set; }

    public override Action Build(Entity entity)
    {
        if (EntityBehaviorAbilities.TryGet(entity, out var entityAbilities))
        {
            if (entityAbilities.TryGetAbility(Ability, out var ability))
            {
                return Build(new AbilityAction(Weight, ability));
            }
        }

        return null;
    }
}
