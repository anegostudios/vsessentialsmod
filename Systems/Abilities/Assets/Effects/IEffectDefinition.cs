using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[PolymorphicAssetElement("Effect")]
[JsonConverter(typeof(AssetElementJsonConverter<IAbilityAsset, IEffectDefinition>))]
public interface IEffectDefinition
{
    public IEffect Build(Entity entity);
}
