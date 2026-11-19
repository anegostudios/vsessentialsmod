using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

/// <summary>
/// Enables the entity to use Abilities.
/// </summary>
/// <param name="entity"></param>
public class EntityBehaviorAbilities(Entity entity) : EntityBehavior(entity)
{
    private readonly Dictionary<string, Ability> abilities = [];

    public override string PropertyName() => "abilities";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        ILogger logger = entity.World.Logger;

        IEnumerable<JsonObject> abilitiesJson = attributes["Abilities"]?.AsArray();
        if (abilitiesJson == null)
        {
            logger?.Error($"Entity '{entity.Code}' has abilities behavior, but no abilities defined.");
            return;
        }

        foreach (var abilityJson in abilitiesJson)
        {
            var code = abilityJson["code"]?.AsString();
            if (code == null)
            {
                logger?.Error($"Missing 'code' field for ability on entity '{entity.Code}'");
                continue;
            }

            var id = abilityJson["Id"]?.AsString();
            if (!AssetRegistry.Collection<IAbilityAsset>().TryBuildAsset(id, entity, abilityJson, out Ability ability))
            {
                logger?.Error($"Failed to build ability for entity '{entity.Code}'");
                continue;
            }

            abilities.Add(code, ability);
        }
    }

    public bool TryGetAbility(string code, out Ability ability) => abilities.TryGetValue(code, out ability);

    public static bool TryGet(Entity entity, [NotNullWhen(true)] out EntityBehaviorAbilities? behavior)
    {
        behavior = entity.GetBehavior<EntityBehaviorAbilities>();
        if (behavior == null)
        {
            entity.World.Logger.Warning($"Failed to retrieve EntityBehaviorAbilities for entity '{entity.Code}'");
            return false;
        }

        return true;
    }
}
