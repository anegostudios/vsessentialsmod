using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[PolymorphicAssetElement("Target Selector")]
[JsonConverter(typeof(AssetElementJsonConverter<IAbilityAsset, ITargetSelectorDefinition>))]
public interface ITargetSelectorDefinition
{
    public ITargetSelector Build(Entity abilityUser);
}
