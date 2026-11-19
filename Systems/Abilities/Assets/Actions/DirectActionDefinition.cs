using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Direct", "Forwards the entity directly from the target selector.")]
public class DirectActionDefinition : IActionDefinition
{
    public AbilityAction Build(Entity user, IList<IEffect> onHitEffects, AnimationMetaData animationMetaData, DebugDrawSystem debugDrawSystem)
    {
        return new DirectAction(user, onHitEffects);
    }
}
