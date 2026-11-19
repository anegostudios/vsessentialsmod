using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[PolymorphicAssetElement("Action")]
[JsonConverter(typeof(AssetElementJsonConverter<IAbilityAsset, IActionDefinition>))]
public interface IActionDefinition
{
    public AbilityAction Build(
        Entity user,
        IList<IEffect> onHitEffects,
        AnimationMetaData animation,
        DebugDrawSystem debugDrawSystem);
}
